using Dapper;
using IlsOntVote.Web.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IlsOntVote.Web.Pages.Admin;

// Classes à propriétés (et non records à constructeur) : Dapper lie un constructeur positionnel par nom ET type exact, sans conversion,
// et échoue dès qu'un type SQL diffère (« A parameterless default constructor or one matching… », bug rencontré sur cette page).

/// <summary>Un scrutin à signaler (décompte incomplet, ou vote sur l'ensemble sans texte).</summary>
public sealed class ScrutinSignale
{
    public string Id { get; set; } = "";
    public string Chambre { get; set; } = "";
    public DateTime DateScrutin { get; set; }
    public string Titre { get; set; } = "";
    public int Manquants { get; set; }
}

/// <summary>Dernier import réussi d'une source.</summary>
public sealed class FraicheurImport
{
    public string Source { get; set; } = "";
    public DateTime? FinishedAt { get; set; }
    public string? Message { get; set; }
    /// <summary>Âge en heures du dernier succès ; au-delà de <see cref="QualiteModel.SeuilHeures"/>, la source est signalée.</summary>
    public double AgeHeures => FinishedAt is null ? double.MaxValue : (DateTime.UtcNow - FinishedAt.Value.ToUniversalTime()).TotalHours;
}

/// <summary>Décision de séance à l'Assemblée qu'on n'a pas su rattacher à un texte (donc non affichée sur le site).</summary>
public sealed class EtapeOrpheline
{
    public string DossierAn { get; set; } = "";
    public string CodeActe { get; set; } = "";
    public DateTime DateActe { get; set; }
    public string? Resultat { get; set; }
    public bool SansScrutinPublic { get; set; }

    /// <summary>Adresse du dossier sur le site de l'Assemblée (« DLR5L17N50450 » → législature 17) ; redirige vers la page du dossier.</summary>
    public string LienAn => $"https://www.assemblee-nationale.fr/{DossierAn.Substring(5, 2)}/dossiers/{DossierAn}.asp";
}

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
    public double? AgeHeures => Debut is null ? null : (DateTime.UtcNow - Debut.Value.ToUniversalTime()).TotalHours;
    public TimeSpan? Duree => Debut is null || Fin is null ? null : Fin - Debut;
}

/// <summary>Part des lignes d'une table sans thème repéré (classement par mots-clés : migrations 048 et 057).</summary>
public sealed class ThemeCouverture
{
    public string Table { get; set; } = "";
    public int Total { get; set; }
    public int SansTheme { get; set; }
}

/// <summary>Tableau de bord « qualité des données » : ce que le site sait ne pas savoir, et l'état des imports (règle du doute).</summary>
public class QualiteModel(DapperContext db) : PageModel
{
    /// <summary>Une source dont le dernier succès a plus de 3 jours est signalée (l'import tourne chaque nuit).</summary>
    public const int SeuilHeures = 72;

    public int ScrutinsIncomplets { get; private set; }
    public int EnsemblesSansTexte { get; private set; }
    public int MisesAuPointSansOrigine { get; private set; }
    public int Sieges { get; private set; }
    public int Etapes { get; private set; }
    public int EtapesSansScrutin { get; private set; }
    public int EtapesOrphelines { get; private set; }
    public bool? StatsRemplies { get; private set; }
    public IReadOnlyList<FraicheurImport> Imports { get; private set; } = [];
    public IReadOnlyList<ScrutinSignale> Incomplets { get; private set; } = [];
    public IReadOnlyList<ScrutinSignale> SansTexte { get; private set; } = [];
    public IReadOnlyList<EtapeOrpheline> Orphelines { get; private set; } = [];
    public IReadOnlyList<ChambreQualite> Chambres { get; private set; } = [];
    public IReadOnlyList<TacheQualite> Taches { get; private set; } = [];
    public IReadOnlyList<ThemeCouverture> Themes { get; private set; } = [];
    /// <summary>Une tâche NON ponctuelle sans exécution depuis plus de 48 h est signalée (elles tournent chaque nuit).</summary>
    public const int SeuilTacheHeures = 48;
    /// <summary>Une chambre active sans scrutin depuis plus de 60 jours est signalée (hors vacances parlementaires habituelles).</summary>
    public const int SeuilChambreJours = 60;

