using IlsOntVote.Web.Repositories;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IlsOntVote.Web.Pages.Admin;

/// <summary>Tableau de bord de l'administration : volumes de données, fréquentation de la semaine, dernier import de chaque source.</summary>
public class IndexModel(IAdminRepository admin, IPageViewRepository vues) : PageModel
{
    public ChiffresAdmin Chiffres { get; private set; } = new();
    public ApercuStats Semaine { get; private set; } = new();
    public IReadOnlyList<ExecutionImport> DerniersImports { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Chiffres = await admin.GetChiffresAsync();
        Semaine = await vues.GetStatsAsync(DateTime.UtcNow.Date.AddDays(-6), null, Granularite.Jour, SourceVues.Humains);

        // Dernière exécution de chaque source : on lit les 200 dernières lignes du journal puis on garde la plus récente
        // par source (plus simple et assez rapide pour quelques sources ; à revoir s'il y en a des dizaines).
        DerniersImports = (await admin.GetImportsAsync(200))
            .GroupBy(i => i.Source).Select(g => g.First()).OrderBy(i => i.Source).ToList();
    }
}
