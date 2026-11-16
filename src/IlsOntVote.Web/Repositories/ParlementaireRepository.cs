using Dapper;
using IlsOntVote.Web.Data;
using IlsOntVote.Web.Models;

namespace IlsOntVote.Web.Repositories;

/// <summary>Accès aux élus (députés et sénateurs) : liste en exercice, fiche, bilan de participation et historique de vote.</summary>
public interface IParlementaireRepository
{
    /// <summary>Parlementaires en exercice aujourd'hui, avec leur groupe courant.</summary>
    Task<IReadOnlyList<Parlementaire>> ListerEnExerciceAsync(string chambre);

    Task<Parlementaire?> GetParSlugAsync(string chambre, string slug);

    Task<BilanParlementaire> GetBilanAsync(string parlementaireId, string chambre);

    /// <summary>Périodes de mandat de l'élu (les périodes contiguës sont fusionnées), de la plus ancienne à la plus récente.</summary>
    Task<IReadOnlyList<PeriodeMandat>> GetMandatsAsync(string parlementaireId);

    /// <summary>Participation mois par mois (vue précalculée) ; liste vide tant que les statistiques ne sont pas calculées.</summary>
    Task<IReadOnlyList<PointMensuel>> GetParticipationMensuelleAsync(string parlementaireId);

    /// <summary>Historique de vote sur les scrutins où le parlementaire siégeait (absences incluses).</summary>
    Task<Resultats<LigneVote>> GetVotesAsync(string parlementaireId, string chambre, bool surEnsembleSeulement, int page, int parPage,
        DateTime? du = null, DateTime? au = null, string? recherche = null, string? filtreVote = null);
}

public sealed class ParlementaireRepository(DapperContext db) : IParlementaireRepository
{
    // Colonnes d'un élu + son groupe COURANT (cf. JointureGroupe). `groupe_ordre` vaut 130 (non-inscrits) quand
    // l'élu n'a aucune appartenance ouverte, pour qu'il se range en fin d'hémicycle plutôt que de disparaître.
    private const string Colonnes =
        """
        p.id, p.chambre, p.civilite, p.prenom, p.nom, p.slug, p.departement, p.circonscription,
        g.sigle AS groupe_sigle, g.libelle AS groupe_libelle, g.couleur AS groupe_couleur,
        COALESCE(g.ordre, 130) AS groupe_ordre
        """;

    // Groupe courant : l'appartenance qui couvre la date du jour, la plus récente en cas de recouvrement.
    // LATERAL + LIMIT 1 : une seule ligne par élu même si les dates de la source se chevauchent.
    private const string JointureGroupe =
        """
        LEFT JOIN LATERAL (
              SELECT ga.groupe_id FROM groupe_appartenances ga
               WHERE ga.parlementaire_id = p.id AND ga.date_debut <= CURRENT_DATE
                 AND (ga.date_fin IS NULL OR ga.date_fin >= CURRENT_DATE)
               ORDER BY ga.date_debut DESC LIMIT 1) cur ON true
        LEFT JOIN groupes g ON g.id = cur.groupe_id
        """;

    public async Task<IReadOnlyList<Parlementaire>> ListerEnExerciceAsync(string chambre)
    {
        using var conn = db.CreateConnection();
        var rows = await conn.QueryAsync<Parlementaire>(
            $"""
             SELECT {Colonnes}, true AS en_exercice
               FROM parlementaires p
               {JointureGroupe}
              WHERE p.chambre = @chambre
                AND EXISTS (SELECT 1 FROM mandats m
                             WHERE m.parlementaire_id = p.id AND m.date_debut <= CURRENT_DATE
                               -- législature la plus récente DE LA MÊME CHAMBRE (AN = 17, PE = 10 : un max global exclurait le PE)
                               AND (m.legislature IS NULL OR m.legislature = (SELECT max(m2.legislature) FROM mandats m2 JOIN parlementaires p2 ON p2.id = m2.parlementaire_id WHERE p2.chambre = p.chambre))
                               AND (m.date_fin IS NULL OR m.date_fin >= CURRENT_DATE))
              ORDER BY p.nom, p.prenom
             """, new { chambre });
        return rows.ToList();
    }

