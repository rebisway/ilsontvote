using IlsOntVote.Web.Models;
using Dapper;
using IlsOntVote.Web.Data;

namespace IlsOntVote.Web.Repositories;

/// <summary>Chiffres clés d'une chambre sur une période.</summary>
public sealed record ChiffresCles(
    int Scrutins, int VotesEnsemble, int Adoptes, int Rejetes, int LoisPromulguees, double? ParticipationPct);

/// <summary>Participation d'un groupe politique sur une période (moyennes sur les scrutins retenus).</summary>
/// <remarks>Classe (et non record) : Dapper lie un constructeur positionnel par nom ET type exact, ce qui casse dès qu'un type SQL diffère.</remarks>
public sealed class StatGroupe
{
    public string Sigle { get; set; } = "";
    public string Libelle { get; set; } = "";
    public string? Couleur { get; set; }
    public int Ordre { get; set; }
    public double EffectifMoyen { get; set; }
    public double? ParticipationPct { get; set; }
    public double? PourPct { get; set; }
    public double? ContrePct { get; set; }
    public double? AbstentionPct { get; set; }
    /// <summary>Cohésion : part des présents du groupe qui votent comme la majorité de leur groupe, scrutin par scrutin (100 % = le groupe vote d'une seule voix).</summary>
    public double? CohesionPct { get; set; }
}

/// <summary>
/// Un élu dans la liste complète des taux (période et chambre choisies). Tous les taux ont pour dénominateur les scrutins à sa charge
/// (mandat en cours, hors « non votant » déclaré) : ils sont donc comparables entre eux.
/// </summary>
public sealed class StatEluDetail
{
    public string Slug { get; set; } = "";
    public string Chambre { get; set; } = "";
    public string Prenom { get; set; } = "";
    public string Nom { get; set; } = "";
    public string? GroupeSigle { get; set; }
    public string? GroupeCouleur { get; set; }
    public int Scrutins { get; set; }
    /// <summary>Votes exprimés (pour, contre, abstention), délégations comprises.</summary>
    public int Votants { get; set; }
    public int Delegations { get; set; }
    public int MisesAuPoint { get; set; }

    private double? Taux(int n) => Scrutins == 0 ? null : Math.Round(100.0 * n / Scrutins, 1);
    /// <summary>Part des scrutins où un vote a été exprimé (par délégation ou non).</summary>
    public double? ParticipationPct => Taux(Votants);
    /// <summary>Présence effective : vote exprimé par l'élu lui-même (sans délégation).</summary>
    public double? PresencePct => Taux(Votants - Delegations);
    public double? DelegationPct => Taux(Delegations);
    public double? MiseAuPointPct => Taux(MisesAuPoint);
    /// <summary>Aucun vote exprimé (ni lui, ni par délégation). Le site n'en donne aucune raison.</summary>
    public double? AbsencePct => Taux(Scrutins - Votants);
}

/// <summary>Activité d'une année : nombre de scrutins et issue des votes sur l'ensemble d'un texte.</summary>
public sealed class StatAnnee
{
    public int Annee { get; set; }
    public int Scrutins { get; set; }
    public int VotesEnsemble { get; set; }
    public int Adoptes { get; set; }
    public int Rejetes { get; set; }
    public double? ParticipationPct { get; set; }
}

public interface IStatistiquesRepository
{
    Task<ChiffresCles> GetChiffresAsync(string chambre, DateTime? du, DateTime? au, bool ensembleSeulement);
    Task<IReadOnlyList<StatGroupe>> GetGroupesAsync(string chambre, DateTime? du, DateTime? au, bool ensembleSeulement);
    Task<IReadOnlyList<StatAnnee>> GetAnneesAsync(string chambre);
    /// <summary>Les élus les plus (ou les moins) présents ; seuls ceux ayant au moins <paramref name="minScrutins"/> scrutins sur la période comptent.</summary>
    /// <summary>Liste complète des élus d'une chambre avec leurs taux sur la période : recherche, tri (liste blanche), pagination (parPage = 0 : tout).</summary>
    Task<Resultats<StatEluDetail>> ListerElusAsync(string chambre, DateTime? du, DateTime? au, bool ensembleSeulement, string? recherche,
        string tri, bool decroissant, int page, int parPage);
}

