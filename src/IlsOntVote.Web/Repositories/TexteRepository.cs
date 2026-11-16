using Dapper;
using IlsOntVote.Web.Data;
using IlsOntVote.Web.Models;

namespace IlsOntVote.Web.Repositories;

/// <summary>Filtres de la liste des lois : mots du titre, statut (clé de liste blanche) et chambre (an | senat).</summary>
public sealed record FiltreTextes(string? Recherche, string? Statut, string? Chambre);

/// <summary>Accès aux textes de loi (dossiers législatifs) et à leur parcours : lecture, liste filtrée, triée et paginée.</summary>
public interface ITexteRepository
{
    Task<Texte?> GetAsync(string id);
    Task<Texte?> GetParScrutinAsync(string scrutinId);
    Task<IReadOnlyList<ScrutinDuTexte>> GetScrutinsAsync(string texteId);

    /// <summary>Textes ayant au moins un vote sur l'ensemble, triés (liste blanche) et paginés. parPage = 0 : tout (export).</summary>
    Task<Resultats<Texte>> ListerAsync(FiltreTextes filtre, string tri, bool decroissant, int page, int parPage);
}

public sealed class TexteRepository(DapperContext db) : ITexteRepository
{
    private const string ColonnesTexte =
        """
        t.id, t.type_libelle, t.titre, t.etat_code, t.etat_libelle, t.numero_loi, t.date_promulgation, t.date_jo, t.numero_jo,
        t.url_legifrance, t.url_dossier_senat, t.url_dossier_an, t.saisine_cc_date, t.saisine_cc_par, t.decision_cc_numero,
        t.decision_cc_date, t.decision_cc_code, t.decision_cc_libelle, t.decision_cc_url
        """;

    // Agrégat des scrutins rattachés à un texte. L'adoption se lit sur le résultat du vote sur l'ensemble
    // (`sort` commence par « adopt ») : un texte peut avoir plusieurs votes par chambre (lectures successives),
    // « adopté » signifie donc « au moins un vote sur l'ensemble adopté dans cette chambre ».
    private const string Agregat =
        """
        max(s.date_scrutin) AS dernier_vote,
        count(s.id)::int AS nb_scrutins,
        COALESCE(bool_or(s.chambre = 'AN' AND s.sort LIKE 'adopt%'), false) AS adopte_an,
        COALESCE(bool_or(s.chambre = 'SENAT' AND s.sort LIKE 'adopt%'), false) AS adopte_senat,
        (count(s.id) FILTER (WHERE s.chambre = 'AN'))::int AS nb_scrutins_an,
        (count(s.id) FILTER (WHERE s.chambre = 'SENAT'))::int AS nb_scrutins_senat,
        max(s.date_scrutin) FILTER (WHERE s.chambre = 'AN' AND s.sort LIKE 'adopt%') AS date_adoption_an,
        max(s.date_scrutin) FILTER (WHERE s.chambre = 'SENAT' AND s.sort LIKE 'adopt%') AS date_adoption_senat
        """;

    // Colonnes triables : clé d'URL -> expression SQL. JAMAIS de SQL construit depuis l'URL.
    private static readonly Dictionary<string, string> Tris = new()
    {
        ["vote"] = "dernier_vote",
        ["titre"] = "titre",
        ["type"] = "type_libelle",
        ["promulgation"] = "date_promulgation",
        ["jo"] = "date_jo",
        ["numero"] = "numero_loi",
        ["scrutins"] = "nb_scrutins",
        ["statut"] = "statut_rang",
    };

    public static IEnumerable<string> ClesTri => Tris.Keys;

    // Rang de la colonne « statut » : doit rester cohérent avec Texte.Statut (C#). Les censurés d'abord, puis les
    // promulgués, puis les adoptés par les deux assemblées sans promulgation, puis le reste.
    private const string RangStatut =
        """
        CASE WHEN decision_cc_code = '03' THEN 1
             WHEN date_promulgation IS NOT NULL AND decision_cc_code = '02' THEN 3
             WHEN date_promulgation IS NOT NULL THEN 2
             WHEN etat_code = '01' AND adopte_an AND adopte_senat THEN 4
             WHEN etat_code = '01' THEN 5
             WHEN etat_code = '04' THEN 2
             ELSE 6 END
        """;