    public async Task<Parlementaire?> GetParSlugAsync(string chambre, string slug)
    {
        using var conn = db.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<Parlementaire>(
            $"""
             SELECT {Colonnes},
                    EXISTS (SELECT 1 FROM mandats m
                             WHERE m.parlementaire_id = p.id AND m.date_debut <= CURRENT_DATE
                               AND (m.legislature IS NULL OR m.legislature = (SELECT max(m2.legislature) FROM mandats m2 JOIN parlementaires p2 ON p2.id = m2.parlementaire_id WHERE p2.chambre = p.chambre))
                               AND (m.date_fin IS NULL OR m.date_fin >= CURRENT_DATE)) AS en_exercice
               FROM parlementaires p
               {JointureGroupe}
              WHERE p.chambre = @chambre AND p.slug = @slug
             """, new { chambre, slug });
    }

    public async Task<BilanParlementaire> GetBilanAsync(string parlementaireId, string chambre)
    {
        using var conn = db.CreateConnection();

        // Base de calcul : les scrutins publics tenus pendant SES mandats (un mandat = une période de dates ;
        // un élu peut en avoir plusieurs). On écarte les scrutins à la source incomplète : leurs « absents » ne sont
        // pas fiables et fausseraient le taux. `legislature` : voir la note sur les anciens mandats sans date de fin.
        return await conn.QuerySingleAsync<BilanParlementaire>(
            """
            SELECT count(*)::int AS total,
                   count(*) FILTER (WHERE v.position = 'pour')::int AS pour,
                   count(*) FILTER (WHERE v.position = 'contre')::int AS contre,
                   count(*) FILTER (WHERE v.position = 'abstention')::int AS abstention,
                   count(*) FILTER (WHERE v.position = 'non_votant')::int AS non_votant,
                   -- Absent = siégeait ce jour-là mais aucune ligne de vote : il n'est pas stocké, il se déduit.
                   count(*) FILTER (WHERE v.parlementaire_id IS NULL)::int AS absent,
                   -- Attributs d'un vote existant, SANS exclusivité avec la position : un vote par délégation est aussi un pour/contre/abstention.
                   count(*) FILTER (WHERE v.par_delegation)::int AS delegation,
                   count(*) FILTER (WHERE v.rectifie)::int AS mise_au_point
              FROM scrutins s
              LEFT JOIN votes v ON v.scrutin_id = s.id AND v.parlementaire_id = @parlementaireId
             WHERE s.chambre = @chambre AND NOT s.nominatif_incomplet
               AND EXISTS (SELECT 1 FROM mandats m
                            WHERE m.parlementaire_id = @parlementaireId
                              AND (m.legislature IS NULL OR m.legislature = s.legislature)
                              AND m.date_debut <= s.date_scrutin
                              AND (m.date_fin IS NULL OR m.date_fin >= s.date_scrutin))
            """, new { parlementaireId, chambre });
    }

    public async Task<IReadOnlyList<PeriodeMandat>> GetMandatsAsync(string parlementaireId)
    {
        using var conn = db.CreateConnection();
        var brut = (await conn.QueryAsync<PeriodeMandat>(
            "SELECT date_debut, date_fin FROM mandats WHERE parlementaire_id = @parlementaireId ORDER BY date_debut", new { parlementaireId })).ToList();
        // Fusion : une période qui commence au plus tard le lendemain de la fin de la précédente prolonge celle-ci (législatures qui s'enchaînent).
        var fusion = new List<PeriodeMandat>();
        foreach (var m in brut)
        {
            var dernier = fusion.Count > 0 ? fusion[^1] : null;
            if (dernier is not null && (dernier.DateFin is null || m.DateDebut <= dernier.DateFin.Value.AddDays(1)))
            {
                if (dernier.DateFin is not null && (m.DateFin is null || m.DateFin > dernier.DateFin)) dernier.DateFin = m.DateFin;
            }
            else fusion.Add(new PeriodeMandat { DateDebut = m.DateDebut, DateFin = m.DateFin });
        }
        return fusion;
    }

