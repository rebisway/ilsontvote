using IlsOntVote.Web.Models;
using IlsOntVote.Web.Repositories;
using IlsOntVote.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace IlsOntVote.Web.Pages;

/// <summary>
/// Pétitions déposées sur petitions.senat.fr (jeu « Pétitions du Sénat », data.gouv.fr). Liste triable, filtrable et exportable.
/// Les statuts et nombres de soutiens sont ceux de la source ; le site n'en tire aucune conclusion. Rappel affiché : ce sont des soutiens
/// de citoyens.
/// </summary>
public class PetitionsModel(IPetitionRepository petitions, Langue langue, Themes themes) : ListePageModel
{
    /// <summary>Code de thème (approximatif, mots-clés du titre) : ne garde que les éléments de ce thème.</summary>
    [BindProperty(SupportsGet = true)] public string? Theme { get; set; }
    /// <summary>Thèmes proposés par le filtre.</summary>
    public IReadOnlyList<Theme> ThemesListe => themes.Lister();
    [BindProperty(SupportsGet = true, Name = "q")] public string? Recherche { get; set; }
    /// <summary>« senat » ou « an » (vide = les deux plateformes).</summary>
    [BindProperty(SupportsGet = true)] public string? Chambre { get; set; }
    [BindProperty(SupportsGet = true)] public string? Statut { get; set; }
    [BindProperty(SupportsGet = true)] public string? Type { get; set; }
    /// <summary>Assemblée seulement : commission permanente saisie.</summary>
    [BindProperty(SupportsGet = true)] public string? Commission { get; set; }
    /// <summary>Historique : « oui » (au moins un constat), ou un type de constat (baisse, disparition, reapparition).</summary>
    [BindProperty(SupportsGet = true)] public string? Anomalie { get; set; }
    public static readonly string[] ChoixAnomalie = ["oui", "baisse", "disparition", "reapparition"];
    /// <summary>Période de PUBLICATION (comme les dates de /lois) : bornes incluses, une borne absente = ouverte.</summary>
    [BindProperty(SupportsGet = true)] public string? Du { get; set; }
    [BindProperty(SupportsGet = true)] public string? Au { get; set; }

    public Resultats<Petition> Resultats { get; private set; } = new([], 0, 1, 30);

    public override int TotalLignes => Resultats.Total;
    public IReadOnlyList<EffectifPetition> ParStatut { get; private set; } = [];
    public IReadOnlyList<EffectifPetition> ParType { get; private set; } = [];
    public IReadOnlyList<EffectifPetition> ParCommission { get; private set; } = [];

    protected override string TriDefaut => "date";
    protected override IReadOnlyCollection<string> ClesTri { get; } = [.. PetitionRepository.Tris.Keys];
    // Date et soutiens : le premier clic trie du plus grand au plus petit ; titre, statut, type : alphabétique.
    protected override bool DecroissantInitial(string cle) => cle is "date" or "soutiens" or "fin" or "historique";
    protected override IEnumerable<(string Nom, string? Valeur)> Filtres => [("q", Recherche), ("chambre", Chambre), ("statut", Statut), ("type", Type), ("theme", themes.Valider(Theme)), ("commission", Commission), ("anomalie", Anomalie), ("du", Du), ("au", Au)];

    /// <summary>Code de chambre en base (SENAT / AN) d'après le paramètre d'adresse ; NULL = toutes.</summary>
    /// <summary>Plateforme affichée : sur le site britannique (/uk) la seule « UK » ; sur le site français, selon le paramètre (null = les deux plateformes françaises).</summary>
    public string? ChambreCode => langue.Pays == "GB" ? "UK" : Chambre switch { "senat" => "SENAT", "an" => "AN", _ => null };

    /// <summary>Nom de la plateforme dans la langue de la page (textes en base).</summary>
    public static string PlateformeLibelle(string code, Langue l) => code switch
    {
        "AN" => l.Chambre("AN"),
        "SENAT" => l.Chambre("SENAT"),
        "UK" => l.T("plateforme-parlement-britannique"),
        _ => code,
    };

    /// <summary>Libellé du statut dans la langue de la page (les valeurs publiées par la source sont en français ; une valeur inconnue reste telle quelle). Textes en base.</summary>
    public static string StatutLibelle(string statut, Langue l) => statut.ToLowerInvariant() switch
    {
        // Sénat
        "seuil non atteint" => l.T("petition-statut-seuil-non-atteint"),
        "seuil atteint" => l.T("petition-statut-seuil-atteint"),
        // Assemblée (libellés de la source en minuscules sans accents : « archivee », « classee », « expiree »)
        "ouverte" or "open" => l.T("petition-statut-ouverte"),   // « open » : plateforme britannique
        "closed" => l.T("petition-statut-cloturee"),
        "archivee" => l.T("petition-statut-archivee"),
        "classee" => l.T("petition-statut-classee"),
        "expiree" => l.T("petition-statut-expiree"),
        _ => statut,
    };

