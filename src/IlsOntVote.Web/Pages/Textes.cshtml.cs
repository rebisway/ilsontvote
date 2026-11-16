using IlsOntVote.Web.Models;
using IlsOntVote.Web.Repositories;
using IlsOntVote.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace IlsOntVote.Web.Pages;

/// <summary>Liste des scrutins publics (tous les votes), triable et exportable.</summary>
public class TextesModel(IScrutinRepository scrutins) : ListePageModel
{
    // 30 lignes : un tableau lisible d'un coup d'œil ; au-delà on pagine (l'export, lui, n'est pas paginé).
    protected override int ParPageDefaut => 30;

    /// <summary>an | senat | (vide = les deux)</summary>
    [BindProperty(SupportsGet = true)] public string? Chambre { get; set; }
    [BindProperty(SupportsGet = true, Name = "q")] public string? Recherche { get; set; }
    /// <summary>« tout » pour inclure les amendements et articles ; sinon seulement les votes sur l'ensemble d'un texte.</summary>
    [BindProperty(SupportsGet = true)] public string? Portee { get; set; }

    /// <summary>Période (yyyy-MM-dd) : « du » et « au », bornes incluses ; la même date des deux côtés = ce jour-là.</summary>
    [BindProperty(SupportsGet = true)] public string? Du { get; set; }
    [BindProperty(SupportsGet = true)] public string? Au { get; set; }

    public Resultats<Scrutin> Resultats { get; private set; } = new([], 0, 1, 30);

    public bool ToutAfficher => Portee == "tout";

    protected override string TriDefaut => "date";
    protected override IReadOnlyCollection<string> ClesTri { get; } = [.. ScrutinRepository.ClesTri];
    // Dates et nombres : le premier clic trie du plus grand au plus petit.
    protected override bool DecroissantInitial(string cle) => cle is "date" or "votants" or "pour" or "contre" or "abst";

    protected override IEnumerable<(string Nom, string? Valeur)> Filtres =>
        [("q", Recherche), ("chambre", Chambre), ("portee", ToutAfficher ? "tout" : null), ("du", Du), ("au", Au)];

    private static readonly IReadOnlyList<Colonne<Scrutin>> ColonnesExport =
    [
        new("Date", s => s.DateScrutin),
        new("Chambre", s => Chambres.Nom(s.Chambre)),
        new("Numéro", s => s.Numero),
        new("Titre", s => s.TitreAffiche),
        new("Type de vote", s => s.TypeVoteLibelle),
        new("Résultat", s => s.Adopte ? "adopté" : "rejeté"),
        new("Votants", s => s.NbVotants),
        new("Suffrages exprimés", s => s.SuffragesExprimes),
        new("Majorité requise", s => s.SuffragesRequis),
        new("Pour", s => s.NbPour),
        new("Contre", s => s.NbContre),
        new("Abstentions", s => s.NbAbstentions),
        new("Données incomplètes", s => s.NominatifIncomplet),
        new("Identifiant", s => s.Id),
    ];

    public async Task<IActionResult> OnGetAsync()
    {
        var chambre = Chambre switch { "an" => Chambres.AN, "senat" => Chambres.Senat, "ue" => Chambres.UE, _ => null };

        var (du, au) = Periode.Lire(Du, Au);

        if (ExportDemande)
        {
            var tout = await scrutins.ListerAsync(chambre, Recherche, !ToutAfficher, 1, 0, TriEffectif, Decroissant, du, au);
            return Exporteur.Ecrire(Format!, "scrutins", ColonnesExport, tout.Lignes);
        }

        Resultats = await scrutins.ListerAsync(chambre, Recherche, !ToutAfficher, NumeroPage, ParPage, TriEffectif, Decroissant, du, au);
        return Page();
    }
}
