using IlsOntVote.Web.Models;
using IlsOntVote.Web.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IlsOntVote.Web.Pages;

/// <summary>Fiche d'une loi : parcours (votes, Conseil constitutionnel, promulgation, Journal officiel).</summary>
public class TexteModel(ITexteRepository textes) : PageModel
{
    /// <summary>Le texte (dossier législatif) : statut, promulgation, parution au JO, Conseil constitutionnel.</summary>
    public Texte Loi { get; private set; } = null!;
    /// <summary>Tous les votes sur l'ensemble rattachés à ce texte (Assemblée, Sénat, CMP, nouvelles lectures…), du plus ancien au plus récent.</summary>
    public IReadOnlyList<ScrutinDuTexte> Scrutins { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(string id)
    {
        var loi = await textes.GetAsync(id);
        if (loi is null) return NotFound();

        Loi = loi;
        Scrutins = await textes.GetScrutinsAsync(id);
        return Page();
    }
}
