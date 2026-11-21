using IlsOntVote.Web.Models;
using IlsOntVote.Web.Repositories;
using IlsOntVote.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace IlsOntVote.Web.Pages;

/// <summary>
/// Liste COMPLÈTE des élus d'une chambre avec leurs taux sur une période (participation, présence effective, délégation, mise au point,
/// absence), avec rang dans le tri, filtres, tri par colonne et export. Aucun seuil de scrutins : le nombre de scrutins est affiché pour que
/// le lecteur juge lui-même du poids de chaque taux. Chiffres calculés par le site (cf. migration 019), jamais présentés comme officiels.
/// </summary>
public class ElusModel(IStatistiquesRepository stats, Langue langue, PaysSite site) : ListePageModel
{
    [BindProperty(SupportsGet = true)] public string? Chambre { get; set; }
    [BindProperty(SupportsGet = true)] public string? Periode { get; set; }
    [BindProperty(SupportsGet = true)] public string? Du { get; set; }
    [BindProperty(SupportsGet = true)] public string? Au { get; set; }
    [BindProperty(SupportsGet = true)] public string? Ensemble { get; set; }
    /// <summary>« 1 » : masquer les élus qui n'ont exprimé aucun vote sur la période.</summary>
    [BindProperty(SupportsGet = true)] public string? Votants { get; set; }
    [BindProperty(SupportsGet = true, Name = "q")] public string? Recherche { get; set; }

    public bool EnsembleSeul => Ensemble == "1";
    public bool AvecVotes => Votants == "1";
    /// <summary>Sur le site d'un pays (/uk) : sa seule chambre, quel que soit le paramètre « chambre ».</summary>
    /// <summary>Chambres du site courant (sélecteur sous un pays).</summary>
    public string[] ChambresSite => site.ChambresVisibles(langue.Pays);
    public string ChambreCode => langue.Pays is not null ? Chambres.DepuisFiltre(Chambre, ChambresSite) ?? ChambresSite.FirstOrDefault() ?? Chambres.Commons
        : Chambre switch { "senat" => Chambres.Senat, "ue" => Chambres.UE, "commons" => Chambres.Commons, _ => Chambres.AN };
    public string ChambreCle => Chambres.Filtre(ChambreCode);
    /// <summary>« perso » si une date est saisie, sinon « 12m » (défaut), « annee » ou « tout ».</summary>
    public string PeriodeCle => !string.IsNullOrEmpty(Du) || !string.IsNullOrEmpty(Au) ? "perso" : Periode is "annee" or "tout" ? Periode : "12m";
    public string PeriodeLibelle { get; private set; } = "";
    public Resultats<StatEluDetail> Resultats { get; private set; } = new([], 0, 1, 100);
    public override int TotalLignes => Resultats.Total;

    protected override int ParPageDefaut => 100;
    protected override string TriDefaut => "participation";
    protected override IReadOnlyCollection<string> ClesTri { get; } = [.. StatistiquesRepository.ClesTriElus];
    // Les taux et effectifs : le premier clic trie du plus grand au plus petit ; nom et groupe : alphabétique.
    protected override bool DecroissantInitial(string cle) => cle is not ("nom" or "groupe");
    protected override IEnumerable<(string Nom, string? Valeur)> Filtres =>
        [("q", Recherche), ("chambre", ChambreCle), ("periode", PeriodeCle is "annee" or "tout" ? PeriodeCle : null), ("du", Du), ("au", Au), ("ensemble", EnsembleSeul ? "1" : null), ("votants", AvecVotes ? "1" : null)];

    /// <summary>Filtre « actif » : tout sauf les valeurs par défaut (Assemblée, 12 mois, pas de recherche).</summary>
    public new bool FiltreActif => !string.IsNullOrWhiteSpace(Recherche) || PeriodeCle != "12m" || EnsembleSeul || AvecVotes || ChambreCode != Chambres.AN;

    private IReadOnlyList<Colonne<StatEluDetail>> ColonnesExport()
    {
        var en = langue.Anglais;
        return new List<Colonne<StatEluDetail>>
        {
            new(langue.T("nom"), e => e.Nom),
            new(langue.T("prenom"), e => e.Prenom),
            new(langue.T("groupe"), e => e.GroupeSigle ?? ""),
            new(langue.T("scrutins"), e => e.Scrutins),
            new(langue.T("votes-exprimes"), e => e.Votants),
            new(langue.T("participation-2"), e => e.ParticipationPct),
            new(langue.T("presence-2"), e => e.PresencePct),
            new(langue.T("delegation-2"), e => e.DelegationPct),
            new(langue.T("mise-au-point-2"), e => e.MiseAuPointPct),
            new(langue.T("absence-2"), e => e.AbsencePct),
            new(langue.T("delegations"), e => e.Delegations),
            new(langue.T("mises-au-point"), e => e.MisesAuPoint),
        };
    }

    public async Task<IActionResult> OnGetAsync()
    {
        // Chambres du site courant seulement (statistiques des Communes sous /uk, des chambres françaises sur le site français).
        if (!site.ChambresVisibles(langue.Pays).Contains(ChambreCode)) return NotFound();
        var fr = langue.Culture;
        DateTime? du, au;
        var aujourdhui = DateTime.UtcNow.Date;
        switch (PeriodeCle)
        {
            case "perso":
                (du, au) = Services.Periode.Lire(Du, Au);
                PeriodeLibelle = langue.Tf("periode-du-au", du?.ToString("d MMMM yyyy", fr) ?? langue.T("periode-debut"), au?.ToString("d MMMM yyyy", fr) ?? langue.T("periode-aujourdhui"));
                break;
            case "annee": du = new DateTime(aujourdhui.Year, 1, 1); au = null; PeriodeLibelle = langue.Tf("periode-annee", aujourdhui.Year); break;
            case "tout": du = null; au = null; PeriodeLibelle = langue.T("depuis-le-debut-des-donnees-2"); break;
            default: du = aujourdhui.AddMonths(-12); au = null; PeriodeLibelle = langue.T("12-derniers-mois"); break;
        }

        if (ExportDemande)
        {
            var tout = await stats.ListerElusAsync(ChambreCode, du, au, EnsembleSeul, AvecVotes, Recherche, TriEffectif, Decroissant, 1, 0);
            return Exporteur.Ecrire(Format!, langue.T("taux-des-elus"), ColonnesExport(), tout.Lignes);
        }
        Resultats = await stats.ListerElusAsync(ChambreCode, du, au, EnsembleSeul, AvecVotes, Recherche, TriEffectif, Decroissant, NumeroPage, ParPage);
        return Page();
    }
}
