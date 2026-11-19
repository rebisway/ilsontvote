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
    public IReadOnlyList<EffectifPetition> ParType { get; private set; } = [];

    public static readonly string[] Types = ["baisse", "disparition", "reapparition"];

    protected override string TriDefaut => "date";
    protected override IReadOnlyCollection<string> ClesTri { get; } = [.. PetitionRepository.TrisAnomalies.Keys];
    protected override bool DecroissantInitial(string cle) => cle is not ("titre" or "type");
    protected override IEnumerable<(string Nom, string? Valeur)> Filtres =>
        [("type", Type), ("chambre", Chambre), ("du", Du), ("au", Au), ("baisse", BaisseTexte), ("pct", PctTexte), ("avant", AvantTexte), ("q", Recherche)];

    /// <summary>Libellé d'un type d'anomalie dans la langue de la page.</summary>
    public static string TypeLibelle(string type, bool anglais) => type switch
    {
        "baisse" => anglais ? "Drop in supporters" : "Baisse des soutiens",
        "disparition" => anglais ? "Missing from source file" : "Absente du fichier source",
        "reapparition" => anglais ? "Back in source file" : "De nouveau dans le fichier source",
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
            new(en ? "Detected on" : "Détectée le", a => a.DetecteLe),
            new(en ? "Previous snapshot" : "Relevé précédent", a => a.ReleveAvant),
            new(en ? "Platform" : "Plateforme", a => PetitionsModel.PlateformeLibelle(a.Chambre, en)),
            new(en ? "Title" : "Titre", a => a.Titre),
            new("Type", a => a.Type),
            new(en ? "Supporters before" : "Soutiens avant", a => a.ValeurAvant),
            new(en ? "Supporters after" : "Soutiens après", a => a.ValeurApres),
            new(en ? "Drop" : "Baisse", a => a.Baisse),
            new(en ? "Drop (%)" : "Baisse (%)", a => a.BaissePct is { } p ? Math.Round(p, 1) : null),
            new(en ? "Petition id" : "Identifiant pétition", a => a.PetitionId),
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
            return Exporteur.Ecrire(Format!, langue.Anglais ? "petition-anomalies" : "anomalies-petitions", ColonnesExport(), tout.Lignes);
        }
        Resultats = await petitions.ListerAnomaliesAsync(c, TriEffectif, Decroissant, NumeroPage, ParPage);
        ParType = await petitions.CompterAnomaliesParTypeAsync();
        return Page();
    }
}
