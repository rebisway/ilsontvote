using IlsOntVote.Web.Models;
using IlsOntVote.Web.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IlsOntVote.Web.Pages;

/// <summary>Fiche d'un élu : /depute/{slug} ou /senateur/{slug}.</summary>
public class ParlementaireModel(IParlementaireRepository parlementaires) : PageModel
{
    // 40 votes par page dans l'historique d'un élu.
    private const int ParPage = 40;

    [BindProperty(SupportsGet = true, Name = "page")] public int NumeroPage { get; set; } = 1;
    /// <summary>« tout » pour inclure amendements et articles ; sinon votes sur l'ensemble d'un texte.</summary>
    [BindProperty(SupportsGet = true)] public string? Portee { get; set; }

    public Parlementaire Elu { get; private set; } = null!;
    /// <summary>Participation sur les scrutins publics tenus pendant ses mandats (scrutins aux données incomplètes exclus).</summary>
    public BilanParlementaire Bilan { get; private set; } = new();
    public Resultats<LigneVote> Votes { get; private set; } = new([], 0, 1, ParPage);

    public bool ToutAfficher => Portee == "tout";

    public async Task<IActionResult> OnGetAsync(string slug)
    {
        var chambre = Chambres.DepuisChemin(Request.Path.Value ?? "");

        // Le slug n'est unique que par chambre : un sénateur et un député peuvent porter le même nom.
        var elu = await parlementaires.GetParSlugAsync(chambre, slug);
        if (elu is null) return NotFound();

        Elu = elu;
        Bilan = await parlementaires.GetBilanAsync(elu.Id, chambre);
        Votes = await parlementaires.GetVotesAsync(elu.Id, chambre, !ToutAfficher, NumeroPage, ParPage);
        return Page();
    }

    public string LienPage(int n)
    {
        var q = new List<string>();
        if (ToutAfficher) q.Add("portee=tout");
        if (n > 1) q.Add("page=" + n);
        return Elu.Lien + (q.Count > 0 ? "?" + string.Join("&", q) : "");
    }
}
