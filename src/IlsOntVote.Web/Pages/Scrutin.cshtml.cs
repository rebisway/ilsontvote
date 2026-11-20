using IlsOntVote.Web.Models;
using IlsOntVote.Web.Repositories;
using IlsOntVote.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IlsOntVote.Web.Pages;

/// <summary>
/// Page de détail d'un scrutin : caractéristiques du vote, hémicycle (un point par élu), décompte par groupe,
/// liste des élus et, si on l'a identifié, le texte de loi concerné et son parcours.
/// </summary>
public class ScrutinModel(IScrutinRepository scrutins, ITexteRepository textes, IBillRepository bills, PaysSite site) : PageModel
{
    /// <summary>Le scrutin affiché (jamais null : la page répond 404 avant si l'identifiant est inconnu).</summary>
    public Scrutin Scrutin { get; private set; } = null!;
    /// <summary>Un siège par élu en fonction à la date du scrutin, avec sa position (absents déduits, cf. ScrutinRepository).</summary>
    public IReadOnlyList<Siege> Sieges { get; private set; } = [];
    /// <summary>Sièges placés en hémicycle + arcs de groupes, prêts à être dessinés en SVG.</summary>
    public HemicycleVue Vue { get; private set; } = null!;
    public IReadOnlyList<GroupeDecompte> Groupes { get; private set; } = [];
    /// <summary>Texte (dossier législatif) rattaché au scrutin ; null quand le rattachement est absent ou ambigu.</summary>
    public Texte? Loi { get; private set; }
    /// <summary>Projet de loi britannique auquel la division est rattachée (calculé d'après le titre) ; null ailleurs ou sans rattachement.</summary>
    public BillResume? Bill { get; private set; }
    /// <summary>Scrutin précédent (plus ancien) et suivant (plus récent) de la même chambre ; null aux extrémités.</summary>
    public (Scrutin? Precedent, Scrutin? Suivant) Voisins { get; private set; }

    public async Task<IActionResult> OnGetAsync(string id)
    {
        var s = await scrutins.GetAsync(id);
        if (s is null) return NotFound();

        Scrutin = s;
        Voisins = await scrutins.GetVoisinsAsync(s);
        Sieges = await scrutins.GetSiegesAsync(id);
        Vue = HemicycleLayout.Construire(Sieges, site.Disposition(s.Chambre));   // demi-cercle ou bancs face à face selon la chambre
        Groupes = HemicycleLayout.DecompteParGroupe(Sieges);
        // Le rattachement vote -> texte se fait à l'import, par titre : on n'invente rien ici.
        Loi = await textes.GetParScrutinAsync(id);
        if (s.Chambre == Chambres.Commons) Bill = await bills.GetResumeParDivisionAsync(id);
        return Page();
    }
}
