using IlsOntVote.Web.Repositories;
using IlsOntVote.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IlsOntVote.Web.Pages;

/// <summary>Fiche d'une votation populaire fédérale suisse (résultats agrégés, jeu Swissvotes). Site suisse seulement (404 ailleurs).</summary>
public class VotationModel(IVotationRepository votations, Langue langue) : PageModel
{
    public Votation V { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(int anr)
    {
        if (langue.Pays != "CH") return NotFound();
        var v = await votations.GetAsync(anr);
        if (v is null) return NotFound();
        V = v;
        return Page();
    }
}
