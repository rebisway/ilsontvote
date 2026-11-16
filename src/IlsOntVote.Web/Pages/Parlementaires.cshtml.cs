using IlsOntVote.Web.Models;
using IlsOntVote.Web.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IlsOntVote.Web.Pages;

/// <summary>Un groupe politique et son effectif parmi les élus en exercice (puces de filtre de la page).</summary>
public sealed record GroupeEffectif(string Sigle, string? Libelle, string? Couleur, int Ordre, int Effectif);

/// <summary>Liste des élus en exercice : /deputes ou /senateurs (la chambre se déduit de l'URL).</summary>
public class ParlementairesModel(IParlementaireRepository parlementaires) : PageModel
{
    public string Chambre { get; private set; } = Chambres.AN;
    public IReadOnlyList<Parlementaire> Elus { get; private set; } = [];
    public IReadOnlyList<GroupeEffectif> Groupes { get; private set; } = [];

    public async Task OnGetAsync()
    {
        // Une seule page sert les deux chambres : /deputes et /senateurs sont deux routes vers la même page,
        // la chambre se lit donc dans l'URL (cf. Program.cs, AddPageRoute).
        Chambre = Chambres.DepuisChemin(Request.Path.Value ?? "");

        Elus = await parlementaires.ListerEnExerciceAsync(Chambre);
        // Les élus sans groupe courant sont comptés « NI » (non-inscrits), comme dans l'hémicycle.
        Groupes = Elus
            .GroupBy(e => (Sigle: e.GroupeSigle ?? "NI", e.GroupeLibelle, e.GroupeCouleur, e.GroupeOrdre))
            .Select(g => new GroupeEffectif(g.Key.Sigle, g.Key.GroupeLibelle, g.Key.GroupeCouleur, g.Key.GroupeOrdre, g.Count()))
            .OrderBy(g => g.Ordre).ThenBy(g => g.Sigle)
            .ToList();
    }
}
