using IlsOntVote.Web.Models;
using IlsOntVote.Web.Repositories;
using IlsOntVote.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace IlsOntVote.Web.Pages;

/// <summary>Lois (dossiers législatifs) ayant fait l'objet d'un vote sur l'ensemble : parcours et statut.</summary>
public class LoisModel(ITexteRepository textes, Langue langue, Themes themes) : ListePageModel
{
    protected override int ParPageDefaut => 30;

    [BindProperty(SupportsGet = true, Name = "q")] public string? Recherche { get; set; }
    [BindProperty(SupportsGet = true)] public string? Statut { get; set; }
    [BindProperty(SupportsGet = true)] public string? Chambre { get; set; }
    /// <summary>Code de thème (approximatif, mots-clés du titre) : ne garde que les lois de ce thème.</summary>
    [BindProperty(SupportsGet = true)] public string? Theme { get; set; }
    public IReadOnlyList<Theme> ThemesListe => themes.Lister();
    /// <summary>Période (yyyy-MM-dd) : textes dont une date (vote, promulgation, JO, Conseil constitutionnel) est dans l'intervalle.</summary>
    [BindProperty(SupportsGet = true)] public string? Du { get; set; }
    [BindProperty(SupportsGet = true)] public string? Au { get; set; }

    /// <summary>
    /// Filtres de statut proposés. La clé est la valeur d'URL ; son sens exact (le SQL) est dans
    /// <c>TexteRepository.FiltreStatut</c>. « Adoptés par les deux assemblées, non promulgués » est la
    /// formulation neutre de « voté mais pas (encore) promulgué » : on ne présume pas de la cause.
    /// </summary>
    public static readonly (string Cle, string Libelle, string Anglais)[] Statuts =
    [
        ("", "Tous les statuts", "All statuses"),
        ("promulgue", "Promulgués", "Promulgated"),
        ("deux-assemblees", "Adoptés par les deux assemblées, non promulgués", "Adopted by both chambers, not promulgated"),
        ("non-promulgue", "Adoptés par une assemblée, non promulgués", "Adopted by one chamber, not promulgated"),
        ("discussion", "En discussion", "In progress"),
        ("rejete", "Rejetés, caducs ou retirés", "Rejected, lapsed or withdrawn"),
        ("cc", "Passés devant le Conseil constitutionnel", "Referred to the Constitutional Council"),
        ("censure", "Censurés (en tout ou partie)", "Struck down (in whole or in part)"),
        ("49-3", "Adoptés par 49.3 (sans vote)", "Adopted under 49.3 (without a vote)"),
    ];

    public Resultats<Texte> Resultats { get; private set; } = new([], 0, 1, 30);

    public override int TotalLignes => Resultats.Total;

    protected override string TriDefaut => "vote";
    protected override IReadOnlyCollection<string> ClesTri { get; } = [.. TexteRepository.ClesTri];
    // Dates et compteurs : le premier clic doit montrer le plus récent / le plus grand d'abord.
    protected override bool DecroissantInitial(string cle) => cle is "vote" or "promulgation" or "jo" or "scrutins" or "amendements";

    protected override IEnumerable<(string Nom, string? Valeur)> Filtres =>
        [("q", Recherche), ("statut", Statut), ("chambre", Chambre), ("du", Du), ("au", Au), ("theme", themes.Valider(Theme))];

    // Colonnes de l'export CSV/JSON. Plus complètes que le tableau affiché : un export sert à analyser,
    // il garde donc les deux chambres, les liens et la décision du Conseil constitutionnel.
    private static readonly IReadOnlyList<Colonne<Texte>> ColonnesExport =
    [
        new("Dernier vote", t => t.DernierVote),
        new("Titre", t => t.TitreAffiche),
        new("Type", t => t.TypeLibelle),
        new("Statut", t => t.Statut.Libelle),
        new("Thèmes (estimés par mots-clés du titre)", t => string.Join(", ", t.Themes)),
        new("Scrutins", t => t.NbScrutins),
        new("Amendements déposés (officiel)", t => t.NbAmendements),
        new("Amendements adoptés (officiel)", t => t.NbAmendementsAdoptes),
        new("Amendements rejetés (officiel)", t => t.NbAmendementsRejetes),
        new("Amendements au scrutin public (calculé)", t => t.NbAmendementsScrutins),
        new("Adopté par l'Assemblée", t => t.AdopteAn),
        new("Adopté par le Sénat", t => t.AdopteSenat),
        new("Au moins une lecture adoptée par l'article 49-3 (sans vote)", t => t.Adopte493),
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
        // Les lois sont celles du Parlement français : jamais affichées sous le site d'un autre pays (étanchéité entre pays).
        if (langue.Pays is not null) return NotFound();
        var (du, au) = Periode.Lire(Du, Au);
        var filtre = new FiltreTextes(Recherche, Statut, Chambre, du, au, themes.Valider(Theme));

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
