using IlsOntVote.Web.Repositories;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IlsOntVote.Web.Pages.Admin;

/// <summary>Journal des imports : une ligne par exécution d'une source (cron ou manuelle), avec durée, compteurs et message d'erreur.</summary>
public class ImportsModel(IAdminRepository admin) : PageModel
{
    public IReadOnlyList<ExecutionImport> Executions { get; private set; } = [];

    public async Task OnGetAsync() => Executions = await admin.GetImportsAsync(100);
}
