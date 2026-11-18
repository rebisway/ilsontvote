using IlsOntVote.Web.Repositories;
using IlsOntVote.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IlsOntVote.Web.Pages;

/// <summary>Fiche d'un projet de loi du Parlement britannique : statut publié, étapes dans chaque chambre, divisions des Communes rattachées (par le titre). Site /uk seulement.</summary>
public class BillModel(IBillRepository bills, Langue langue) : PageModel
{
    public Bill Fiche { get; private set; } = null!;
    public IReadOnlyList<EtapeBillVue> Etapes { get; private set; } = [];
    public IReadOnlyList<DivisionBill> Divisions { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(long id)
    {
        if (langue.Pays != "GB") return NotFound();
        var b = await bills.GetAsync(id);
        if (b is null) return NotFound();
        Fiche = b;
        Etapes = await bills.GetEtapesAsync(id);
        Divisions = await bills.GetDivisionsAsync(id);
        return Page();
    }
}