/// <summary>
/// Statistiques publiques. La participation vient de la vue matérialisée <c>stats_participation_groupe</c> (migration 013, recalculée
/// à chaque import) : jamais de calcul lourd à la demande. Les scrutins au décompte nominatif incomplet n'y figurent pas.
/// </summary>
public sealed class StatistiquesRepository(DapperContext db) : IStatistiquesRepository
{
    // Filtre commun : chambre, bornes de date incluses, option « votes sur l'ensemble seulement ». Appliqué à la vue (alias p) et à scrutins (alias s).
    private const string FiltreVue =
        """
        p.chambre = @chambre
        AND (@du::date IS NULL OR p.date_scrutin >= @du::date)
        AND (@au::date IS NULL OR p.date_scrutin <= @au::date)
        AND (NOT @ensemble OR EXISTS (SELECT 1 FROM scrutins s WHERE s.id = p.scrutin_id AND s.sur_ensemble))
        """;

    public async Task<ChiffresCles> GetChiffresAsync(string chambre, DateTime? du, DateTime? au, bool ensembleSeulement)
    {
        using var cx = db.CreateConnection();
        var p = new { chambre, du, au, ensemble = ensembleSeulement };

        // Scrutins : tous ceux de la chambre (les incomplets comptent comme scrutins, pas dans la participation).
        var sc = await cx.QuerySingleAsync<(int Total, int Ensemble, int Adoptes, int Rejetes)>(
            """
            SELECT count(*)::int,
                   count(*) FILTER (WHERE sur_ensemble)::int,
                   count(*) FILTER (WHERE sur_ensemble AND sort LIKE 'adopt%')::int,
                   count(*) FILTER (WHERE sur_ensemble AND (sort IS NULL OR sort NOT LIKE 'adopt%'))::int
              FROM scrutins s
             WHERE s.chambre = @chambre
               AND (@du::date IS NULL OR s.date_scrutin >= @du::date)
               AND (@au::date IS NULL OR s.date_scrutin <= @au::date)
               AND (NOT @ensemble OR s.sur_ensemble)
            """, p);

        // Lois promulguées : textes français (dossiers du Sénat) ; sans objet pour le Parlement européen.
        var lois = chambre == "UE" ? 0 : await cx.ExecuteScalarAsync<int>(
            """
            SELECT count(*)::int FROM textes t
             WHERE t.date_promulgation IS NOT NULL
               AND (@du::date IS NULL OR t.date_promulgation >= @du::date)
               AND (@au::date IS NULL OR t.date_promulgation <= @au::date)
            """, p);

        var part = await cx.ExecuteScalarAsync<double?>(
            $"SELECT round(100.0 * sum(votants) / NULLIF(sum(effectif), 0), 1)::float8 FROM stats_participation_groupe p WHERE {FiltreVue}", p);

        return new ChiffresCles(sc.Total, sc.Ensemble, sc.Adoptes, sc.Rejetes, lois, part);
    }

