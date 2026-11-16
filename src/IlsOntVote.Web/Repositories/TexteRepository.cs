using Dapper;
using IlsOntVote.Web.Data;
using IlsOntVote.Web.Models;

namespace IlsOntVote.Web.Repositories;

/// <summary>Filtres de la liste des lois : mots du titre, statut (clé de liste blanche) et chambre (an | senat).</summary>
public sealed record FiltreTextes(string? Recherche, string? Statut, string? Chambre, DateTime? Du = null, DateTime? Au = null);

/// <summary>Accès aux textes de loi (dossiers législatifs) et à leur parcours : lecture, liste filtrée, triée et paginée.</summary>
public interface ITexteRepository
{
    Task<Texte?> GetAsync(string id);
    Task<Texte?> GetParScrutinAsync(string scrutinId);
    Task<IReadOnlyList<ScrutinDuTexte>> GetScrutinsAsync(string texteId);
    /// <summary>Décisions de séance publique à l'Assemblée sur ce texte (avec ou sans scrutin public), de la plus ancienne à la plus récente.</summary>
    Task<IReadOnlyList<EtapeTexte>> GetEtapesAsync(string texteId);

    /// <summary>Textes ayant au moins un vote sur l'ensemble, triés (liste blanche) et paginés. parPage = 0 : tout (export).</summary>
    Task<Resultats<Texte>> ListerAsync(FiltreTextes filtre, string tri, bool decroissant, int page, int parPage);
}

public sealed class TexteRepository(DapperContext db) : ITexteRepository
{
    private const string ColonnesTexte =
        """
        t.id, t.type_libelle, t.titre, t.titre_en, t.titre_en_auto, t.etat_code, t.etat_libelle, t.numero_loi, t.date_promulgation, t.date_jo, t.numero_jo,
        t.url_legifrance, t.url_dossier_senat, t.url_dossier_an, t.saisine_cc_date, t.saisine_cc_par, t.decision_cc_numero,
        t.decision_cc_date, t.decision_cc_code, t.decision_cc_libelle, t.decision_cc_url
        """;

