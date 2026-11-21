using System.Globalization;
using IlsOntVote.Web.Models;
using IlsOntVote.Web.Repositories;
using IlsOntVote.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace IlsOntVote.Web.Pages;

/// <summary>
/// Recherche des anomalies relevées sur les pétitions du Sénat : baisse du nombre de soutiens entre deux relevés, disparition du fichier source,
/// réapparition. Critères cumulables (type, période de détection, ampleur de la baisse en soutiens ou en %, soutiens avant, mots du titre), tri et export.
/// Un constat n'a pas de cause connue : le fichier source n'en donne pas, le site n'en suppose aucune.
/// </summary>
public class PetitionAnomaliesModel(IPetitionRepository petitions, Langue langue) : ListePageModel
{
    [BindProperty(SupportsGet = true)] public string? Type { get; set; }
    /// <summary>« senat » ou « an » (vide = les deux plateformes).</summary>
    [BindProperty(SupportsGet = true)] public string? Chambre { get; set; }
    [BindProperty(SupportsGet = true)] public string? Du { get; set; }
    [BindProperty(SupportsGet = true)] public string? Au { get; set; }
    /// <summary>Baisse minimale en nombre de soutiens.</summary>
    [BindProperty(SupportsGet = true, Name = "baisse")] public string? BaisseTexte { get; set; }
    /// <summary>Baisse minimale en % du nombre précédent.</summary>
    [BindProperty(SupportsGet = true, Name = "pct")] public string? PctTexte { get; set; }
    /// <summary>Soutiens au relevé précédent : au moins.</summary>
    [BindProperty(SupportsGet = true, Name = "avant")] public string? AvantTexte { get; set; }
    [BindProperty(SupportsGet = true, Name = "q")] public string? Recherche { get; set; }

    public Resultats<AnomaliePetition> Resultats { get; private set; } = new([], 0, 1, 30);

    public override int TotalLignes => Resultats.Total;
    public IReadOnlyList<EffectifPetition> ParType { get; private set; } = [];

    public static readonly string[] Types = ["baisse", "disparition", "reapparition"];

    protected override string TriDefaut => "date";
    protected override IReadOnlyCollection<string> ClesTri { get; } = [.. PetitionRepository.TrisAnomalies.Keys];
    protected override bool DecroissantInitial(string cle) => cle is not ("titre" or "type");
    protected override IEnumerable<(string Nom, string? Valeur)> Filtres =>
        [("type", Type), ("chambre", Chambre), ("du", Du), ("au", Au), ("baisse", BaisseTexte), ("pct", PctTexte), ("avant", AvantTexte), ("q", Recherche)];

    /// <summary>Libellé d'un type d'anomalie dans la langue de la page (textes en base).</summary>
    public static string TypeLibelle(string type, Langue l) => type switch
    {
        "baisse" => l.T("anomalie-type-baisse"),
        "disparition" => l.T("anomalie-type-disparition"),
        "reapparition" => l.T("anomalie-type-reapparition"),
        _ => type,
    };

    private static int? Entier(string? s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= 0 ? n : null;
    private static double? Reel(string? s) => double.TryParse(s?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d >= 0 ? d : null;

    private CriteresAnomalies Criteres()
    {
        var (du, au) = Services.Periode.Lire(Du, Au);
        return new CriteresAnomalies(Types.Contains(Type) ? Type : null, du, au, Entier(BaisseTexte), Reel(PctTexte), Entier(AvantTexte), Recherche, Chambre switch { "senat" => "SENAT", "an" => "AN", _ => null });
    }

    private IReadOnlyList<Colonne<AnomaliePetition>> ColonnesExport()
    {
        var en = langue.Anglais;
        return new List<Colonne<AnomaliePetition>>
        {
            new(langue.T("detectee-le"), a => a.DetecteLe),
            new(langue.T("releve-precedent-2"), a => a.ReleveAvant),
            new(langue.T("plateforme"), a => PetitionsModel.PlateformeLibelle(a.Chambre, langue)),
            new(langue.T("titre"), a => a.Titre),
            new("Type", a => a.Type),
            new(langue.T("soutiens-avant-2"), a => a.ValeurAvant),
            new(langue.T("soutiens-apres"), a => a.ValeurApres),
            new(langue.T("baisse"), a => a.Baisse),
            new(langue.T("baisse-2"), a => a.BaissePct is { } p ? Math.Round(p, 1) : null),
            new(langue.T("identifiant-petition"), a => a.PetitionId),
        };
    }

    public async Task<IActionResult> OnGetAsync()
    {
        // Pétitions : France et Royaume-Uni seulement (aucune plateforme américaine importée) : 404 sous les autres pays.
        if (langue.Pays is not null && !PaysSite.APetitionsEtProjets(langue.Pays)) return NotFound();
        var c = Criteres();
        if (ExportDemande)
        {
            var tout = await petitions.ListerAnomaliesAsync(c, TriEffectif, Decroissant, 1, 0);
            return Exporteur.Ecrire(Format!, langue.T("anomalies-petitions"), ColonnesExport(), tout.Lignes);
        }
        Resultats = await petitions.ListerAnomaliesAsync(c, TriEffectif, Decroissant, NumeroPage, ParPage);
        ParType = await petitions.CompterAnomaliesParTypeAsync();
        return Page();
    }
}