    public async Task<IReadOnlyList<StatGroupe>> GetGroupesAsync(string chambre, DateTime? du, DateTime? au, bool ensembleSeulement)
    {
        using var cx = db.CreateConnection();
        // Moyenne par scrutin : effectif moyen d'un groupe = somme des effectifs / nombre de scrutins où il siège.
        // Groupe '' (sans groupe) = non-inscrits d'après les appartenances : affiché « Sans groupe ».
        var rows = await cx.QueryAsync<StatGroupe>(
            $"""
             SELECT COALESCE(g.sigle, 'Sans groupe') AS sigle, COALESCE(max(g.libelle), 'Sans groupe') AS libelle, max(g.couleur) AS couleur,
                    COALESCE(min(g.ordre), 999) AS ordre,
                    round(sum(p.effectif)::numeric / count(*), 1)::float8 AS effectif_moyen,
                    round(100.0 * sum(p.votants) / NULLIF(sum(p.effectif), 0), 1)::float8 AS participation_pct,
                    round(100.0 * sum(p.pour) / NULLIF(sum(p.votants), 0), 1)::float8 AS pour_pct,
                    round(100.0 * sum(p.contre) / NULLIF(sum(p.votants), 0), 1)::float8 AS contre_pct,
                    round(100.0 * sum(p.abstention) / NULLIF(sum(p.votants), 0), 1)::float8 AS abstention_pct,
                    -- Cohésion : à chaque scrutin, les présents qui ont voté comme la position majoritaire du groupe ; moyenne pondérée.
                    round(100.0 * sum(GREATEST(p.pour, p.contre, p.abstention)) / NULLIF(sum(p.votants), 0), 1)::float8 AS cohesion_pct
               FROM stats_participation_groupe p
               LEFT JOIN groupes g ON g.id = p.groupe_id
              WHERE {FiltreVue}
              -- Groupes fusionnés par SIGLE : « SOC », « RN », « DEM »… existent une fois par législature (identifiants différents).
              GROUP BY COALESCE(g.sigle, 'Sans groupe')
              HAVING sum(p.effectif) > 0
              ORDER BY COALESCE(min(g.ordre), 999), COALESCE(g.sigle, 'Sans groupe')
             """, new { chambre, du, au, ensemble = ensembleSeulement });
        return rows.ToList();
    }

    // Tris autorisés : clé d'URL -> expression SQL. JAMAIS de SQL construit depuis l'URL. Les taux sont triés sur leur valeur exacte.
    private static readonly Dictionary<string, string> TrisElus = new()
    {
        ["nom"] = "p.nom, p.prenom",
        ["groupe"] = "g.sigle",
        ["scrutins"] = "a.scrutins",
        ["participation"] = "a.votants::float8 / NULLIF(a.scrutins, 0)",
        ["presence"] = "(a.votants - a.delegations)::float8 / NULLIF(a.scrutins, 0)",
        ["delegation"] = "a.delegations::float8 / NULLIF(a.scrutins, 0)",
        ["mise_au_point"] = "a.mises_au_point::float8 / NULLIF(a.scrutins, 0)",
        ["absence"] = "(a.scrutins - a.votants)::float8 / NULLIF(a.scrutins, 0)",
    };

    public static IEnumerable<string> ClesTriElus => TrisElus.Keys;

