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

/// <summary>Un élu dans un classement de participation (période et chambre choisies).</summary>
public sealed class StatElu
{
    public string Slug { get; set; } = "";
    public string Chambre { get; set; } = "";
    public string Prenom { get; set; } = "";
    public string Nom { get; set; } = "";
    public string? GroupeSigle { get; set; }
    public string? GroupeCouleur { get; set; }
    public int Scrutins { get; set; }
    public int Votants { get; set; }
    public double? ParticipationPct { get; set; }
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
    Task<IReadOnlyList<StatElu>> GetElusAsync(string chambre, DateTime? du, DateTime? au, bool ensembleSeulement, bool plusPresents, int limite, int minScrutins);
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

    public async Task<IReadOnlyList<StatElu>> GetElusAsync(
        string chambre, DateTime? du, DateTime? au, bool ensembleSeulement, bool plusPresents, int limite, int minScrutins)
    {
        using var cx = db.CreateConnection();
        // Somme des jours de la période (vue par élu et par jour). Seuil de scrutins : un élu arrivé hier ne doit pas « gagner » avec 2 sur 2.
        // L'ordre est une donnée interne (jamais de l'URL) : l'interpolation est sûre. Égalités départagées par le nombre de scrutins puis le nom.
        var sens = plusPresents ? "DESC" : "ASC";
        var rows = await cx.QueryAsync<StatElu>(
            $"""
             WITH agg AS (
                 SELECT e.parlementaire_id,
                        CASE WHEN @ensemble THEN sum(e.scrutins_ensemble) ELSE sum(e.scrutins) END AS scrutins,
                        CASE WHEN @ensemble THEN sum(e.votants_ensemble) ELSE sum(e.votants) END AS votants
                   FROM stats_participation_elu e
                  WHERE e.chambre = @chambre
                    AND (@du::date IS NULL OR e.date_scrutin >= @du::date)
                    AND (@au::date IS NULL OR e.date_scrutin <= @au::date)
                  GROUP BY e.parlementaire_id
             )
             SELECT p.slug, p.chambre, p.prenom, p.nom, g.sigle AS groupe_sigle, g.couleur AS groupe_couleur,
                    a.scrutins::int AS scrutins, a.votants::int AS votants,
                    round(100.0 * a.votants / NULLIF(a.scrutins, 0), 1)::float8 AS participation_pct
               FROM agg a
               JOIN parlementaires p ON p.id = a.parlementaire_id
               LEFT JOIN LATERAL (
                     SELECT gr.sigle, gr.couleur FROM groupe_appartenances ga JOIN groupes gr ON gr.id = ga.groupe_id
                      WHERE ga.parlementaire_id = p.id ORDER BY ga.date_debut DESC LIMIT 1) g ON true
              WHERE a.scrutins >= @minScrutins
              ORDER BY a.votants::float8 / NULLIF(a.scrutins, 0) {sens}, a.scrutins DESC, p.nom, p.prenom
              LIMIT @limite
             """, new { chambre, du, au, ensemble = ensembleSeulement, limite, minScrutins });
        return rows.ToList();
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
