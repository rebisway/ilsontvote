using IlsOntVote.Web.Models;
using IlsOntVote.Web.Repositories;
using IlsOntVote.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace IlsOntVote.Web.Pages;

/// <summary>Liste des scrutins publics (tous les votes), triable et exportable.</summary>
public class TextesModel(IScrutinRepository scrutins, Langue langue) : ListePageModel
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

    /// <summary>Identifiant d'un texte : ne garde que les votes de ce texte (vote sur l'ensemble, amendements, articles).</summary>
    [BindProperty(SupportsGet = true, Name = "texte")] public string? TexteId { get; set; }

    /// <summary>« resolution » : ne garde que les votes sur des propositions de résolution (menu « Résolutions »).</summary>
    [BindProperty(SupportsGet = true)] public string? Nature { get; set; }
    public bool Resolutions => Nature == "resolution";
    /// <summary>Vrai sur le site d'un pays hors France : la liste de chambres de la France est masquée.</summary>
    public bool SitePays => langue.Pays is not null;

    public Resultats<Scrutin> Resultats { get; private set; } = new([], 0, 1, 30);

    /// <summary>Toutes les divisions sur le site d'un pays (aucune n'est rattachée à un texte « sur l'ensemble » : un filtre vide la liste) ; sinon selon la case « amendements et articles ».</summary>
    public bool ToutAfficher => Portee == "tout" || SitePays;

    protected override string TriDefaut => "date";
    protected override IReadOnlyCollection<string> ClesTri { get; } = [.. ScrutinRepository.ClesTri];
    // Dates et nombres : le premier clic trie du plus grand au plus petit.
    protected override bool DecroissantInitial(string cle) => cle is "date" or "votants" or "pour" or "contre" or "abst";

    protected override IEnumerable<(string Nom, string? Valeur)> Filtres =>
        [("q", Recherche), ("chambre", Chambre), ("portee", ToutAfficher ? "tout" : null), ("du", Du), ("au", Au), ("texte", TexteId), ("nature", Resolutions ? "resolution" : null)];

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
        // Site d'un pays (/uk) : sa seule chambre (Communes) ; le paramètre « chambre » de la France n'a pas de sens. Les chambres des autres pays sont de toute façon
        // écartées par ScrutinRepository (chambres visibles du site).
        var chambre = langue.Pays == "GB" ? Chambres.Commons
            : Chambre switch { "an" => Chambres.AN, "senat" => Chambres.Senat, "ue" => Chambres.UE, _ => null };

        var (du, au) = Periode.Lire(Du, Au);

        if (ExportDemande)
        {
            var tout = await scrutins.ListerAsync(chambre, Recherche, !ToutAfficher, 1, 0, TriEffectif, Decroissant, du, au, TexteId, Resolutions);
            return Exporteur.Ecrire(Format!, "scrutins", ColonnesExport, tout.Lignes);
        }

        Resultats = await scrutins.ListerAsync(chambre, Recherche, !ToutAfficher, NumeroPage, ParPage, TriEffectif, Decroissant, du, au, TexteId, Resolutions);
        return Page();
    }
}
