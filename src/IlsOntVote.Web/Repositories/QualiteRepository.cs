using Dapper;
using IlsOntVote.Web.Data;

namespace IlsOntVote.Web.Repositories;

/// <summary>Bilan de qualité d'une chambre (une ligne du tableau « Par chambre »).</summary>
public sealed class ChambreQualite
{
    public string Code { get; set; } = "";
    public string Nom { get; set; } = "";
    public string Pays { get; set; } = "";
    public bool Actif { get; set; }
    /// <summary>Nombre de sièges déclaré dans <c>chambres</c> ; null si la chambre n'a pas d'effectif fixe (Lords).</summary>
    public int? Sieges { get; set; }
    public int Scrutins { get; set; }
    public DateTime? Dernier { get; set; }
    public int Incomplets { get; set; }
    public int SansResultat { get; set; }
    /// <summary>Élus dont un mandat couvre aujourd'hui, dans la législature la plus récente de la chambre.</summary>
    public int EnExercice { get; set; }
    /// <summary>Paires de mandats d'un même élu, dans la même législature, dont les dates se chevauchent.</summary>
    public int Chevauchants { get; set; }
    public int SansGroupe { get; set; }
    /// <summary>Âge en jours du dernier scrutin ; null sans scrutin.</summary>
    public int? AgeJours => Dernier is null ? null : (int)(DateTime.UtcNow.Date - Dernier.Value.Date).TotalDays;
    /// <summary>Effectif en exercice au-dessus des sièges : mandats mal datés (suppléants, départs non enregistrés).</summary>
    public bool AuDessusDesSieges => Sieges is > 0 && EnExercice > Sieges;
}

/// <summary>Dernière exécution d'une tâche d'import planifiée (table <c>import_executions</c>).</summary>
public sealed class TacheQualite
{
    public string Code { get; set; } = "";
    public string Libelle { get; set; } = "";
    public string? Pays { get; set; }
    public bool Ponctuelle { get; set; }
    public DateTime? Debut { get; set; }
    public DateTime? Fin { get; set; }
    public bool? Ok { get; set; }
    public int? NbErreurs { get; set; }
    /// <summary>Fin de la dernière exécution réussie ; null si aucune.</summary>
    public DateTime? DernierSucces { get; set; }
    public double? AgeHeures => Debut is null ? null : (DateTime.UtcNow - Debut.Value.ToUniversalTime()).TotalHours;
    public TimeSpan? Duree => Debut is null || Fin is null ? null : Fin - Debut;
}

/// <summary>État des données et des imports, commun à /Admin/Qualite et au serveur MCP (qui n'en expose qu'une partie).</summary>
public interface IQualiteRepository
{
    /// <summary>Une ligne par chambre décrite en base, actives d'abord.</summary>
    Task<IReadOnlyList<ChambreQualite>> ChambresAsync();
    /// <summary>Chaque tâche du catalogue d'import avec sa dernière exécution.</summary>
    Task<IReadOnlyList<TacheQualite>> TachesAsync();
}

public sealed class QualiteRepository(DapperContext db) : IQualiteRepository
{
    public async Task<IReadOnlyList<ChambreQualite>> ChambresAsync()
    {
        using var cx = db.CreateConnection();
        // Par chambre : volumes, fraîcheur, scrutins incomplets ou sans résultat, effectif en exercice face aux sièges, mandats qui se chevauchent, élus sans groupe.
        // La législature « courante » est calculée UNE fois par chambre (CTE) : en sous-requête corrélée elle serait recalculée à chaque ligne.
        return (await cx.QueryAsync<ChambreQualite>(
            """
            WITH cur AS (
                SELECT p.chambre, max(m.legislature) AS l FROM mandats m JOIN parlementaires p ON p.id = m.parlementaire_id GROUP BY p.chambre
            ), act AS (
                SELECT p.chambre, count(DISTINCT p.id)::int AS n
                  FROM parlementaires p JOIN mandats m ON m.parlementaire_id = p.id JOIN cur ON cur.chambre = p.chambre
                 WHERE m.date_debut <= current_date AND (m.date_fin IS NULL OR m.date_fin >= current_date) AND (m.legislature IS NULL OR m.legislature = cur.l)
                 GROUP BY p.chambre
            ), chev AS (
                -- Même élu, même législature (NULL = Sénat, Lords : une seule « législature ») : deux mandats dont les intervalles se recoupent.
                SELECT p.chambre, count(*)::int AS n
                  FROM mandats a JOIN mandats b ON a.parlementaire_id = b.parlementaire_id AND a.ctid < b.ctid
                       AND COALESCE(a.legislature, -1) = COALESCE(b.legislature, -1)
                       AND a.date_debut <= COALESCE(b.date_fin, 'infinity') AND b.date_debut <= COALESCE(a.date_fin, 'infinity')
                  JOIN parlementaires p ON p.id = a.parlementaire_id
                 GROUP BY p.chambre
            ), sg AS (
                SELECT p.chambre, count(*)::int AS n FROM parlementaires p
                 WHERE NOT EXISTS (SELECT 1 FROM groupe_appartenances ga WHERE ga.parlementaire_id = p.id) GROUP BY p.chambre
            ), sc AS (
                SELECT chambre, count(*)::int AS scrutins, max(date_scrutin) AS dernier,
                       count(*) FILTER (WHERE nominatif_incomplet)::int AS incomplets, count(*) FILTER (WHERE sort IS NULL)::int AS sans_resultat
                  FROM scrutins GROUP BY chambre
            )
            SELECT c.code, c.nom_fr AS nom, c.pays, c.actif, c.nb_sieges AS sieges, COALESCE(sc.scrutins, 0) AS scrutins, sc.dernier,
                   COALESCE(sc.incomplets, 0) AS incomplets, COALESCE(sc.sans_resultat, 0) AS sans_resultat,
                   COALESCE(act.n, 0) AS en_exercice, COALESCE(chev.n, 0) AS chevauchants, COALESCE(sg.n, 0) AS sans_groupe
              FROM chambres c
              LEFT JOIN sc ON sc.chambre = c.code LEFT JOIN act ON act.chambre = c.code LEFT JOIN chev ON chev.chambre = c.code LEFT JOIN sg ON sg.chambre = c.code
             ORDER BY c.actif DESC, c.ordre, c.code
            """)).ToList();
    }

    public async Task<IReadOnlyList<TacheQualite>> TachesAsync()
    {
        using var cx = db.CreateConnection();
        // Dernière exécution (planificateur, migration 026) et fin du dernier succès.
        return (await cx.QueryAsync<TacheQualite>(
            """
            SELECT t.code, t.libelle, t.pays, t.ponctuelle, e.debut, e.fin, e.ok, e.nb_erreurs,
                   (SELECT max(r.fin) FROM import_executions r WHERE r.tache = t.code AND r.ok) AS dernier_succes
              FROM import_taches t
              LEFT JOIN LATERAL (SELECT x.debut, x.fin, x.ok, x.nb_erreurs FROM import_executions x WHERE x.tache = t.code ORDER BY x.debut DESC LIMIT 1) e ON true
             ORDER BY t.ponctuelle, t.ordre, t.code
            """)).ToList();
    }
}
