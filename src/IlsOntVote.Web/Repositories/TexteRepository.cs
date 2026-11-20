using Dapper;
using IlsOntVote.Web.Data;
using IlsOntVote.Web.Models;

namespace IlsOntVote.Web.Repositories;

/// <summary>Filtres de la liste des lois : mots du titre, statut (clé de liste blanche) et chambre (an | senat).</summary>
public sealed record FiltreTextes(string? Recherche, string? Statut, string? Chambre, DateTime? Du = null, DateTime? Au = null, string? Theme = null);

/// <summary>Accès aux textes de loi (dossiers législatifs) et à leur parcours : lecture, liste filtrée, triée et paginée.</summary>
public interface ITexteRepository
{
    Task<Texte?> GetAsync(string id);
    /// <summary>Loi précédente et suivante dans l'ordre de la liste /lois (date du dernier vote) ; null aux extrémités ou si la loi n'a aucun vote rattaché.</summary>
    Task<(Voisin? Precedent, Voisin? Suivant)> GetVoisinsAsync(string id);
    Task<Texte?> GetParScrutinAsync(string scrutinId);
    Task<IReadOnlyList<ScrutinDuTexte>> GetScrutinsAsync(string texteId);
    /// <summary>Autres scrutins publics liés au texte (amendements, articles…) : leur nombre et les plus récents.</summary>
    Task<(int Total, IReadOnlyList<ScrutinDuTexte> Recents)> GetScrutinsLiesAsync(string texteId, int limite);
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
        t.decision_cc_date, t.decision_cc_code, t.decision_cc_libelle, t.decision_cc_url, t.themes
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
        (SELECT count(*) FROM texte_etapes e WHERE e.texte_id = t.id AND e.chambre = 'AN')::int AS nb_etapes_an,
        (SELECT count(*) FROM texte_etapes e WHERE e.texte_id = t.id AND e.chambre = 'SENAT')::int AS nb_etapes_senat,
        EXISTS (SELECT 1 FROM texte_etapes e WHERE e.texte_id = t.id AND e.article_49_3) AS adopte_493,
        EXISTS (SELECT 1 FROM texte_etapes e WHERE e.texte_id = t.id AND e.chambre = 'AN' AND e.resultat ILIKE ANY (ARRAY['adopt%', 'modifi%', 'consid%'])) AS adopte_an_etape,
        (COALESCE(bool_or(s.chambre = 'AN' AND s.sort LIKE 'adopt%'), false)
         OR EXISTS (SELECT 1 FROM texte_etapes e WHERE e.texte_id = t.id AND e.chambre = 'AN' AND e.resultat ILIKE ANY (ARRAY['adopt%', 'modifi%', 'consid%']))) AS adopte_an,
        (COALESCE(bool_or(s.chambre = 'SENAT' AND s.sort LIKE 'adopt%'), false)
         OR EXISTS (SELECT 1 FROM texte_etapes e WHERE e.texte_id = t.id AND e.chambre = 'SENAT' AND e.resultat ILIKE 'adopt%')) AS adopte_senat,
        -- Amendements (table amendements_dossiers, import officiel) : somme sur les dossiers AN du texte. Un dossier d'une législature COUVERTE
        -- (des lignes existent pour elle) sans ligne propre a zéro amendement ; une législature non couverte ou un texte sans dossier AN donne NULL
        -- (donnée manquante / non applicable), jamais un 0 inventé.
        (SELECT CASE WHEN bool_or(EXISTS (SELECT 1 FROM amendements_dossiers x WHERE x.legislature = substring(d.dossier_an from '^DLR5L([0-9]+)N')::smallint))
                     THEN COALESCE(sum(a.deposes), 0) END
           FROM (SELECT DISTINCT e.dossier_an FROM texte_etapes e WHERE e.texte_id = t.id AND e.chambre = 'AN') d
           LEFT JOIN amendements_dossiers a ON a.dossier_an = d.dossier_an)::int AS nb_amendements,
        (SELECT sum(a.adoptes) FROM amendements_dossiers a WHERE a.dossier_an IN (SELECT e.dossier_an FROM texte_etapes e WHERE e.texte_id = t.id AND e.chambre = 'AN'))::int AS nb_amendements_adoptes,
        (SELECT sum(a.rejetes) FROM amendements_dossiers a WHERE a.dossier_an IN (SELECT e.dossier_an FROM texte_etapes e WHERE e.texte_id = t.id AND e.chambre = 'AN'))::int AS nb_amendements_rejetes,
        -- Calculé : scrutins publics rattachés au texte (texte_lie_id, index partiel) dont le titre vise un amendement.
        (SELECT count(*) FROM scrutins x WHERE x.texte_lie_id = t.id AND x.titre ILIKE '%amendement%')::int AS nb_amendements_scrutins,
        (count(s.id) FILTER (WHERE s.chambre = 'AN'))::int AS nb_scrutins_an,
        (count(s.id) FILTER (WHERE s.chambre = 'SENAT'))::int AS nb_scrutins_senat,
        GREATEST(max(s.date_scrutin) FILTER (WHERE s.chambre = 'AN' AND s.sort LIKE 'adopt%'),
                 (SELECT max(e.date_acte) FROM texte_etapes e WHERE e.texte_id = t.id AND e.chambre = 'AN' AND e.resultat ILIKE ANY (ARRAY['adopt%', 'modifi%', 'consid%']))) AS date_adoption_an,
        GREATEST(max(s.date_scrutin) FILTER (WHERE s.chambre = 'SENAT' AND s.sort LIKE 'adopt%'),
                 (SELECT max(e.date_acte) FROM texte_etapes e WHERE e.texte_id = t.id AND e.chambre = 'SENAT' AND e.resultat ILIKE 'adopt%')) AS date_adoption_senat
        """;

    // AGRÉGAT LÉGER : seulement ce qui sert à filtrer, trier et classer (statut) TOUS les textes. Les sous-requêtes d'amendements et d'étapes de l'agrégat complet étaient évaluées
    // pour les ~2 100 textes avant le tri et la pagination (2,4 s) alors que la page n'en affiche que 30 : elles sont dans LateralLourd, évaluée pour les seules lignes de la page.
    private const string AgregatLeger =
        """
        GREATEST(max(s.date_scrutin), (SELECT max(e.date_acte) FROM texte_etapes e WHERE e.texte_id = t.id)) AS dernier_vote,
        count(s.id)::int AS nb_scrutins,
        EXISTS (SELECT 1 FROM texte_etapes e WHERE e.texte_id = t.id AND e.article_49_3) AS adopte_493,
        (COALESCE(bool_or(s.chambre = 'AN' AND s.sort LIKE 'adopt%'), false)
         OR EXISTS (SELECT 1 FROM texte_etapes e WHERE e.texte_id = t.id AND e.chambre = 'AN' AND e.resultat ILIKE ANY (ARRAY['adopt%', 'modifi%', 'consid%']))) AS adopte_an,
        (COALESCE(bool_or(s.chambre = 'SENAT' AND s.sort LIKE 'adopt%'), false)
         OR EXISTS (SELECT 1 FROM texte_etapes e WHERE e.texte_id = t.id AND e.chambre = 'SENAT' AND e.resultat ILIKE 'adopt%')) AS adopte_senat
        """;

    // Colonnes lourdes, calculées après pagination pour les lignes de la page (c = ligne de la page). MÊMES définitions que dans Agregat (qui reste utilisé quand on trie par
    // amendements) ; les agrégats sur les scrutins reprennent le filtre de chambre de la requête (@chambre) pour rester identiques à ceux du JOIN de l'agrégat complet.
    private const string LateralLourd =
        """
        LEFT JOIN LATERAL (SELECT
            (SELECT count(*) FROM texte_etapes e WHERE e.texte_id = c.id AND e.chambre = 'AN')::int AS nb_etapes_an,
            (SELECT count(*) FROM texte_etapes e WHERE e.texte_id = c.id AND e.chambre = 'SENAT')::int AS nb_etapes_senat,
            EXISTS (SELECT 1 FROM texte_etapes e WHERE e.texte_id = c.id AND e.chambre = 'AN' AND e.resultat ILIKE ANY (ARRAY['adopt%', 'modifi%', 'consid%'])) AS adopte_an_etape,
            (SELECT CASE WHEN bool_or(EXISTS (SELECT 1 FROM amendements_dossiers x WHERE x.legislature = substring(d.dossier_an from '^DLR5L([0-9]+)N')::smallint))
                         THEN COALESCE(sum(a.deposes), 0) END
               FROM (SELECT DISTINCT e.dossier_an FROM texte_etapes e WHERE e.texte_id = c.id AND e.chambre = 'AN') d
               LEFT JOIN amendements_dossiers a ON a.dossier_an = d.dossier_an)::int AS nb_amendements,
            (SELECT sum(a.adoptes) FROM amendements_dossiers a WHERE a.dossier_an IN (SELECT e.dossier_an FROM texte_etapes e WHERE e.texte_id = c.id AND e.chambre = 'AN'))::int AS nb_amendements_adoptes,
            (SELECT sum(a.rejetes) FROM amendements_dossiers a WHERE a.dossier_an IN (SELECT e.dossier_an FROM texte_etapes e WHERE e.texte_id = c.id AND e.chambre = 'AN'))::int AS nb_amendements_rejetes,
            (SELECT count(*) FROM scrutins x WHERE x.texte_lie_id = c.id AND x.titre ILIKE '%amendement%')::int AS nb_amendements_scrutins,
            (SELECT count(*) FROM scrutins s WHERE s.texte_id = c.id AND s.chambre = 'AN' AND (@chambre IS NULL OR s.chambre = @chambre))::int AS nb_scrutins_an,
            (SELECT count(*) FROM scrutins s WHERE s.texte_id = c.id AND s.chambre = 'SENAT' AND (@chambre IS NULL OR s.chambre = @chambre))::int AS nb_scrutins_senat,
            GREATEST((SELECT max(s.date_scrutin) FROM scrutins s WHERE s.texte_id = c.id AND s.chambre = 'AN' AND s.sort LIKE 'adopt%' AND (@chambre IS NULL OR s.chambre = @chambre)),
                     (SELECT max(e.date_acte) FROM texte_etapes e WHERE e.texte_id = c.id AND e.chambre = 'AN' AND e.resultat ILIKE ANY (ARRAY['adopt%', 'modifi%', 'consid%']))) AS date_adoption_an,
            GREATEST((SELECT max(s.date_scrutin) FROM scrutins s WHERE s.texte_id = c.id AND s.chambre = 'SENAT' AND s.sort LIKE 'adopt%' AND (@chambre IS NULL OR s.chambre = @chambre)),
                     (SELECT max(e.date_acte) FROM texte_etapes e WHERE e.texte_id = c.id AND e.chambre = 'SENAT' AND e.resultat ILIKE 'adopt%')) AS date_adoption_senat
        ) h ON true
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
        ["amendements"] = "nb_amendements",
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
        // Au moins une lecture adoptée sans vote sur le texte (article 49, alinéa 3) : colonne adopte_493 (une étape `article_49_3` du texte).
        "49-3" => "AND adopte_493",
        _ => "",
    };

    public async Task<(Voisin? Precedent, Voisin? Suivant)> GetVoisinsAsync(string id)
    {
        using var conn = db.CreateConnection();
        // Ordre de la liste /lois par défaut : date du DERNIER vote rattaché au texte (puis identifiant). Un texte sans vote rattaché n'est pas dans cette liste : pas de voisins.
        var d = await conn.ExecuteScalarAsync<DateTime?>("SELECT max(date_scrutin) FROM scrutins WHERE texte_id = @id", new { id });
        if (d is null) return (null, null);
        const string agregat = "SELECT s.texte_id AS id, max(s.date_scrutin) AS d FROM scrutins s WHERE s.texte_id IS NOT NULL GROUP BY s.texte_id";
        var p = new { id, d = d.Value.Date };
        var prec = await conn.QueryFirstOrDefaultAsync<Voisin>($"SELECT t.id AS Cle, t.titre AS Titre FROM ({agregat}) x JOIN textes t ON t.id = x.id WHERE (x.d, x.id) < (@d::date, @id) ORDER BY x.d DESC, x.id DESC LIMIT 1", p);
        var suiv = await conn.QueryFirstOrDefaultAsync<Voisin>($"SELECT t.id AS Cle, t.titre AS Titre FROM ({agregat}) x JOIN textes t ON t.id = x.id WHERE (x.d, x.id) > (@d::date, @id) ORDER BY x.d ASC, x.id ASC LIMIT 1", p);
        return (prec, suiv);
    }

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
            SELECT chambre, code_acte, date_acte, resultat, scrutin_id, sans_scrutin_public, article_49_3
              FROM texte_etapes WHERE texte_id = @texteId ORDER BY date_acte, code_acte
            """, new { texteId })).ToList();
    }

    public async Task<IReadOnlyList<ScrutinDuTexte>> GetScrutinsAsync(string texteId)
    {
        using var conn = db.CreateConnection();
        return (await conn.QueryAsync<ScrutinDuTexte>(
            """
            SELECT id, chambre, date_scrutin, titre, titre_en, titre_en_auto, titre_fr_auto, sort, nb_pour, nb_contre, nb_abstentions
              FROM scrutins WHERE texte_id = @texteId ORDER BY date_scrutin, chambre, numero
            """, new { texteId })).ToList();
    }

    public async Task<(int Total, IReadOnlyList<ScrutinDuTexte> Recents)> GetScrutinsLiesAsync(string texteId, int limite)
    {
        using var conn = db.CreateConnection();
        // texte_lie_id : rattachement calculé par le titre du texte cité (cf. migration 017) ; absent en cas d'ambiguïté.
        var total = await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM scrutins WHERE texte_lie_id = @texteId", new { texteId });
        var recents = (await conn.QueryAsync<ScrutinDuTexte>(
            """
            SELECT id, chambre, date_scrutin, titre, titre_en, titre_en_auto, titre_fr_auto, sort, nb_pour, nb_contre, nb_abstentions
              FROM scrutins WHERE texte_lie_id = @texteId ORDER BY date_scrutin DESC, numero DESC LIMIT @limite
            """, new { texteId, limite })).ToList();
        return (total, recents);
    }

    public async Task<Resultats<Texte>> ListerAsync(FiltreTextes filtre, string tri, bool decroissant, int page, int parPage)
    {
        using var conn = db.CreateConnection();
        page = Math.Max(1, page);

        var mots = RechercheSql.Mots(filtre.Recherche);   // tirets et apostrophes = espaces
        var chambre = filtre.Chambre switch { "an" => "AN", "senat" => "SENAT", "ue" => "UE", _ => null };

        var colonne = Tris.GetValueOrDefault(tri, "dernier_vote");
        // Seul le tri par nombre d'amendements a besoin de l'agrégat complet pour TOUS les textes ; sinon agrégat léger + colonnes lourdes pour la page seulement.
        var lourd = colonne == "nb_amendements";
        var agregat = lourd ? Agregat : AgregatLeger;
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
                 SELECT {ColonnesTexte}, {agregat}
                   FROM textes t LEFT JOIN scrutins s ON s.texte_id = t.id AND (@chambre IS NULL OR s.chambre = @chambre)
                  WHERE (recherche_norm(t.titre) ILIKE ALL (@mots) OR recherche_norm(t.titre_en) ILIKE ALL (@mots))   -- aussi dans le titre anglais ; tirets = espaces
                    AND (@theme::text IS NULL OR @theme = ANY(t.themes))   -- thème approximatif (migration 048) ; code validé par Themes.Valider
                    -- Au moins un vote nominatif, OU des étapes de lecture à l'Assemblée sans scrutin public (main levée) : ces textes
                    -- ne sont pas omis faute de nominatif. Les étapes suivent le filtre de chambre (Assemblée ou Sénat ; aucune pour l'UE).
                    AND (s.id IS NOT NULL
                         OR EXISTS (SELECT 1 FROM texte_etapes e WHERE e.texte_id = t.id AND (@chambre IS NULL OR e.chambre = @chambre)))
                  GROUP BY t.id
                  {periode}
             ), classe AS (
                 SELECT *, {RangStatut} AS statut_rang FROM base
             )
             """;
        var where = $"WHERE TRUE {FiltreStatut(filtre.Statut)}";
        var p = new { chambre, mots, du = filtre.Du, au = filtre.Au, theme = filtre.Theme, limit = parPage, offset = (page - 1) * parPage };

        var total = await conn.ExecuteScalarAsync<int>($"{cte} SELECT count(*) FROM classe {where}", p);
        var pagination = parPage > 0 ? "LIMIT @limit OFFSET @offset" : "LIMIT 50000";
        var lignes = lourd
            ? (await conn.QueryAsync<Texte>($"{cte} SELECT * FROM classe {where} ORDER BY {colonne} {sens} NULLS LAST, titre {pagination}", p)).ToList()
            // Pagination d'abord (sur l'agrégat léger), colonnes lourdes ensuite pour ces lignes seulement ; le tri est répété à l'extérieur (l'ordre d'une sous-requête n'est pas garanti).
            : (await conn.QueryAsync<Texte>(
                $"{cte} SELECT c.*, h.* FROM (SELECT * FROM classe {where} ORDER BY {colonne} {sens} NULLS LAST, titre {pagination}) c {LateralLourd} ORDER BY {colonne} {sens} NULLS LAST, c.titre", p)).ToList();

        return new Resultats<Texte>(lignes, total, page, parPage == 0 ? Math.Max(1, total) : parPage);
    }
}