    public async Task<IReadOnlyList<PointMensuel>> GetParticipationMensuelleAsync(string parlementaireId)
    {
        using var conn = db.CreateConnection();
        // Une vue créée « WITH NO DATA » fait échouer toute lecture tant qu'elle n'est pas calculée : on répond « rien » plutôt qu'une erreur.
        if (!await conn.ExecuteScalarAsync<bool>("SELECT COALESCE((SELECT ispopulated FROM pg_matviews WHERE matviewname = 'stats_participation_elu'), false)"))
            return [];
        // La vue est par élu et par jour : somme par mois civil. Les mois sans scrutin à sa charge n'ont pas de ligne (et pas de barre).
        return (await conn.QueryAsync<PointMensuel>(
            """
            SELECT date_trunc('month', date_scrutin)::date AS mois,
                   sum(scrutins)::int AS scrutins, sum(votants)::int AS votants, sum(delegations)::int AS delegations
              FROM stats_participation_elu
             WHERE parlementaire_id = @parlementaireId
             GROUP BY 1 HAVING sum(scrutins) > 0
             ORDER BY 1
            """, new { parlementaireId })).ToList();
    }

    public async Task<Resultats<LigneVote>> GetVotesAsync(
        string parlementaireId, string chambre, bool surEnsembleSeulement, int page, int parPage,
        DateTime? du = null, DateTime? au = null, string? recherche = null, string? filtreVote = null)
    {
        using var conn = db.CreateConnection();
        page = Math.Max(1, page);
        var mots = RechercheSql.Mots(recherche);   // chaque mot dans le titre (français ou anglais) ; tirets et apostrophes = espaces
        // Liste blanche : une valeur inconnue = pas de filtre (jamais de SQL construit depuis l'adresse).
        var vote = filtreVote is "pour" or "contre" or "abstention" or "non_votant" or "absent" or "delegation" or "mise_au_point" ? filtreVote : null;

        const string from =
            """
              FROM scrutins s
              LEFT JOIN votes v ON v.scrutin_id = s.id AND v.parlementaire_id = @parlementaireId
             WHERE s.chambre = @chambre AND NOT s.nominatif_incomplet
               AND (NOT @ensemble OR s.sur_ensemble)
               AND (@du::date IS NULL OR s.date_scrutin >= @du::date)
               AND (@au::date IS NULL OR s.date_scrutin <= @au::date)
               AND (cardinality(@mots) = 0 OR recherche_norm(s.titre) ILIKE ALL (@mots) OR recherche_norm(s.titre_en) ILIKE ALL (@mots))
               -- Filtre de vote : « absent » = pas de ligne dans votes (les absents ne sont pas stockés) ; délégation et mise au point
               -- sont des attributs d'un vote existant, indépendants de la position.
               AND (@vote::text IS NULL OR CASE @vote
                        WHEN 'absent' THEN COALESCE(v.position, 'absent') = 'absent'
                        WHEN 'delegation' THEN COALESCE(v.par_delegation, false)
                        WHEN 'mise_au_point' THEN COALESCE(v.rectifie, false)
                        ELSE v.position = @vote END)
               AND EXISTS (SELECT 1 FROM mandats m
                            WHERE m.parlementaire_id = @parlementaireId
                              AND (m.legislature IS NULL OR m.legislature = s.legislature)
                              AND m.date_debut <= s.date_scrutin
                              AND (m.date_fin IS NULL OR m.date_fin >= s.date_scrutin))
            """;
        var p = new { parlementaireId, chambre, ensemble = surEnsembleSeulement, du, au, mots, vote, limit = parPage, offset = (page - 1) * parPage };

        var total = await conn.ExecuteScalarAsync<int>($"SELECT count(*) {from}", p);
        var lignes = (await conn.QueryAsync<LigneVote>(
            $"""
             SELECT s.id AS scrutin_id, s.date_scrutin, s.titre, s.titre_en, s.titre_en_auto, s.titre_fr_auto, s.sort,
                    COALESCE(v.position, 'absent') AS position,
                    COALESCE(v.par_delegation, false) AS par_delegation,
                    COALESCE(v.rectifie, false) AS rectifie,
                    v.position_origine, COALESCE(v.origine_inconnue, false) AS origine_inconnue
               {from}
              ORDER BY s.date_scrutin DESC, s.numero DESC
              LIMIT @limit OFFSET @offset
             """, p)).ToList();

        return new Resultats<LigneVote>(lignes, total, page, parPage);
    }
}