    // Agrégat des scrutins rattachés à un texte. L'adoption se lit sur le résultat du vote sur l'ensemble
    // (`sort` commence par « adopt ») : un texte peut avoir plusieurs votes par chambre (lectures successives),
    // « adopté » signifie donc « au moins un vote sur l'ensemble adopté dans cette chambre ».
    private const string Agregat =
        """
        -- Étapes de lecture à l'Assemblée (table texte_etapes) : elles couvrent aussi les décisions SANS scrutin public (main levée).
        -- « Adoptée » = adoptée, adoptée sans modification, modifiée (adoptée avec modifications) ou considérée comme définitive ; « rejetée » = non adoptée.
        GREATEST(max(s.date_scrutin), (SELECT max(e.date_acte) FROM texte_etapes e WHERE e.texte_id = t.id)) AS dernier_vote,
        count(s.id)::int AS nb_scrutins,
        (SELECT count(*) FROM texte_etapes e WHERE e.texte_id = t.id)::int AS nb_etapes_an,
        EXISTS (SELECT 1 FROM texte_etapes e WHERE e.texte_id = t.id AND e.article_49_3) AS adopte_493,
        EXISTS (SELECT 1 FROM texte_etapes e WHERE e.texte_id = t.id AND e.resultat ILIKE ANY (ARRAY['adopt%', 'modifi%', 'consid%'])) AS adopte_an_etape,
        (COALESCE(bool_or(s.chambre = 'AN' AND s.sort LIKE 'adopt%'), false)
         OR EXISTS (SELECT 1 FROM texte_etapes e WHERE e.texte_id = t.id AND e.resultat ILIKE ANY (ARRAY['adopt%', 'modifi%', 'consid%']))) AS adopte_an,
        COALESCE(bool_or(s.chambre = 'SENAT' AND s.sort LIKE 'adopt%'), false) AS adopte_senat,
        (count(s.id) FILTER (WHERE s.chambre = 'AN'))::int AS nb_scrutins_an,
        (count(s.id) FILTER (WHERE s.chambre = 'SENAT'))::int AS nb_scrutins_senat,
        GREATEST(max(s.date_scrutin) FILTER (WHERE s.chambre = 'AN' AND s.sort LIKE 'adopt%'),
                 (SELECT max(e.date_acte) FROM texte_etapes e WHERE e.texte_id = t.id AND e.resultat ILIKE ANY (ARRAY['adopt%', 'modifi%', 'consid%']))) AS date_adoption_an,
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

    public async Task<IReadOnlyList<EtapeTexte>> GetEtapesAsync(string texteId)
    {
        using var conn = db.CreateConnection();
        return (await conn.QueryAsync<EtapeTexte>(
            """
            SELECT code_acte, date_acte, resultat, scrutin_id, sans_scrutin_public, article_49_3
              FROM texte_etapes WHERE texte_id = @texteId ORDER BY date_acte, code_acte
            """, new { texteId })).ToList();
    }

    public async Task<IReadOnlyList<ScrutinDuTexte>> GetScrutinsAsync(string texteId)
    {
        using var conn = db.CreateConnection();
        return (await conn.QueryAsync<ScrutinDuTexte>(
            """
            SELECT id, chambre, date_scrutin, titre, titre_en, titre_en_auto, sort, nb_pour, nb_contre, nb_abstentions
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
        // Période : un texte est retenu dès qu'UNE de ses dates tombe dans l'intervalle (bornes incluses) : un de ses votes,
        // la promulgation, la parution au JO, la saisine ou la décision du Conseil constitutionnel. Les votes pris en compte
        // sont ceux de la chambre filtrée. Sans période, aucune condition. Colonne NULL = pas dans l'intervalle.
        static string EnPeriode(string col) =>
            $"COALESCE(({col} >= @du::date OR @du::date IS NULL) AND ({col} <= @au::date OR @au::date IS NULL), false)";
        var periode =
            $"""
             HAVING (@du::date IS NULL AND @au::date IS NULL)
                 OR {EnPeriode("t.date_promulgation")} OR {EnPeriode("t.date_jo")}
                 OR {EnPeriode("t.saisine_cc_date")} OR {EnPeriode("t.decision_cc_date")}
                 OR bool_or({EnPeriode("s.date_scrutin")})
                 OR EXISTS (SELECT 1 FROM texte_etapes e WHERE e.texte_id = t.id AND {EnPeriode("e.date_acte")})
             """;
        var cte =
            $"""
             WITH base AS (
                 SELECT {ColonnesTexte}, {Agregat}
                   FROM textes t LEFT JOIN scrutins s ON s.texte_id = t.id AND (@chambre IS NULL OR s.chambre = @chambre)
                  WHERE (t.titre ILIKE ALL (@mots) OR t.titre_en ILIKE ALL (@mots))   -- recherche aussi dans le titre anglais
                    -- Au moins un vote nominatif, OU des étapes de lecture à l'Assemblée sans scrutin public (main levée) : ces textes
                    -- ne sont pas omis faute de nominatif. Les étapes sont des étapes de l'Assemblée : exclues si l'on filtre Sénat / UE.
                    AND (s.id IS NOT NULL
                         OR (@chambre IS NULL OR @chambre = 'AN') AND EXISTS (SELECT 1 FROM texte_etapes e WHERE e.texte_id = t.id))
                  GROUP BY t.id
                  {periode}
             ), classe AS (
                 SELECT *, {RangStatut} AS statut_rang FROM base
             )
             """;
        var where = $"WHERE TRUE {FiltreStatut(filtre.Statut)}";
        var p = new { chambre, mots, du = filtre.Du, au = filtre.Au, limit = parPage, offset = (page - 1) * parPage };

        var total = await conn.ExecuteScalarAsync<int>($"{cte} SELECT count(*) FROM classe {where}", p);
        var pagination = parPage > 0 ? "LIMIT @limit OFFSET @offset" : "LIMIT 50000";
        var lignes = (await conn.QueryAsync<Texte>(
            $"{cte} SELECT * FROM classe {where} ORDER BY {colonne} {sens} NULLS LAST, titre {pagination}", p)).ToList();

        return new Resultats<Texte>(lignes, total, page, parPage == 0 ? Math.Max(1, total) : parPage);
    }
}