    /// <summary>
    /// Classe CSS de la teinte d'un statut (puce colorée). Teintes volontairement DISTINCTES des couleurs de vote (bleu/rouge/orange) : un statut de
    /// pétition n'est pas un vote. « Seuil atteint » (Sénat) ressort ; « classée » est violet ; archivée, expirée et seuil non atteint sont neutres.
    /// </summary>
    public static string StatutClasse(string statut) => statut.ToLowerInvariant() switch
    {
        "ouverte" => "st-ouverte",
        "open" => "st-ouverte",
        "closed" => "st-archivee",
        "seuil atteint" => "st-seuil",
        "classee" => "st-classee",
        "archivee" => "st-archivee",
        _ => "st-neutre",   // expiree, seuil non atteint
    };

    /// <summary>Libellé du type dans la langue de la page (textes en base).</summary>
    public static string TypeLibelle(string? type, Langue l) => type is null ? "—" : type switch
    {
        "Texte législatif" => l.T("petition-type-texte-legislatif"),
        "Mission de contrôle" => l.T("petition-type-mission-de-controle"),
        _ => type,
    };

    private IReadOnlyList<Colonne<Petition>> ColonnesExport()
    {
        var en = langue.Anglais;
        return new List<Colonne<Petition>>
        {
            new(langue.T("plateforme"), p => p.Chambre == "AN" ? (langue.T("assemblee-nationale")) : (langue.T("senat"))),
            new(langue.T("date-de-publication"), p => p.DatePublication),
            new(langue.T("titre"), p => p.Titre),
            new(langue.T("themes-estimes-par-mots-cles-du"), p => string.Join(", ", p.Themes)),
            new("Type", p => p.Type),
            new(langue.T("statut"), p => p.Statut),
            new(langue.T("soutiens"), p => p.NbVotes),
            new(langue.T("fin-du-recueil"), p => p.DateLimiteVote),
            new(langue.T("constats-historique"), p => p.NbAnomalies),
            new(langue.T("commission"), p => p.Commission),
            new(langue.T("reponse-du-gouvernement"), p => p.ReponseLe),
            new(langue.T("debattue-le"), p => p.DebatLe),
            new(langue.T("decision-de-la-commission"), p => p.DecisionCommission),
            new("URL", p => p.Url),
        };
    }

    public async Task<IActionResult> OnGetAsync()
    {
        // Pétitions : France et Royaume-Uni seulement (aucune plateforme américaine importée) : 404 sous les autres pays.
        if (langue.Pays is not null && !PaysSite.APetitionsEtProjets(langue.Pays)) return NotFound();
        // Effectifs d'abord : un statut, un type ou une commission qui n'existe pas dans la chambre choisie (changement de chambre avec un
        // filtre déjà posé) est ignoré, au lieu d'afficher une liste vide sans explication.
        ParStatut = await petitions.CompterParStatutAsync();
        ParType = await petitions.CompterParTypeAsync();
        ParCommission = await petitions.CompterParCommissionAsync();
        bool Valide(IReadOnlyList<EffectifPetition> liste, string? v) => v is null || liste.Any(x => x.Libelle == v && (ChambreCode is null || x.Chambre == ChambreCode));
        if (!Valide(ParStatut, Statut)) Statut = null;
        if (!Valide(ParType, Type)) Type = null;
        if (!Valide(ParCommission, Commission)) Commission = null;

        if (!ChoixAnomalie.Contains(Anomalie)) Anomalie = null;
        var (du, au) = Services.Periode.Lire(Du, Au);
        if (ExportDemande)
        {
            var tout = await petitions.ListerAsync(Recherche, ChambreCode, Statut, Type, Commission, Anomalie, du, au, TriEffectif, Decroissant, 1, 0, themes.Valider(Theme));
            return Exporteur.Ecrire(Format!, langue.T("petitions-2"), ColonnesExport(), tout.Lignes);
        }
        Resultats = await petitions.ListerAsync(Recherche, ChambreCode, Statut, Type, Commission, Anomalie, du, au, TriEffectif, Decroissant, NumeroPage, ParPage, themes.Valider(Theme));
        return Page();
    }
}
