using IlsOntVote.Web.Models;
using IlsOntVote.Web.Repositories;
using IlsOntVote.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace IlsOntVote.Web.Pages;

/// <summary>Lois (dossiers législatifs) ayant fait l'objet d'un vote sur l'ensemble : parcours et statut.</summary>
public class LoisModel(ITexteRepository textes) : ListePageModel
{
    private const int ParPage = 30;

    [BindProperty(SupportsGet = true, Name = "q")] public string? Recherche { get; set; }
    [BindProperty(SupportsGet = true)] public string? Statut { get; set; }
    [BindProperty(SupportsGet = true)] public string? Chambre { get; set; }

    /// <summary>
    /// Filtres de statut proposés. La clé est la valeur d'URL ; son sens exact (le SQL) est dans
    /// <c>TexteRepository.FiltreStatut</c>. « Adoptés par les deux assemblées, non promulgués » est la
    /// formulation neutre de « voté mais pas (encore) promulgué » : on ne présume pas de la cause.
    /// </summary>
    public static readonly (string Cle, string Libelle)[] Statuts =
    [
        ("", "Tous les statuts"),
        ("promulgue", "Promulgués"),
        ("deux-assemblees", "Adoptés par les deux assemblées, non promulgués"),
        ("non-promulgue", "Adoptés par une assemblée, non promulgués"),
        ("discussion", "En discussion"),
        ("rejete", "Rejetés, caducs ou retirés"),
        ("cc", "Passés devant le Conseil constitutionnel"),
        ("censure", "Censurés (en tout ou partie)"),
    ];

    public Resultats<Texte> Resultats { get; private set; } = new([], 0, 1, ParPage);

    protected override string TriDefaut => "vote";
    protected override IReadOnlyCollection<string> ClesTri { get; } = [.. TexteRepository.ClesTri];
    // Dates et compteurs : le premier clic doit montrer le plus récent / le plus grand d'abord.
    protected override bool DecroissantInitial(string cle) => cle is "vote" or "promulgation" or "jo" or "scrutins";

    protected override IEnumerable<(string Nom, string? Valeur)> Filtres =>
        [("q", Recherche), ("statut", Statut), ("chambre", Chambre)];

    // Colonnes de l'export CSV/JSON. Plus complètes que le tableau affiché : un export sert à analyser,
    // il garde donc les deux chambres, les liens et la décision du Conseil constitutionnel.
    private static readonly IReadOnlyList<Colonne<Texte>> ColonnesExport =
    [
        new("Dernier vote", t => t.DernierVote),
        new("Titre", t => t.TitreAffiche),
        new("Type", t => t.TypeLibelle),
        new("Statut", t => t.Statut.Libelle),
        new("Scrutins", t => t.NbScrutins),
        new("Adopté par l'Assemblée", t => t.AdopteAn),
        new("Adopté par le Sénat", t => t.AdopteSenat),
        new("Numéro de loi", t => t.NumeroLoi),
        new("Date de promulgation", t => t.DatePromulgation),
        new("Date de parution au JO", t => t.DateJo),
        new("Lien Légifrance", t => t.UrlLegifrance),
        new("Décision du Conseil constitutionnel", t => t.DecisionCcLibelle),
        new("Numéro de décision", t => t.DecisionCcNumero),
        new("Date de décision", t => t.DecisionCcDate),
        new("Lien du dossier (Sénat)", t => t.UrlDossierSenat),
        new("Lien du dossier (Assemblée)", t => t.UrlDossierAn),
    ];

    public async Task<IActionResult> OnGetAsync()
    {
        var filtre = new FiltreTextes(Recherche, Statut, Chambre);

        // Export : mêmes filtres et même tri que l'écran, mais SANS pagination (parPage = 0 => tout, plafonné côté SQL).
        if (ExportDemande)
        {
            var tout = await textes.ListerAsync(filtre, TriEffectif, Decroissant, 1, 0);
            return Exporteur.Ecrire(Format!, "lois", ColonnesExport, tout.Lignes);
        }

        Resultats = await textes.ListerAsync(filtre, TriEffectif, Decroissant, NumeroPage, ParPage);
        return Page();
    }
}
