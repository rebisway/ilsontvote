using IlsOntVote.Web.Models;
using IlsOntVote.Web.Repositories;
using IlsOntVote.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace IlsOntVote.Web.Pages;

/// <summary>
/// Page publique des données en anomalie (règle du doute : ce que le site sait ne pas savoir est dit). Limitée aux chambres du site courant.
/// N'expose que l'état des DONNÉES : jamais les imports, leurs erreurs ni leurs sorties (réservés à /Admin/Qualite).
/// </summary>
public class AnomaliesModel(IQualiteRepository qualite, PaysSite pays, Langue langue) : ListePageModel
{
    [BindProperty(SupportsGet = true)] public string? Chambre { get; set; }
    /// <summary>incomplet | sans-resultat ; vide = les deux.</summary>
    [BindProperty(SupportsGet = true)] public string? Type { get; set; }

    public IReadOnlyList<ChambreQualite> Bilan { get; private set; } = [];
    public IReadOnlyDictionary<string, int> OriginesInconnues { get; private set; } = new Dictionary<string, int>();
    public Resultats<Scrutin> Resultats { get; private set; } = new([], 0, 1, 30);
    public override int TotalLignes => Resultats.Total;

    protected override string TriDefaut => "date";
    protected override IReadOnlyCollection<string> ClesTri { get; } = [.. QualiteRepository.TrisAnomalies.Keys];
    protected override bool DecroissantInitial(string cle) => cle is "date" or "manquants";
    protected override IEnumerable<(string Nom, string? Valeur)> Filtres => [("chambre", Chambre), ("type", Type)];

    private IReadOnlyList<Colonne<Scrutin>> ColonnesExport() =>
    [
        new(langue.T("date"), s => s.DateScrutin),
        new(langue.T("anom-chambre"), s => langue.Chambre(s.Chambre)),
        new(langue.T("anom-vote"), s => s.TitreLangue(langue.Anglais)),
        new(langue.T("anom-incomplet"), s => s.NominatifIncomplet ? langue.T("anom-oui") : langue.T("anom-non")),
        new(langue.T("anom-noms-manquants"), s => s.NominatifIncomplet ? s.NbVotesManquants : null),
        new(langue.T("anom-sans-resultat"), s => s.Sort is null ? langue.T("anom-oui") : langue.T("anom-non")),
        new(langue.T("identifiant-67e9"), s => s.Id),
    ];

    public async Task<IActionResult> OnGetAsync()
    {
        var visibles = pays.ChambresVisibles(langue.Pays);
        if (Type is not ("incomplet" or "sans-resultat")) Type = null;
        // La chambre demandée doit appartenir au site : sinon « toutes » (jamais les données d'un autre pays).
        var chambre = visibles.FirstOrDefault(c => string.Equals(c, Chambre, StringComparison.OrdinalIgnoreCase));
        Chambre = chambre;
        var choisies = chambre is null ? visibles : [chambre];
        if (ExportDemande)
        {
            var tout = await qualite.AnomaliesAsync(choisies, Type, TriEffectif, Decroissant, 1, 0);
            return Exporteur.Ecrire(Format!, "anomalies", ColonnesExport(), tout.Lignes);
        }
        Resultats = await qualite.AnomaliesAsync(choisies, Type, TriEffectif, Decroissant, NumeroPage, ParPage);
        // Une chambre sans aucun scrutin (pas encore importée) n'a rien à signaler.
        Bilan = (await qualite.ChambresAsync()).Where(c => visibles.Contains(c.Code) && c.Scrutins > 0).ToList();
        OriginesInconnues = await qualite.OriginesInconnuesAsync(visibles);
        return Page();
    }
}
