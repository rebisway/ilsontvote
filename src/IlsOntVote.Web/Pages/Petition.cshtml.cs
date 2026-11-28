using IlsOntVote.Web.Models;
using IlsOntVote.Web.Services;
using IlsOntVote.Web.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IlsOntVote.Web.Pages;

/// <summary>
/// Fiche d'une pétition du Sénat : sa fiche telle que publiée, l'historique des relevés (soutiens et statut par date) et les constats éventuels
/// (baisse, disparition, réapparition). L'historique commence au premier relevé conservé par le site ; avant, rien n'est connu.
/// </summary>
public class PetitionModel(IPetitionRepository petitions, Langue langue) : PageModel
{
    public Petition? Fiche { get; private set; }
    public IReadOnlyList<RelevePetition> Releves { get; private set; } = [];
    public IReadOnlyList<AnomaliePetition> Anomalies { get; private set; } = [];

    /// <summary>Pétitions voisines (précédente / suivante) pour la navigation.</summary>
    public VoisinsVue Voisins { get; private set; } = new("/petition/", null, null);

    public async Task<IActionResult> OnGetAsync(string id)
    {
        if (langue.Pays is not null && !PaysSite.APetitions(langue.Pays)) return NotFound();
        Fiche = await petitions.GetAsync(id);
        if (Fiche is null) return NotFound();
        Releves = await petitions.GetHistoriqueAsync(id);
        Anomalies = await petitions.GetAnomaliesAsync(id);
        var (prec, suiv) = await petitions.GetVoisinsAsync(Fiche);
        Voisins = new VoisinsVue("/petition/", prec, suiv);
        return Page();
    }
}
