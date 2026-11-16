using IlsOntVote.Web.Models;
using IlsOntVote.Web.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IlsOntVote.Web.Pages.Admin;

/// <summary>Choix éditorial des textes « à la une » de l'accueil (jamais écrasé par l'import).</summary>
public class UnesModel(IAdminRepository admin, IScrutinRepository scrutins) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "q")] public string? Recherche { get; set; }

    public IReadOnlyList<ScrutinUne> Unes { get; private set; } = [];
    public IReadOnlyList<Scrutin> Candidats { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Unes = await admin.GetUnesAsync();
        if (!string.IsNullOrWhiteSpace(Recherche))
            Candidats = (await scrutins.ListerAsync(null, Recherche, surEnsembleSeulement: true, page: 1, parPage: 15)).Lignes;
    }

    /// <summary>Met un scrutin à la une (ou met à jour son rang et son titre court). Les champs « à la une » ne sont jamais écrasés par l'import.</summary>
    public async Task<IActionResult> OnPostAjouterAsync(string id, int? rang, string? titreCourt, string? q)
    {
        await admin.DefinirUneAsync(id, rang, titreCourt);
        return RedirectToPage(new { q });
    }

    /// <summary>Retire un scrutin de la une : l'accueil retombe sur la sélection automatique pour cette chambre s'il n'en reste aucun.</summary>
    public async Task<IActionResult> OnPostRetirerAsync(string id)
    {
        await admin.RetirerUneAsync(id);
        return RedirectToPage();
    }
}
