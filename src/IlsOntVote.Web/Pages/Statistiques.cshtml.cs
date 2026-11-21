using IlsOntVote.Web.Models;
using IlsOntVote.Web.Repositories;
using IlsOntVote.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IlsOntVote.Web.Pages;

/// <summary>
/// Statistiques publiques d'une chambre : chiffres clés, participation par groupe, activité par année. Filtres : chambre, période
/// (bornes incluses), « votes sur l'ensemble seulement ». La participation est calculée à l'import (cf. migration 013) : on la présente
/// toujours comme une donnée calculée, jamais comme un chiffre officiel.
/// </summary>
public class StatistiquesModel(IStatistiquesRepository stats, Langue langue, PaysSite site) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Chambre { get; set; }
    /// <summary>Période prédéfinie : « 12m » (défaut), « annee » (année en cours) ou « tout » ; ignorée si des dates sont saisies.</summary>
    [BindProperty(SupportsGet = true)] public string? Periode { get; set; }
    [BindProperty(SupportsGet = true)] public string? Du { get; set; }
    [BindProperty(SupportsGet = true)] public string? Au { get; set; }
    /// <summary>« 1 » = ne compter que les votes sur l'ensemble d'un texte (sans amendements ni articles).</summary>
    [BindProperty(SupportsGet = true)] public string? Ensemble { get; set; }
    [BindProperty(SupportsGet = true)] public string? Format { get; set; }

    public bool EnsembleSeul => Ensemble == "1";
    /// <summary>Code de chambre validé (liste blanche) : AN par défaut.</summary>
    /// <summary>Chambres du site courant (sélecteur sous un pays).</summary>
    public string[] ChambresSite => site.ChambresVisibles(langue.Pays);
    public string ChambreCode => langue.Pays is not null ? Chambres.DepuisFiltre(Chambre, ChambresSite) ?? ChambresSite.FirstOrDefault() ?? Chambres.Commons : Chambre switch { "senat" => Chambres.Senat, "ue" => Chambres.UE, _ => Chambres.AN };
    /// <summary>Chambre affichée sans filtre : l'Assemblée sur le site français, la chambre du pays sinon (le filtre « chambre » n'y existe pas).</summary>
    private string ChambreDefaut => langue.Pays is not null ? ChambresSite.FirstOrDefault() ?? Chambres.Commons : Chambres.AN;
    public string ChambreCle => Chambres.Filtre(ChambreCode);
    /// <summary>Période effective : « perso » si une date est saisie, sinon la période prédéfinie valide (12 derniers mois par défaut).</summary>
    public string PeriodeCle => !string.IsNullOrEmpty(Du) || !string.IsNullOrEmpty(Au) ? "perso" : Periode is "annee" or "tout" ? Periode : "12m";
    public bool FiltreActif => PeriodeCle != "12m" || EnsembleSeul || ChambreCode != ChambreDefaut;
    /// <summary>Libellé de la période affichée (rappel au-dessus des chiffres : on ne montre jamais un chiffre sans sa période).</summary>
    public string PeriodeLibelle { get; private set; } = "";

    public ChiffresCles Chiffres { get; private set; } = new(0, 0, 0, 0, 0, null);
    public IReadOnlyList<StatGroupe> Groupes { get; private set; } = [];
    public IReadOnlyList<StatAnnee> Annees { get; private set; } = [];

    /// <summary>En-têtes des colonnes de l'export dans la langue de la page.</summary>
    private IReadOnlyList<Colonne<StatGroupe>> ColonnesExport()
    {
        var en = langue.Anglais;
        return new List<Colonne<StatGroupe>>
        {
            new(langue.T("groupe"), g => g.Sigle),
            new(langue.T("nom-du-groupe"), g => g.Libelle),
            new(langue.T("effectif-moyen"), g => g.EffectifMoyen),
            new("Participation (%)", g => g.ParticipationPct),
            new(langue.T("pour-des-votants"), g => g.PourPct),
            new(langue.T("contre-des-votants"), g => g.ContrePct),
            new(langue.T("abstention-des-votants"), g => g.AbstentionPct),
            new(langue.T("cohesion-2"), g => g.CohesionPct),
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
        Groupes = await stats.GetGroupesAsync(ChambreCode, du, au, EnsembleSeul);

        if (Exporteur.EstFormat(Format))
            return Exporteur.Ecrire(Format!, langue.T("participation-par-groupe-2"), ColonnesExport(), Groupes);

        Chiffres = await stats.GetChiffresAsync(ChambreCode, du, au, EnsembleSeul);
        Annees = await stats.GetAnneesAsync(ChambreCode);
        return Page();
    }

    /// <summary>Adresse de la liste complète des élus (/elus) avec la chambre, la période et l'option « votes sur l'ensemble » actives.</summary>
    public string LienElus()
    {
        var q = new List<string> { "chambre=" + ChambreCle };
        if (PeriodeCle is "annee" or "tout") q.Add("periode=" + PeriodeCle);
        if (!string.IsNullOrEmpty(Du)) q.Add("du=" + Uri.EscapeDataString(Du));
        if (!string.IsNullOrEmpty(Au)) q.Add("au=" + Uri.EscapeDataString(Au));
        if (EnsembleSeul) q.Add("ensemble=1");
        return Request.PathBase + "/elus?" + string.Join("&", q);
    }

    /// <summary>Adresse de la page avec les filtres actifs (pour l'export).</summary>
    public string Lien(string? format = null)
    {
        var q = new List<string> { "chambre=" + ChambreCle };
        if (PeriodeCle is "annee" or "tout") q.Add("periode=" + PeriodeCle);
        if (!string.IsNullOrEmpty(Du)) q.Add("du=" + Uri.EscapeDataString(Du));
        if (!string.IsNullOrEmpty(Au)) q.Add("au=" + Uri.EscapeDataString(Au));
        if (EnsembleSeul) q.Add("ensemble=1");
        if (format is not null) q.Add("format=" + format);
        return Request.PathBase + "/statistiques?" + string.Join("&", q);
    }
}
