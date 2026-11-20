using IlsOntVote.Web.Services;
using IlsOntVote.Web.Models;
using IlsOntVote.Web.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IlsOntVote.Web.Pages;

/// <summary>Fiche d'une loi : parcours (votes, Conseil constitutionnel, promulgation, Journal officiel).</summary>
public class TexteModel(ITexteRepository textes, Langue langue) : PageModel
{
    /// <summary>Le texte (dossier législatif) : statut, promulgation, parution au JO, Conseil constitutionnel.</summary>
    public Texte Loi { get; private set; } = null!;
    /// <summary>Lois voisines (précédente / suivante dans l'ordre de la liste /lois) pour la navigation.</summary>
    public VoisinsVue Voisins { get; private set; } = new("/texte/", null, null);
    /// <summary>Tous les votes sur l'ensemble rattachés à ce texte (Assemblée, Sénat, CMP, nouvelles lectures…), du plus ancien au plus récent.</summary>
    public IReadOnlyList<ScrutinDuTexte> Scrutins { get; private set; } = [];
    /// <summary>Décisions de séance à l'Assemblée sur ce texte, y compris celles sans scrutin public (votes à main levée).</summary>
    public IReadOnlyList<EtapeTexte> Etapes { get; private set; } = [];

    /// <summary>Nombre de scrutins liés (amendements, articles…) et les plus récents.</summary>
    public int NbLies { get; private set; }
    public IReadOnlyList<ScrutinDuTexte> Lies { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(string id)
    {
        // Les lois sont celles du Parlement français : jamais affichées sous le site d'un autre pays (étanchéité entre pays).
        if (langue.Pays is not null) return NotFound();
        var loi = await textes.GetAsync(id);
        if (loi is null) return NotFound();

        Loi = loi;
        var (prec, suiv) = await textes.GetVoisinsAsync(id);
        Voisins = new VoisinsVue("/texte/", prec, suiv);
        Scrutins = await textes.GetScrutinsAsync(id);
        Etapes = await textes.GetEtapesAsync(id);
        (NbLies, Lies) = await textes.GetScrutinsLiesAsync(id, 10);
        return Page();
    }
}
