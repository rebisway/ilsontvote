using Dapper;
using IlsOntVote.Web.Data;
using IlsOntVote.Web.Models;
using IlsOntVote.Web.Services;

namespace IlsOntVote.Web.Repositories;

public interface IScrutinRepository
{
    /// <summary>Textes « à la une » d'une chambre : ceux marqués featured, sinon les derniers votes sur l'ensemble d'une loi.</summary>
    Task<IReadOnlyList<Scrutin>> GetAccueilAsync(string chambre, int max);

    /// <summary>Derniers scrutins d'une chambre, tous types confondus (accueil d'un pays sans « à la une » ni vote sur l'ensemble identifié : Communes).</summary>
    Task<IReadOnlyList<Scrutin>> GetDernieresAsync(string chambre, int max);

    Task<Scrutin?> GetAsync(string id);

    /// <summary>Tous les sièges (votants + absents déduits) pour un scrutin, sans coordonnées.</summary>
    Task<IReadOnlyList<Siege>> GetSiegesAsync(string scrutinId);

    /// <summary>Liste paginée de scrutins. <paramref name="chambre"/> null = Assemblée nationale et Sénat (pas le Parlement européen).</summary>
    Task<Resultats<Scrutin>> ListerAsync(string? chambre, string? recherche, bool surEnsembleSeulement, int page, int parPage,
        string tri = "date", bool decroissant = true, DateTime? du = null, DateTime? au = null, string? texteId = null, bool resolutionsSeulement = false);
}

public sealed class ScrutinRepository(DapperContext db, PaysSite site, Langue langue) : IScrutinRepository
{
    /// <summary>
    /// Chambres visibles sur le site courant (France : AN, Sénat, UE ; /uk : Communes). Garde-fou : un scrutin d'un autre pays n'est ni listé ni ouvert par son
    /// identifiant sur ce site (sinon les divisions britanniques apparaîtraient sur le site français dès leur import).
    /// </summary>
    private string[] Visibles => site.ChambresVisibles(langue.Pays);

    private const string Colonnes =
        """
        id, chambre, legislature, numero, date_scrutin, titre, titre_en, titre_en_auto, titre_fr_auto, sort, type_vote, sur_ensemble,
        nb_votants, nb_pour, nb_contre, nb_abstentions, nb_non_votants, featured_title,
        nominatif_incomplet, nb_votes_manquants, note,
        type_vote_libelle, type_majorite, suffrages_exprimes, suffrages_requis
        """;

    public async Task<IReadOnlyList<Scrutin>> GetDernieresAsync(string chambre, int max)
    {
        if (!Visibles.Contains(chambre)) return [];
        using var conn = db.CreateConnection();
        return (await conn.QueryAsync<Scrutin>(
            $"SELECT {Colonnes} FROM scrutins WHERE chambre = @chambre ORDER BY date_scrutin DESC, numero DESC LIMIT @max", new { chambre, max })).ToList();
    }

    public async Task<IReadOnlyList<Scrutin>> GetAccueilAsync(string chambre, int max)
    {
        using var conn = db.CreateConnection();

        var featured = (await conn.QueryAsync<Scrutin>(
            $"""
             SELECT {Colonnes} FROM scrutins
              WHERE featured AND chambre = @chambre
              ORDER BY featured_rank NULLS LAST, date_scrutin DESC LIMIT @max
             """, new { chambre, max })).ToList();
        if (featured.Count > 0) return featured;

        // Repli automatique : votes sur l'ensemble d'une loi (pas de résolution), les plus récents.
        var recents = await conn.QueryAsync<Scrutin>(
            $"""
             SELECT {Colonnes} FROM scrutins
              WHERE chambre = @chambre AND sur_ensemble AND (NOT nominatif_incomplet OR chambre = 'COMMONS')
                AND (chambre IN ('UE', 'COMMONS') OR titre ~* '(projet|proposition) de loi') -- UE : titres anglais, `sur_ensemble` = vote principal de la procédure
              ORDER BY date_scrutin DESC, numero DESC LIMIT @max
             """, new { chambre, max });
        return recents.ToList();
    }

    public async Task<Scrutin?> GetAsync(string id)
    {
        using var conn = db.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<Scrutin>(
            $"SELECT {Colonnes} FROM scrutins WHERE id = @id AND chambre = ANY(@visibles)", new { id, visibles = Visibles });
    }