    // Filtres de statut : fragments SQL FIXES choisis par une liste blanche, jamais construits depuis l'URL.
    // « non-promulgue » = adopté par au moins une assemblée, ni promulgué, ni rejeté/retiré.
    private static string FiltreStatut(string? statut) => statut switch
    {
        "promulgue" => "AND date_promulgation IS NOT NULL",
        "non-promulgue" => "AND date_promulgation IS NULL AND etat_code IN ('01','04') AND (adopte_an OR adopte_senat)",
        "deux-assemblees" => "AND date_promulgation IS NULL AND etat_code = '01' AND adopte_an AND adopte_senat",
        "discussion" => "AND etat_code = '01'",
        "rejete" => "AND etat_code IN ('03','05','06')",
        "cc" => "AND decision_cc_code IS NOT NULL",
        "censure" => "AND decision_cc_code IN ('02','03')",
        _ => "",
    };

    public async Task<Texte?> GetAsync(string id)
    {
        using var conn = db.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<Texte>(
            $"""
             SELECT {ColonnesTexte}, {Agregat}
               FROM textes t LEFT JOIN scrutins s ON s.texte_id = t.id
              WHERE t.id = @id GROUP BY t.id
             """, new { id });
    }

    public async Task<Texte?> GetParScrutinAsync(string scrutinId)
    {
        using var conn = db.CreateConnection();
        var texteId = await conn.ExecuteScalarAsync<string?>("SELECT texte_id FROM scrutins WHERE id = @scrutinId", new { scrutinId });
        return texteId is null ? null : await GetAsync(texteId);
    }

    public async Task<IReadOnlyList<ScrutinDuTexte>> GetScrutinsAsync(string texteId)
    {
        using var conn = db.CreateConnection();
        return (await conn.QueryAsync<ScrutinDuTexte>(
            """
            SELECT id, chambre, date_scrutin, titre, sort, nb_pour, nb_contre, nb_abstentions
              FROM scrutins WHERE texte_id = @texteId ORDER BY date_scrutin, chambre, numero
            """, new { texteId })).ToList();
    }

    public async Task<Resultats<Texte>> ListerAsync(FiltreTextes filtre, string tri, bool decroissant, int page, int parPage)
    {
        using var conn = db.CreateConnection();
        page = Math.Max(1, page);

        var mots = (filtre.Recherche ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(6).Select(m => "%" + m.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%").ToArray();
        var chambre = filtre.Chambre switch { "an" => "AN", "senat" => "SENAT", "ue" => "UE", _ => null };

        var colonne = Tris.GetValueOrDefault(tri, "dernier_vote");
        var sens = decroissant ? "DESC" : "ASC";

        // Les scrutins filtrés par chambre alimentent l'agrégat ; un texte sans vote dans cette chambre disparaît.
        // JOIN (et non LEFT JOIN) : cette page ne liste que les textes ayant au moins un vote nominatif rattaché.
        var cte =
            $"""
             WITH base AS (
                 SELECT {ColonnesTexte}, {Agregat}
                   FROM textes t JOIN scrutins s ON s.texte_id = t.id
                  WHERE (@chambre IS NULL OR s.chambre = @chambre) AND t.titre ILIKE ALL (@mots)
                  GROUP BY t.id
             ), classe AS (
                 SELECT *, {RangStatut} AS statut_rang FROM base
             )
             """;
        var where = $"WHERE TRUE {FiltreStatut(filtre.Statut)}";
        var p = new { chambre, mots, limit = parPage, offset = (page - 1) * parPage };

        var total = await conn.ExecuteScalarAsync<int>($"{cte} SELECT count(*) FROM classe {where}", p);
        var pagination = parPage > 0 ? "LIMIT @limit OFFSET @offset" : "LIMIT 50000";
        var lignes = (await conn.QueryAsync<Texte>(
            $"{cte} SELECT * FROM classe {where} ORDER BY {colonne} {sens} NULLS LAST, titre {pagination}", p)).ToList();

        return new Resultats<Texte>(lignes, total, page, parPage == 0 ? Math.Max(1, total) : parPage);
    }
}
