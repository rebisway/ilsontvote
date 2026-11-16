using IlsOntVote.Web.Models;
using IlsOntVote.Web.Repositories;
using IlsOntVote.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IlsOntVote.Web.Pages;

/// <summary>Une carte de l'accueil : un scrutin et son hémicycle, avec les décomptes affichés sous le dessin.</summary>
public sealed record CarteScrutin(Scrutin Scrutin, HemicycleVue Vue)
{
    private IReadOnlyList<Siege> Sieges => Vue.Sieges;
    public int Pour => Sieges.Count(s => s.Position == Pos.Pour);
    public int Contre => Sieges.Count(s => s.Position == Pos.Contre);
    public int Abstention => Sieges.Count(s => s.Position == Pos.Abstention);
    public int Absent => Sieges.Count(s => s.Position == Pos.Absent);
}

/// <summary>Les cartes d'une chambre : l'accueil affiche l'Assemblée à gauche et le Sénat à droite.</summary>
public sealed record SectionChambre(string Chambre, IReadOnlyList<CarteScrutin> Cartes);

/// <summary>Accueil : les derniers textes importants de chaque chambre, chacun avec son hémicycle.</summary>
public class IndexModel(IScrutinRepository scrutins) : PageModel
{
    // 3 cartes par chambre : assez pour donner une image de l'actualité, peu pour garder une page légère
    // (chaque hémicycle pèse plusieurs centaines de points SVG).
    private const int CartesParChambre = 3;

    public IReadOnlyList<SectionChambre> Sections { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var sections = new List<SectionChambre>();
        foreach (var chambre in new[] { Chambres.AN, Chambres.Senat, Chambres.UE })
        {
            var cartes = new List<CarteScrutin>();
            foreach (var s in await scrutins.GetAccueilAsync(chambre, CartesParChambre))
            {
                var sieges = await scrutins.GetSiegesAsync(s.Id);
                cartes.Add(new CarteScrutin(s, HemicycleLayout.Construire(sieges)));
            }
            sections.Add(new SectionChambre(chambre, cartes));
        }
        Sections = sections;
    }
}
