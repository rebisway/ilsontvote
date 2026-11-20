using IlsOntVote.Web.Models;
using IlsOntVote.Web.Repositories;
using IlsOntVote.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace IlsOntVote.Web.Pages;

/// <summary>
/// Projets de loi du Parlement britannique (équivalent de /lois pour le Royaume-Uni) : liste triable, filtrable et exportable. Statuts = indicateurs publiés par la source
/// (loi, retiré, rejeté, en cours) ; le nombre de divisions rattachées est calculé par le site d'après les titres. Page du site britannique seulement (404 ailleurs).
/// </summary>
public class BillsModel(IBillRepository bills, Langue langue, Themes themes) : ListePageModel
{
    /// <summary>Code de thème (approximatif, mots-clés du titre) : ne garde que les éléments de ce thème.</summary>
    [BindProperty(SupportsGet = true)] public string? Theme { get; set; }
    /// <summary>Thèmes proposés par le filtre.</summary>
    public IReadOnlyList<Theme> ThemesListe => themes.Lister();
    [BindProperty(SupportsGet = true, Name = "q")] public string? Recherche { get; set; }
    /// <summary>loi | en-cours | retire (retiré ou rejeté) ; vide = tous.</summary>
    [BindProperty(SupportsGet = true)] public string? Statut { get; set; }
    /// <summary>Chambre où se trouve le projet : Commons | Lords ; vide = les deux.</summary>
    [BindProperty(SupportsGet = true)] public string? Chambre { get; set; }

    public static readonly string[] Statuts = ["loi", "en-cours", "retire"];
    public static readonly string[] Chambres = ["Commons", "Lords"];

    public Resultats<Bill> Resultats { get; private set; } = new([], 0, 1, 30);

    public override int TotalLignes => Resultats.Total;
    public IReadOnlyList<(string Cle, int Nombre)> ParStatut { get; private set; } = [];

    protected override string TriDefaut => "maj";
    protected override IReadOnlyCollection<string> ClesTri { get; } = [.. BillRepository.Tris.Keys];
    protected override bool DecroissantInitial(string cle) => cle is "depot" or "maj" or "divisions";
    protected override IEnumerable<(string Nom, string? Valeur)> Filtres => [("q", Recherche), ("statut", Statut), ("chambre", Chambre), ("theme", themes.Valider(Theme))];

    /// <summary>
    /// Statut d'un projet dans la langue de la page. Les libellés de la source (étape en cours : « Committee stage ») restent en anglais : ce sont des DONNÉES officielles, comme
    /// les titres ; seuls les libellés que le site ajoute (loi, rejeté, retiré, en cours) se traduisent.
    /// </summary>
    public static string StatutLibelle(Bill b, Langue l) => b.StatutCle switch
    {
        "loi" => l.T("loi-a3bb", "Loi", "Act") + (b.DateLoi is { } d ? l.T("sanction-royale-le", " · sanction royale le ", " · Royal Assent ") + d.ToString("d MMM yyyy", l.Culture) : ""),
        "rejete" => l.T("rejete-cf6a", "Rejeté", "Defeated"),
        "retire" => l.T("retire", "Retiré", "Withdrawn"),
        _ => b.EtapeCourante ?? l.T("en-cours-34e5", "En cours", "In progress"),
    };

    /// <summary>
    /// Libellé abrégé pour la puce d'un tableau : « Consideration of Commons amendments and / or reasons » devient « Commons amendments », « Committee stage » « Committee ».
    /// L'infobulle de la puce garde le libellé complet de la source.
    /// </summary>
    public static string StatutCourt(Bill b, Langue l)
    {
        var s = StatutLibelle(b, l);
        if (b.StatutCle != "en-cours") return s.Replace("Royal Assent ", "").Replace("sanction royale le ", "");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"^Consideration of ", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+and\s*/\s*or reasons$| stage$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return s;
    }

    /// <summary>Classe de teinte (puce de statut) : mêmes teintes que les pétitions, distinctes des couleurs de vote.</summary>
    public static string StatutClasse(Bill b) => b.StatutCle switch { "loi" => "st-ouverte", "rejete" => "st-archivee", "retire" => "st-neutre", _ => "st-seuil" };

    private IReadOnlyList<Colonne<Bill>> ColonnesExport() =>
    [
        new(langue.T("projet-de-loi", "Projet de loi", "Bill"), b => b.Titre),
        new(langue.T("themes-estimes-par-mots-cles-du", "Thèmes (estimés par mots-clés du titre)", "Topics (estimated from the title)"), b => string.Join(", ", b.Themes)),
        new(langue.T("statut", "Statut", "Status"), b => b.StatutCle),
        new(langue.T("etape-en-cours", "Étape en cours", "Current stage"), b => b.EtapeCourante),
        new(langue.T("chambre-d-origine", "Chambre d'origine", "Originating house"), b => b.ChambreOrigine),
        new(langue.T("chambre-actuelle", "Chambre actuelle", "Current house"), b => b.ChambreCourante),
        new(langue.T("premiere-seance", "Première séance", "First sitting"), b => b.DateDepot),
        new(langue.T("sanction-royale", "Sanction royale", "Royal Assent"), b => b.DateLoi),
        new(langue.T("retire-le", "Retiré le", "Withdrawn on"), b => b.RetireLe),
        new(langue.T("divisions-rattachees-calcule", "Divisions rattachées (calculé)", "Divisions linked (computed)"), b => b.NbDivisions),
        new(langue.T("derniere-mise-a-jour", "Dernière mise à jour", "Last update"), b => b.DerniereMaj),
        new("URL", b => b.Url),
        new(langue.T("identifiant-67e9", "Identifiant", "Identifier"), b => b.Id),
    ];

    public async Task<IActionResult> OnGetAsync()
    {
        // Projets de loi du Parlement britannique : site /uk seulement.
        if (langue.Pays != "GB") return NotFound();
        if (Statut is not null && !Statuts.Contains(Statut)) Statut = null;
        if (Chambre is not null && !Chambres.Contains(Chambre)) Chambre = null;
        ParStatut = await bills.CompterParStatutAsync();
        if (ExportDemande)
        {
            var tout = await bills.ListerAsync(Recherche, Statut, Chambre, TriEffectif, Decroissant, 1, 0, themes.Valider(Theme));
            return Exporteur.Ecrire(Format!, "bills", ColonnesExport(), tout.Lignes);
        }
        Resultats = await bills.ListerAsync(Recherche, Statut, Chambre, TriEffectif, Decroissant, NumeroPage, ParPage, themes.Valider(Theme));
        return Page();
    }
}
