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
public class ElusModel(IStatistiquesRepository stats, Langue langue) : ListePageModel
{
    [BindProperty(SupportsGet = true)] public string? Chambre { get; set; }
    [BindProperty(SupportsGet = true)] public string? Periode { get; set; }
    [BindProperty(SupportsGet = true)] public string? Du { get; set; }
    [BindProperty(SupportsGet = true)] public string? Au { get; set; }
    [BindProperty(SupportsGet = true)] public string? Ensemble { get; set; }
    [BindProperty(SupportsGet = true, Name = "q")] public string? Recherche { get; set; }

    public bool EnsembleSeul => Ensemble == "1";
    public string ChambreCode => Chambre switch { "senat" => Chambres.Senat, "ue" => Chambres.UE, _ => Chambres.AN };
    public string ChambreCle => Chambres.Filtre(ChambreCode);
    /// <summary>« perso » si une date est saisie, sinon « 12m » (défaut), « annee » ou « tout ».</summary>
    public string PeriodeCle => !string.IsNullOrEmpty(Du) || !string.IsNullOrEmpty(Au) ? "perso" : Periode is "annee" or "tout" ? Periode : "12m";
    public string PeriodeLibelle { get; private set; } = "";
    public Resultats<StatEluDetail> Resultats { get; private set; } = new([], 0, 1, 100);

    protected override int ParPageDefaut => 100;
    protected override string TriDefaut => "participation";
    protected override IReadOnlyCollection<string> ClesTri { get; } = [.. StatistiquesRepository.ClesTriElus];
    // Les taux et effectifs : le premier clic trie du plus grand au plus petit ; nom et groupe : alphabétique.
    protected override bool DecroissantInitial(string cle) => cle is not ("nom" or "groupe");
    protected override IEnumerable<(string Nom, string? Valeur)> Filtres =>
        [("q", Recherche), ("chambre", ChambreCle), ("periode", PeriodeCle is "annee" or "tout" ? PeriodeCle : null), ("du", Du), ("au", Au), ("ensemble", EnsembleSeul ? "1" : null)];

    /// <summary>Filtre « actif » : tout sauf les valeurs par défaut (Assemblée, 12 mois, pas de recherche).</summary>
    public new bool FiltreActif => !string.IsNullOrWhiteSpace(Recherche) || PeriodeCle != "12m" || EnsembleSeul || ChambreCode != Chambres.AN;

    private IReadOnlyList<Colonne<StatEluDetail>> ColonnesExport()
    {
        var en = langue.Anglais;
        return new List<Colonne<StatEluDetail>>
        {
            new(en ? "Last name" : "Nom", e => e.Nom),
            new(en ? "First name" : "Prénom", e => e.Prenom),
            new(en ? "Group" : "Groupe", e => e.GroupeSigle ?? ""),
            new(en ? "Votes counted" : "Scrutins", e => e.Scrutins),
            new(en ? "Votes cast" : "Votes exprimés", e => e.Votants),
            new(en ? "Participation (%)" : "Participation (%)", e => e.ParticipationPct),
            new(en ? "Presence (%)" : "Présence (%)", e => e.PresencePct),
            new(en ? "Proxy votes (%)" : "Délégation (%)", e => e.DelegationPct),
            new(en ? "Vote corrections (%)" : "Mise au point (%)", e => e.MiseAuPointPct),
            new(en ? "Absence (%)" : "Absence (%)", e => e.AbsencePct),
            new(en ? "Proxy votes" : "Délégations", e => e.Delegations),
            new(en ? "Vote corrections" : "Mises au point", e => e.MisesAuPoint),
        };
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var fr = langue.Culture;
        DateTime? du, au;
        var aujourdhui = DateTime.UtcNow.Date;
        switch (PeriodeCle)
        {
            case "perso":
                (du, au) = Services.Periode.Lire(Du, Au);
                PeriodeLibelle = langue.Anglais
                    ? $"from {du?.ToString("d MMMM yyyy", fr) ?? "the start"} to {au?.ToString("d MMMM yyyy", fr) ?? "today"}"
                    : $"du {du?.ToString("d MMMM yyyy", fr) ?? "début"} au {au?.ToString("d MMMM yyyy", fr) ?? "aujourd'hui"}";
                break;
            case "annee": du = new DateTime(aujourdhui.Year, 1, 1); au = null; PeriodeLibelle = langue.Anglais ? $"year {aujourdhui.Year}" : $"année {aujourdhui.Year}"; break;
            case "tout": du = null; au = null; PeriodeLibelle = langue.Anglais ? "since the start of the data" : "depuis le début des données"; break;
            default: du = aujourdhui.AddMonths(-12); au = null; PeriodeLibelle = langue.Anglais ? "last 12 months" : "12 derniers mois"; break;
        }

        if (ExportDemande)
        {
            var tout = await stats.ListerElusAsync(ChambreCode, du, au, EnsembleSeul, Recherche, TriEffectif, Decroissant, 1, 0);
            return Exporteur.Ecrire(Format!, langue.Anglais ? "officials-rates" : "taux-des-elus", ColonnesExport(), tout.Lignes);
        }
        Resultats = await stats.ListerElusAsync(ChambreCode, du, au, EnsembleSeul, Recherche, TriEffectif, Decroissant, NumeroPage, ParPage);
        return Page();
    }
}