    public async Task OnGetAsync()
    {
        using var cx = db.CreateConnection();
        // Décompte nominatif plus court que le total officiel : les « absents » de ces scrutins peuvent être de faux absents.
        ScrutinsIncomplets = await cx.ExecuteScalarAsync<int>("SELECT count(*)::int FROM scrutins WHERE nominatif_incomplet");
        // Votes sur l'ensemble d'un texte que le rapprochement par titre n'a pas pu rattacher (ambigu ou introuvable). Le Parlement européen est exclu :
        // ses votes ne se rattachent pas aux dossiers législatifs français.
        EnsemblesSansTexte = await cx.ExecuteScalarAsync<int>("SELECT count(*)::int FROM scrutins WHERE sur_ensemble AND texte_id IS NULL AND chambre <> 'UE'");
        // Mises au point du Sénat dont la position d'origine n'est pas publiée.
        MisesAuPointSansOrigine = await cx.ExecuteScalarAsync<int>("SELECT count(*)::int FROM votes WHERE origine_inconnue");
        // Contrôle de cohérence des mandats AN : doit rester <= 577 (piège des suppléants : début de mandat = prise de fonction, pas date d'élection).
        Sieges = await cx.ExecuteScalarAsync<int>(
            """
            SELECT count(DISTINCT m.parlementaire_id)::int
              FROM mandats m JOIN parlementaires p ON p.id = m.parlementaire_id
             WHERE p.chambre = 'AN' AND m.date_debut <= current_date AND (m.date_fin IS NULL OR m.date_fin >= current_date)
               AND m.legislature = (SELECT max(m2.legislature) FROM mandats m2 JOIN parlementaires p2 ON p2.id = m2.parlementaire_id WHERE p2.chambre = 'AN')
            """);

        // Étapes de lecture (Dossiers législatifs de l'AN, migration 014) : total, sans scrutin public (main levée), non rattachées à un texte.
        (Etapes, EtapesSansScrutin, EtapesOrphelines) = await cx.QuerySingleAsync<(int, int, int)>(
            "SELECT count(*)::int, count(*) FILTER (WHERE sans_scrutin_public)::int, count(*) FILTER (WHERE texte_id IS NULL)::int FROM texte_etapes");

        // Vues de statistiques (migration 013) : vides tant que l'import n'a pas calculé la participation.
        StatsRemplies = await cx.ExecuteScalarAsync<bool?>("SELECT ispopulated FROM pg_matviews WHERE matviewname = 'stats_participation_groupe'");

        // Dernier succès par source d'import : une source muette depuis plus de 3 jours est signalée.
        Imports = (await cx.QueryAsync<FraicheurImport>(
            """
            SELECT DISTINCT ON (source) source, finished_at, message
              FROM import_runs WHERE ok ORDER BY source, finished_at DESC
            """)).OrderBy(i => i.Source).ToList();

        // Par chambre : volumes, fraîcheur, scrutins incomplets ou sans résultat, effectif en exercice face aux sièges, mandats qui se chevauchent, élus sans groupe.
        // La législature « courante » est calculée UNE fois par chambre (CTE) : en sous-requête corrélée elle serait recalculée à chaque ligne.
        Chambres = (await cx.QueryAsync<ChambreQualite>(
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

        // Dernière exécution de chaque tâche du catalogue (planificateur, migration 026).
        Taches = (await cx.QueryAsync<TacheQualite>(
            """
            SELECT t.code, t.libelle, t.pays, t.ponctuelle, e.debut, e.fin, e.ok, e.nb_erreurs
              FROM import_taches t
              LEFT JOIN LATERAL (SELECT x.debut, x.fin, x.ok, x.nb_erreurs FROM import_executions x WHERE x.tache = t.code ORDER BY x.debut DESC LIMIT 1) e ON true
             ORDER BY t.ponctuelle, t.ordre, t.code
            """)).ToList();

        // Couverture des thèmes (classement approximatif par mots-clés) : une table dont la colonne manque (migration non appliquée) est simplement omise.
        var themes = new List<ThemeCouverture>();
        foreach (var table in new[] { "scrutins", "textes", "petitions", "bills" })
        {
            try
            {
                var (total, sans) = await cx.QuerySingleAsync<(int, int)>($"SELECT count(*)::int, count(*) FILTER (WHERE cardinality(themes) = 0)::int FROM {table}");
                themes.Add(new ThemeCouverture { Table = table, Total = total, SansTheme = sans });
            }
            catch (Npgsql.PostgresException) { /* colonne « themes » absente */ }
        }
        Themes = themes;

        Incomplets = (await cx.QueryAsync<ScrutinSignale>(
            "SELECT id, chambre, date_scrutin, titre, nb_votes_manquants AS manquants FROM scrutins WHERE nominatif_incomplet ORDER BY date_scrutin DESC LIMIT 50")).ToList();
        SansTexte = (await cx.QueryAsync<ScrutinSignale>(
            "SELECT id, chambre, date_scrutin, titre, 0 AS manquants FROM scrutins WHERE sur_ensemble AND texte_id IS NULL AND chambre <> 'UE' AND NOT nominatif_incomplet ORDER BY date_scrutin DESC LIMIT 50")).ToList();
        Orphelines = (await cx.QueryAsync<EtapeOrpheline>(
            "SELECT dossier_an, code_acte, date_acte, resultat, sans_scrutin_public FROM texte_etapes WHERE texte_id IS NULL ORDER BY date_acte DESC LIMIT 50")).ToList();
    }
}