    public async Task<Resultats<StatEluDetail>> ListerElusAsync(string chambre, DateTime? du, DateTime? au, bool ensembleSeulement, string? recherche,
        string tri, bool decroissant, int page, int parPage)
    {
        using var cx = db.CreateConnection();
        page = Math.Max(1, page);
        // Une vue matérialisée créée « WITH NO DATA » (migration 019) fait ÉCHOUER toute lecture tant qu'elle n'a pas été calculée (import
        // « stats ») : on répond « aucun résultat » (la page l'explique) plutôt qu'une erreur 500.
        if (!await cx.ExecuteScalarAsync<bool>("SELECT COALESCE((SELECT ispopulated FROM pg_matviews WHERE matviewname = 'stats_participation_elu'), false)"))
            return new Resultats<StatEluDetail>([], 0, 1, parPage == 0 ? 1 : parPage);
        // Recherche : chaque mot dans « prénom nom » ou le sigle du groupe ; tirets et apostrophes comptent comme des espaces.
        var mots = RechercheSql.Mots(recherche);

        // Somme des jours de la période (vue par élu et par jour, cf. migration 019). Aucun seuil : tout élu ayant eu au moins un scrutin
        // à sa charge figure dans la liste ; le nombre de scrutins est affiché à côté de chaque taux pour que le lecteur juge de son poids.
        const string avant =
            """
            WITH agg AS (
                SELECT e.parlementaire_id,
                       CASE WHEN @ensemble THEN sum(e.scrutins_ensemble) ELSE sum(e.scrutins) END AS scrutins,
                       CASE WHEN @ensemble THEN sum(e.votants_ensemble) ELSE sum(e.votants) END AS votants,
                       CASE WHEN @ensemble THEN sum(e.delegations_ensemble) ELSE sum(e.delegations) END AS delegations,
                       CASE WHEN @ensemble THEN sum(e.mises_au_point_ensemble) ELSE sum(e.mises_au_point) END AS mises_au_point
                  FROM stats_participation_elu e
                 WHERE e.chambre = @chambre
                   AND (@du::date IS NULL OR e.date_scrutin >= @du::date)
                   AND (@au::date IS NULL OR e.date_scrutin <= @au::date)
                 GROUP BY e.parlementaire_id
            )
            """;
        const string depuis =
            """
              FROM agg a
              JOIN parlementaires p ON p.id = a.parlementaire_id
              LEFT JOIN LATERAL (
                    SELECT gr.sigle, gr.couleur FROM groupe_appartenances ga JOIN groupes gr ON gr.id = ga.groupe_id
                     WHERE ga.parlementaire_id = p.id ORDER BY ga.date_debut DESC LIMIT 1) g ON true
             WHERE a.scrutins > 0
               AND (cardinality(@mots) = 0 OR (translate(p.prenom || ' ' || p.nom, '-''’', '   ') || ' ' || COALESCE(g.sigle, '')) ILIKE ALL (@mots))
            """;
        var p = new { chambre, du, au, ensemble = ensembleSeulement, mots, limit = parPage, offset = (page - 1) * parPage };

        var total = await cx.ExecuteScalarAsync<int>($"{avant} SELECT count(*) {depuis}", p);
        var ordre = TrisElus.GetValueOrDefault(tri, TrisElus["participation"]);
        var sens = decroissant ? "DESC" : "ASC";
        var pagination = parPage > 0 ? "LIMIT @limit OFFSET @offset" : "";
        var lignes = (await cx.QueryAsync<StatEluDetail>(
            $"""
             {avant}
             SELECT p.slug, p.chambre, p.prenom, p.nom, g.sigle AS groupe_sigle, g.couleur AS groupe_couleur,
                    a.scrutins::int AS scrutins, a.votants::int AS votants, a.delegations::int AS delegations, a.mises_au_point::int AS mises_au_point
               {depuis}
              ORDER BY {ordre} {sens} NULLS LAST, a.scrutins DESC, p.nom, p.prenom
              {pagination}
             """, p)).ToList();
        return new Resultats<StatEluDetail>(lignes, total, page, parPage == 0 ? Math.Max(1, total) : parPage);
    }

    public async Task<IReadOnlyList<StatAnnee>> GetAnneesAsync(string chambre)
    {
        using var cx = db.CreateConnection();
        // Sénat : `date_scrutin` est une vraie date (l'année de session n'est utilisée que comme « législature »).
        var rows = await cx.QueryAsync<StatAnnee>(
            """
            WITH a AS (
                SELECT extract(year FROM s.date_scrutin)::int AS annee,
                       count(*)::int AS scrutins,
                       count(*) FILTER (WHERE s.sur_ensemble)::int AS votes_ensemble,
                       count(*) FILTER (WHERE s.sur_ensemble AND s.sort LIKE 'adopt%')::int AS adoptes,
                       count(*) FILTER (WHERE s.sur_ensemble AND (s.sort IS NULL OR s.sort NOT LIKE 'adopt%'))::int AS rejetes
                  FROM scrutins s WHERE s.chambre = @chambre GROUP BY 1
            ), p AS (
                -- Participation de l'année : somme des votants / somme des effectifs de tous ses scrutins (vue précalculée).
                SELECT extract(year FROM date_scrutin)::int AS annee,
                       round(100.0 * sum(votants) / NULLIF(sum(effectif), 0), 1)::float8 AS participation_pct
                  FROM stats_participation_groupe WHERE chambre = @chambre GROUP BY 1
            )
            SELECT a.*, p.participation_pct FROM a LEFT JOIN p USING (annee) ORDER BY a.annee DESC
            """, new { chambre });
        return rows.ToList();
    }
}