    public async Task<IReadOnlyList<Siege>> GetSiegesAsync(string scrutinId)
    {
        using var conn = db.CreateConnection();

        // Absent = siégeait à la date du scrutin (mandat) et n'apparaît pas dans `votes`.
        // Le groupe est celui du vote s'il existe, sinon celui en vigueur ce jour-là.
        const string sql =
            """
            SELECT p.id AS parlementaire_id, p.chambre, p.prenom, p.nom, p.slug,
                   g.id AS groupe_id, COALESCE(g.sigle, 'NI') AS groupe_sigle,
                   g.couleur AS groupe_couleur, COALESCE(g.ordre, 130) AS groupe_ordre,
                   -- Gouvernement : si une période CLOSE (chambre_gouvernements) couvre la date, elle fait foi ; sinon l'indicateur courant du groupe (réglé dans /Admin/Pays).
                   CASE WHEN EXISTS (SELECT 1 FROM chambre_gouvernements x WHERE x.chambre = s.chambre AND s.date_scrutin BETWEEN x.date_debut AND x.date_fin)
                        THEN EXISTS (SELECT 1 FROM chambre_gouvernements x WHERE x.chambre = s.chambre AND x.groupe_id = g.id AND s.date_scrutin BETWEEN x.date_debut AND x.date_fin)
                        ELSE COALESCE(g.gouvernement, false) END AS groupe_gouvernement,
                   COALESCE(v.position, 'absent') AS position,
                   COALESCE(v.par_delegation, false) AS par_delegation,
                   COALESCE(v.rectifie, false) AS rectifie,
                   v.position_origine,
                   COALESCE(v.origine_inconnue, false) AS origine_inconnue
              FROM scrutins s
              JOIN parlementaires p ON p.chambre = s.chambre
              LEFT JOIN votes v ON v.scrutin_id = s.id AND v.parlementaire_id = p.id
              LEFT JOIN LATERAL (
                    SELECT ga.groupe_id FROM groupe_appartenances ga
                     WHERE ga.parlementaire_id = p.id
                       AND ga.date_debut <= s.date_scrutin
                       AND (ga.date_fin IS NULL OR ga.date_fin >= s.date_scrutin)
                     ORDER BY ga.date_debut DESC LIMIT 1) cur ON true
              LEFT JOIN groupes g ON g.id = COALESCE(v.groupe_id, cur.groupe_id)
             WHERE s.id = @scrutinId
               AND (v.parlementaire_id IS NOT NULL OR EXISTS (
                    SELECT 1 FROM mandats m
                     WHERE m.parlementaire_id = p.id
                       AND (m.legislature IS NULL OR m.legislature = s.legislature)
                       AND m.date_debut <= s.date_scrutin
                       AND (m.date_fin IS NULL OR m.date_fin >= s.date_scrutin)))
             ORDER BY COALESCE(g.ordre, 130), g.sigle, p.nom, p.prenom
            """;

        return (await conn.QueryAsync<Siege>(sql, new { scrutinId })).ToList();
    }

    // Colonnes triables : clé d'URL -> colonne SQL. JAMAIS de SQL construit depuis l'URL.
    private static readonly Dictionary<string, string> Tris = new()
    {
        ["date"] = "date_scrutin", ["chambre"] = "chambre", ["titre"] = "titre", ["resultat"] = "sort",
        ["votants"] = "nb_votants", ["pour"] = "nb_pour", ["contre"] = "nb_contre", ["abst"] = "nb_abstentions",
    };

    public static IEnumerable<string> ClesTri => Tris.Keys;

    public async Task<Resultats<Scrutin>> ListerAsync(string? chambre, string? recherche, bool surEnsembleSeulement, int page, int parPage,
        string tri = "date", bool decroissant = true, DateTime? du = null, DateTime? au = null, string? texteId = null, bool resolutionsSeulement = false)
    {
        using var conn = db.CreateConnection();
        page = Math.Max(1, page);

        // Recherche : chaque mot doit apparaître dans le titre (insensible à la casse).
        var mots = RechercheSql.Mots(recherche);   // tirets et apostrophes = espaces

        var where = """
            -- Sans filtre = « Assemblée et Sénat » : le Parlement européen ne s'affiche que si on le choisit explicitement.
            WHERE ((@chambre IS NULL AND chambre <> 'UE') OR chambre = @chambre)
              AND chambre = ANY(@visibles)   -- seulement les chambres du site courant (voir Visibles)
              AND (NOT @ensemble OR sur_ensemble)
              AND NOT (nominatif_incomplet AND @ensemble AND chambre <> 'COMMONS')   -- Communes : incomplets conservés (alerte sur la fiche)
              AND (recherche_norm(titre) ILIKE ALL (@mots) OR recherche_norm(titre_en) ILIKE ALL (@mots))   -- aussi dans le titre anglais ; tirets = espaces
              -- Période : bornes incluses ; `date_scrutin` est une date (sans heure).
              AND (@du::date IS NULL OR date_scrutin >= @du::date)
              AND (@au::date IS NULL OR date_scrutin <= @au::date)
              -- Votes d'un texte : le vote sur l'ensemble (texte_id) et ceux qui le citent (amendements, articles : texte_lie_id).
              AND (@texte::text IS NULL OR texte_id = @texte OR texte_lie_id = @texte)
              -- Résolutions : le titre cite une « proposition de résolution » (européenne, article 34-1, commission d'enquête...). Elles n'ont pas force
              -- de loi : elles ont leur propre entrée de menu et ne sont pas rattachées à un texte de loi.
              AND (NOT @resolutions OR titre ~* 'proposition de r[ée]solution')
            """;
        var p = new { chambre, visibles = Visibles, ensemble = surEnsembleSeulement, mots, du, au, texte = texteId, resolutions = resolutionsSeulement, limit = parPage, offset = (page - 1) * parPage };

        var total = await conn.ExecuteScalarAsync<int>($"SELECT count(*) FROM scrutins {where}", p);
        var colonne = Tris.GetValueOrDefault(tri, "date_scrutin");
        var sens = decroissant ? "DESC" : "ASC";
        var pagination = parPage > 0 ? "LIMIT @limit OFFSET @offset" : "LIMIT 50000";
        var lignes = (await conn.QueryAsync<Scrutin>(
            $"SELECT {Colonnes} FROM scrutins {where} ORDER BY {colonne} {sens} NULLS LAST, date_scrutin DESC, numero DESC {pagination}", p)).ToList();

        return new Resultats<Scrutin>(lignes, total, page, parPage == 0 ? Math.Max(1, total) : parPage);
    }
}
