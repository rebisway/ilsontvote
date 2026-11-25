using IlsOntVote.Web.Repositories;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IlsOntVote.Web.Pages.Admin;

/// <summary>Statistiques de fréquentation. Les « visiteurs uniques » comptent des empreintes d'IP irréversibles (cf. IpHasher), jamais des adresses.</summary>
public class StatsModel(IPageViewRepository vues) : PageModel
{
    public string Periode { get; private set; } = "jour";
    public string Source { get; private set; } = "humains";
    public ApercuStats Apercu { get; private set; } = new();
    public IReadOnlyList<LigneSource> Recap { get; private set; } = [];

    public async Task OnGetAsync(string? periode, string? source)
    {
        // Les paramètres d'URL sont validés contre des listes blanches : jamais de valeur libre vers le SQL.
        Periode = periode is "jour" or "hier" or "mois" or "annee" or "tout" ? periode : "jour";   // par défaut : aujourd'hui
        Source = source is "humains" or "bots" or "cloud" or "bots-cloud" or "tous" ? source : "humains";

        // Les dates de la base sont en UTC (colonne `visited_at` sans fuseau, remplie en UTC).
        var maintenant = DateTime.UtcNow;
        DateTime depuis;
        DateTime? jusqua = null;
        Granularite g;
        switch (Periode)
        {
            case "jour": depuis = maintenant.Date; g = Granularite.Jour; break;
            case "hier": depuis = maintenant.Date.AddDays(-1); jusqua = maintenant.Date; g = Granularite.Jour; break;
            case "annee": depuis = new DateTime(maintenant.Year, 1, 1); g = Granularite.Mois; break;
            case "tout": depuis = DateTime.MinValue; g = Granularite.Mois; break;
            default: depuis = new DateTime(maintenant.Year, maintenant.Month, 1); g = Granularite.Jour; break;
        }

        var src = Source switch { "bots" => SourceVues.Bots, "cloud" => SourceVues.Cloud, "bots-cloud" => SourceVues.BotsCloud, "tous" => SourceVues.Tous, _ => SourceVues.Humains };
        Recap = await vues.GetRecapAsync(depuis, jusqua);
        Apercu = await vues.GetStatsAsync(depuis, jusqua, g, src);
    }

    public string Lien(string? periode = null, string? source = null) =>
        $"/Admin/Stats?periode={periode ?? Periode}&source={source ?? Source}";
}
