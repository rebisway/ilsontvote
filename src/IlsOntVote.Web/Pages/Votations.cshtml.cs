using IlsOntVote.Web.Models;
using IlsOntVote.Web.Repositories;
using IlsOntVote.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace IlsOntVote.Web.Pages;

/// <summary>
/// Votations populaires fédérales suisses depuis 2000 (jeu Swissvotes, CC BY 4.0) : liste triable, filtrable et exportable. Résultats AGRÉGÉS (oui, participation, cantons), pas de vote individuel.
/// Page du site suisse seulement (404 ailleurs).
/// </summary>
public class VotationsModel(IVotationRepository votations, Langue langue) : ListePageModel
{
    [BindProperty(SupportsGet = true, Name = "q")] public string? Recherche { get; set; }
    /// <summary>acceptee | rejetee | avenir ; vide = toutes.</summary>
    [BindProperty(SupportsGet = true)] public string? Resultat { get; set; }
    /// <summary>Forme juridique 1 à 5 ; vide = toutes.</summary>
    [BindProperty(SupportsGet = true)] public string? Forme { get; set; }

    public static readonly string[] Resultats_ = ["acceptee", "rejetee", "avenir"];
    public Resultats<Votation> Resultats { get; private set; } = new([], 0, 1, 30);
    public override int TotalLignes => Resultats.Total;

    protected override string TriDefaut => "date";
    protected override IReadOnlyCollection<string> ClesTri { get; } = [.. VotationRepository.Tris.Keys];
    protected override bool DecroissantInitial(string cle) => cle is "date" or "oui" or "participation";
    protected override IEnumerable<(string Nom, string? Valeur)> Filtres => [("q", Recherche), ("resultat", Resultat), ("forme", Forme)];

    /// <summary>Libellé de la forme juridique (textes en base : « votation-forme-1 » à « -5 »).</summary>
    public static string FormeLibelle(int forme, Langue l) => l.T("votation-forme-" + forme);
    /// <summary>Libellé du résultat (acceptée, rejetée, à venir).</summary>
    public static string ResultatLibelle(Votation v, Langue l) => l.T("votation-" + v.ResultatCle);

    private IReadOnlyList<Colonne<Votation>> ColonnesExport() =>
    [
        new(langue.T("date"), v => v.DateVotation),
        new(langue.T("votation-objet"), v => v.Titre(langue.Code)),
        new(langue.T("votation-forme"), v => FormeLibelle(v.Rechtsform, langue)),
        new(langue.T("votation-resultat"), v => ResultatLibelle(v, langue)),
        new(langue.T("votation-oui-pct"), v => v.OuiPct),
        new(langue.T("votation-participation-pct"), v => v.Participation),
        new(langue.T("votation-oui"), v => v.Oui),
        new(langue.T("votation-non"), v => v.Non),
        new(langue.T("votation-cantons-oui"), v => v.CantonsOui),
        new(langue.T("votation-cantons-non"), v => v.CantonsNon),
        new("Swissvotes", v => v.Url),
        new(langue.T("identifiant-67e9"), v => v.Anr),
    ];

    public async Task<IActionResult> OnGetAsync()
    {
        if (langue.Pays != "CH") return NotFound();
        if (Resultat is not null && !Resultats_.Contains(Resultat)) Resultat = null;
        short? forme = short.TryParse(Forme, out var f) && f is >= 1 and <= 5 ? f : null;
        if (forme is null) Forme = null;
        if (ExportDemande)
        {
            var tout = await votations.ListerAsync(Recherche, Resultat, forme, TriEffectif, Decroissant, 1, 0);
            return Exporteur.Ecrire(Format!, "votations", ColonnesExport(), tout.Lignes);
        }
        Resultats = await votations.ListerAsync(Recherche, Resultat, forme, TriEffectif, Decroissant, NumeroPage, ParPage);
        return Page();
    }
}
