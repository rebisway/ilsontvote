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
public class StatistiquesModel(IStatistiquesRepository stats) : PageModel
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
    public string ChambreCode => Chambre switch { "senat" => Chambres.Senat, "ue" => Chambres.UE, _ => Chambres.AN };
    public string ChambreCle => Chambres.Filtre(ChambreCode);
    /// <summary>Période effective : « perso » si une date est saisie, sinon la période prédéfinie valide (12 derniers mois par défaut).</summary>
    public string PeriodeCle => !string.IsNullOrEmpty(Du) || !string.IsNullOrEmpty(Au) ? "perso" : Periode is "annee" or "tout" ? Periode : "12m";
    public bool FiltreActif => PeriodeCle != "12m" || EnsembleSeul || ChambreCode != Chambres.AN;
    /// <summary>Libellé de la période affichée (rappel au-dessus des chiffres : on ne montre jamais un chiffre sans sa période).</summary>
    public string PeriodeLibelle { get; private set; } = "";

    public ChiffresCles Chiffres { get; private set; } = new(0, 0, 0, 0, 0, null);
    public IReadOnlyList<StatGroupe> Groupes { get; private set; } = [];
    public IReadOnlyList<StatAnnee> Annees { get; private set; } = [];
    public IReadOnlyList<StatElu> PlusPresents { get; private set; } = [];
    public IReadOnlyList<StatElu> MoinsPresents { get; private set; } = [];
    /// <summary>Seuil du classement : un élu doit avoir eu au moins ce nombre de scrutins à sa charge sur la période.</summary>
    public const int MinScrutinsClassement = 20;

    private static readonly IReadOnlyList<Colonne<StatGroupe>> ColonnesExport =
    [
        new("Groupe", g => g.Sigle),
        new("Nom du groupe", g => g.Libelle),
        new("Effectif moyen", g => g.EffectifMoyen),
        new("Participation (%)", g => g.ParticipationPct),
        new("Pour (% des votants)", g => g.PourPct),
        new("Contre (% des votants)", g => g.ContrePct),
        new("Abstention (% des votants)", g => g.AbstentionPct),
        new("Cohésion (%)", g => g.CohesionPct),
    ];

    public async Task<IActionResult> OnGetAsync()
    {
        var fr = new System.Globalization.CultureInfo("fr-FR");
        DateTime? du, au;
        var aujourdhui = DateTime.UtcNow.Date;
        switch (PeriodeCle)
        {
            case "perso":
                (du, au) = Services.Periode.Lire(Du, Au);
                PeriodeLibelle = $"du {du?.ToString("d MMMM yyyy", fr) ?? "début"} au {au?.ToString("d MMMM yyyy", fr) ?? "aujourd'hui"}";
                break;
            case "annee": du = new DateTime(aujourdhui.Year, 1, 1); au = null; PeriodeLibelle = $"année {aujourdhui.Year}"; break;
            case "tout": du = null; au = null; PeriodeLibelle = "depuis le début des données"; break;
            default: du = aujourdhui.AddMonths(-12); au = null; PeriodeLibelle = "12 derniers mois"; break;
        }
        Groupes = await stats.GetGroupesAsync(ChambreCode, du, au, EnsembleSeul);

        if (Exporteur.EstFormat(Format))
            return Exporteur.Ecrire(Format!, "participation-par-groupe", ColonnesExport, Groupes);

        Chiffres = await stats.GetChiffresAsync(ChambreCode, du, au, EnsembleSeul);
        Annees = await stats.GetAnneesAsync(ChambreCode);
        PlusPresents = await stats.GetElusAsync(ChambreCode, du, au, EnsembleSeul, true, 10, MinScrutinsClassement);
        MoinsPresents = await stats.GetElusAsync(ChambreCode, du, au, EnsembleSeul, false, 10, MinScrutinsClassement);
        return Page();
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
        return "/statistiques?" + string.Join("&", q);
    }
}
