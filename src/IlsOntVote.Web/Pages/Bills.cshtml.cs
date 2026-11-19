using IlsOntVote.Web.Models;
using IlsOntVote.Web.Repositories;
using IlsOntVote.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace IlsOntVote.Web.Pages;

/// <summary>
/// Projets de loi du Parlement britannique (équivalent de /lois pour le Royaume-Uni) : liste triable, filtrable et exportable. Statuts = indicateurs publiés par la source
/// (loi, retiré, rejeté, en cours) ; le nombre de divisions rattachées est calculé par le site d'après les titres. Page du site britannique seulement (404 ailleurs).
/// </summary>
public class BillsModel(IBillRepository bills, Langue langue) : ListePageModel
{
    [BindProperty(SupportsGet = true, Name = "q")] public string? Recherche { get; set; }
    /// <summary>loi | en-cours | retire (retiré ou rejeté) ; vide = tous.</summary>
    [BindProperty(SupportsGet = true)] public string? Statut { get; set; }
    /// <summary>Chambre où se trouve le projet : Commons | Lords ; vide = les deux.</summary>
    [BindProperty(SupportsGet = true)] public string? Chambre { get; set; }

    public static readonly string[] Statuts = ["loi", "en-cours", "retire"];
    public static readonly string[] Chambres = ["Commons", "Lords"];

    public Resultats<Bill> Resultats { get; private set; } = new([], 0, 1, 30);
    public IReadOnlyList<(string Cle, int Nombre)> ParStatut { get; private set; } = [];

    protected override string TriDefaut => "maj";
    protected override IReadOnlyCollection<string> ClesTri { get; } = [.. BillRepository.Tris.Keys];
    protected override bool DecroissantInitial(string cle) => cle is "depot" or "maj" or "divisions";
    protected override IEnumerable<(string Nom, string? Valeur)> Filtres => [("q", Recherche), ("statut", Statut), ("chambre", Chambre)];

    public static string StatutLibelle(Bill b) => b.StatutCle switch
    {
        "loi" => "Act" + (b.DateLoi is { } d ? " · Royal Assent " + d.ToString("d MMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("en-GB")) : ""),
        "rejete" => "Defeated",
        "retire" => "Withdrawn",
        _ => b.EtapeCourante ?? "In progress",
    };

    /// <summary>
    /// Libellé abrégé pour la puce d'un tableau : « Consideration of Commons amendments and / or reasons » devient « Commons amendments », « Committee stage » « Committee ».
    /// L'infobulle de la puce garde le libellé complet de la source.
    /// </summary>
    public static string StatutCourt(Bill b)
    {
        var s = StatutLibelle(b);
        if (b.StatutCle != "en-cours") return s.Replace("Royal Assent ", "");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"^Consideration of ", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+and\s*/\s*or reasons$| stage$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return s;
    }

    /// <summary>Classe de teinte (puce de statut) : mêmes teintes que les pétitions, distinctes des couleurs de vote.</summary>
    public static string StatutClasse(Bill b) => b.StatutCle switch { "loi" => "st-ouverte", "rejete" => "st-archivee", "retire" => "st-neutre", _ => "st-seuil" };

    private static readonly IReadOnlyList<Colonne<Bill>> ColonnesExport =
    [
        new("Bill", b => b.Titre),
        new("Status", b => b.StatutCle),
        new("Current stage", b => b.EtapeCourante),
        new("Originating house", b => b.ChambreOrigine),
        new("Current house", b => b.ChambreCourante),
        new("First sitting", b => b.DateDepot),
        new("Royal Assent", b => b.DateLoi),
        new("Withdrawn on", b => b.RetireLe),
        new("Divisions linked (computed)", b => b.NbDivisions),
        new("Last update", b => b.DerniereMaj),
        new("URL", b => b.Url),
        new("Identifier", b => b.Id),
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
            var tout = await bills.ListerAsync(Recherche, Statut, Chambre, TriEffectif, Decroissant, 1, 0);
            return Exporteur.Ecrire(Format!, "bills", ColonnesExport, tout.Lignes);
        }
        Resultats = await bills.ListerAsync(Recherche, Statut, Chambre, TriEffectif, Decroissant, NumeroPage, ParPage);
        return Page();
    }
}
