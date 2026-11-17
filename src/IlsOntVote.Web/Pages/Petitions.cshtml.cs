using IlsOntVote.Web.Models;
using IlsOntVote.Web.Repositories;
using IlsOntVote.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace IlsOntVote.Web.Pages;

/// <summary>
/// Pétitions déposées sur petitions.senat.fr (jeu « Pétitions du Sénat », data.gouv.fr). Liste triable, filtrable et exportable.
/// Les statuts et nombres de soutiens sont ceux de la source ; le site n'en tire aucune conclusion. Rappel affiché : ce sont des soutiens
/// de citoyens, pas des votes d'élus.
/// </summary>
public class PetitionsModel(IPetitionRepository petitions, Langue langue) : ListePageModel
{
    [BindProperty(SupportsGet = true, Name = "q")] public string? Recherche { get; set; }
    [BindProperty(SupportsGet = true)] public string? Statut { get; set; }
    [BindProperty(SupportsGet = true)] public string? Type { get; set; }

    public Resultats<Petition> Resultats { get; private set; } = new([], 0, 1, 30);
    public IReadOnlyList<EffectifPetition> ParStatut { get; private set; } = [];
    public IReadOnlyList<EffectifPetition> ParType { get; private set; } = [];

    protected override string TriDefaut => "date";
    protected override IReadOnlyCollection<string> ClesTri { get; } = [.. PetitionRepository.Tris.Keys];
    // Date et soutiens : le premier clic trie du plus grand au plus petit ; titre, statut, type : alphabétique.
    protected override bool DecroissantInitial(string cle) => cle is "date" or "soutiens";
    protected override IEnumerable<(string Nom, string? Valeur)> Filtres => [("q", Recherche), ("statut", Statut), ("type", Type)];

    /// <summary>Libellé du statut dans la langue de la page (les valeurs publiées par la source sont en français).</summary>
    public static string StatutLibelle(string statut, bool anglais) => !anglais ? statut : statut switch
    {
        "Seuil non atteint" => "Threshold not reached",
        "Seuil atteint" => "Threshold reached",
        "Ouverte" => "Open",
        _ => statut,
    };

    /// <summary>Libellé du type dans la langue de la page.</summary>
    public static string TypeLibelle(string type, bool anglais) => !anglais ? type : type switch
    {
        "Texte législatif" => "Legislative text",
        "Mission de contrôle" => "Oversight mission",
        _ => type,
    };

    private IReadOnlyList<Colonne<Petition>> ColonnesExport()
    {
        var en = langue.Anglais;
        return new List<Colonne<Petition>>
        {
            new(en ? "Published" : "Date de publication", p => p.DatePublication),
            new(en ? "Title" : "Titre", p => p.Titre),
            new("Type", p => p.Type),
            new(en ? "Status" : "Statut", p => p.Statut),
            new(en ? "Supporters" : "Soutiens", p => p.NbVotes),
            new(en ? "Voting deadline" : "Date limite", p => p.DateLimiteVote),
            new("URL", p => p.Url),
        };
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (ExportDemande)
        {
            var tout = await petitions.ListerAsync(Recherche, Statut, Type, TriEffectif, Decroissant, 1, 0);
            return Exporteur.Ecrire(Format!, langue.Anglais ? "senate-petitions" : "petitions-senat", ColonnesExport(), tout.Lignes);
        }
        Resultats = await petitions.ListerAsync(Recherche, Statut, Type, TriEffectif, Decroissant, NumeroPage, ParPage);
        ParStatut = await petitions.CompterParStatutAsync();
        ParType = await petitions.CompterParTypeAsync();
        return Page();
    }
}
