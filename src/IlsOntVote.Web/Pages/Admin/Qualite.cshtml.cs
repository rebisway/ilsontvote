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
        // Contrôle de cohérence des mandats AN : doit rester <= 577 (cf. CLAUDE.md, piège des suppléants).
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

        Incomplets = (await cx.QueryAsync<ScrutinSignale>(
            "SELECT id, chambre, date_scrutin, titre, nb_votes_manquants AS manquants FROM scrutins WHERE nominatif_incomplet ORDER BY date_scrutin DESC LIMIT 50")).ToList();
        SansTexte = (await cx.QueryAsync<ScrutinSignale>(
            "SELECT id, chambre, date_scrutin, titre, 0 AS manquants FROM scrutins WHERE sur_ensemble AND texte_id IS NULL AND chambre <> 'UE' AND NOT nominatif_incomplet ORDER BY date_scrutin DESC LIMIT 50")).ToList();
        Orphelines = (await cx.QueryAsync<EtapeOrpheline>(
            "SELECT dossier_an, code_acte, date_acte, resultat, sans_scrutin_public FROM texte_etapes WHERE texte_id IS NULL ORDER BY date_acte DESC LIMIT 50")).ToList();
    }
}
