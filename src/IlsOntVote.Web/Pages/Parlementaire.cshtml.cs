using IlsOntVote.Web.Services;
using IlsOntVote.Web.Models;
using IlsOntVote.Web.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IlsOntVote.Web.Pages;

/// <summary>Fiche d'un élu : /depute/{slug} ou /senateur/{slug}.</summary>
public class ParlementaireModel(IParlementaireRepository parlementaires, PaysSite site, Langue langue) : PageModel
{
    // 40 votes par page dans l'historique d'un élu.
    private const int ParPage = 40;

    // « page » est aussi une valeur de route réservée de Razor Pages (le chemin de la page, ex. « /Textes ») : sans [FromQuery],
    // elle masquait ?page=2 et « Suivant » restait sur la page 1.
    [BindProperty(SupportsGet = true), FromQuery(Name = "page")] public int NumeroPage { get; set; } = 1;
    /// <summary>« tout » pour inclure amendements et articles ; sinon votes sur l'ensemble d'un texte.</summary>
    [BindProperty(SupportsGet = true)] public string? Portee { get; set; }

    public Parlementaire Elu { get; private set; } = null!;
    /// <summary>Participation sur les scrutins publics tenus pendant ses mandats (scrutins aux données incomplètes exclus).</summary>
    public BilanParlementaire Bilan { get; private set; } = new();
    /// <summary>Même bilan sur les votes sur l'ensemble d'un texte ; null si non applicable (Lords, Irlande) ou aucun.</summary>
    public BilanParlementaire? BilanEnsemble { get; private set; }
    /// <summary>Périodes de mandat (début = prise de fonction), affichées sous le nom : elles expliquent un nombre de scrutins parfois faible.</summary>
    public IReadOnlyList<PeriodeMandat> Mandats { get; private set; } = [];
    /// <summary>Participation mois par mois (graphique de la fiche).</summary>
    public IReadOnlyList<PointMensuel> Mensuel { get; private set; } = [];
    public Resultats<LigneVote> Votes { get; private set; } = new([], 0, 1, ParPage);
    // Défaut : 40 lignes par page (cf. ChoixLignes).

    /// <summary>Période (yyyy-MM-dd) appliquée à l'historique de vote.</summary>
    [BindProperty(SupportsGet = true)] public string? Du { get; set; }
    [BindProperty(SupportsGet = true)] public string? Au { get; set; }

    /// <summary>Recherche dans le titre des scrutins.</summary>
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    /// <summary>Filtre sur son vote : pour, contre, abstention, non_votant, absent, delegation, mise_au_point.</summary>
    [BindProperty(SupportsGet = true)] public string? Vote { get; set; }
    /// <summary>Lignes par page : choix dans une liste blanche.</summary>
    [BindProperty(SupportsGet = true), FromQuery(Name = "lignes")] public string? Lignes { get; set; }
    public static readonly int[] ChoixLignes = [20, 40, 100, 200];
    /// <summary>Valeur de « Tous » (plafonnée : une fiche aux milliers de votes ne doit pas produire une page de plusieurs Mo ; l'export donne tout).</summary>
    public const int Toutes = 5000;
    public int ParPageEff => Lignes == "tous" ? Toutes : int.TryParse(Lignes, out var n) && ChoixLignes.Contains(n) ? n : ParPage;
    public static readonly string[] ChoixVote = ["pour", "contre", "abstention", "absent", "non_votant", "delegation", "mise_au_point"];
    public bool FiltreActif => !string.IsNullOrWhiteSpace(Q) || !string.IsNullOrEmpty(Vote) || !string.IsNullOrEmpty(Du) || !string.IsNullOrEmpty(Au) || ToutAfficher;

    /// <summary>Chambre de la fiche (déduite de l'adresse).</summary>
    public string Chambre { get; private set; } = Chambres.AN;
    /// <summary>
    /// Aux Communes, aucun vote n'est rattaché à un texte « sur l'ensemble » : le filtre par défaut viderait l'historique, donc toutes les divisions sont listées
    /// (et la case « amendements et articles » est masquée). Ailleurs : selon la case.
    /// </summary>
    public bool ToutAfficher => Portee == "tout" || Chambres.SansVoteSurEnsemble(Chambre);

    /// <summary>Élus voisins (précédent / suivant dans l'ordre alphabétique de la liste) pour la navigation.</summary>
    public VoisinsVue Voisins { get; private set; } = new("/", null, null);

    public async Task<IActionResult> OnGetAsync(string slug)
    {
        var chambre = Chambres.DepuisChemin(Request.Path.Value ?? "");
        Chambre = chambre;
        // Fiches des chambres du site courant seulement (une fiche britannique n'existe pas côté français, ni l'inverse).
        if (!site.ChambresVisibles(langue.Pays).Contains(chambre)) return NotFound();

        // Le slug n'est unique que par chambre : un sénateur et un député peuvent porter le même nom.
        var elu = await parlementaires.GetParSlugAsync(chambre, slug);
        if (elu is null) return NotFound();

        Elu = elu;
        // Voisins = ordre de la liste des élus en exercice (nom, prénom) : un ancien élu, absent de cette liste, n'a pas de navigation.
        var (prec, suiv) = await parlementaires.GetVoisinsAsync(chambre, elu.Id);
        Voisin Vers(Parlementaire x) => new(x.Slug, $"{x.Prenom} {x.Nom}");
        Voisins = new VoisinsVue("/" + Chambres.Fiche(chambre) + "/", prec is null ? null : Vers(prec), suiv is null ? null : Vers(suiv));
        Bilan = await parlementaires.GetBilanAsync(elu.Id, chambre);
        BilanEnsemble = await parlementaires.GetBilanEnsembleAsync(elu.Id, chambre);
        Mensuel = await parlementaires.GetParticipationMensuelleAsync(elu.Id);
        Mandats = await parlementaires.GetMandatsAsync(elu.Id);
        var (du, au) = Periode.Lire(Du, Au);
        Votes = await parlementaires.GetVotesAsync(elu.Id, chambre, !ToutAfficher, NumeroPage, ParPageEff, du, au, Q, Vote);
        return Page();
    }

    public string LienPage(int n, int? lignes = null)
    {
        var q = new List<string>();
        if (!string.IsNullOrWhiteSpace(Q)) q.Add("q=" + Uri.EscapeDataString(Q));
        if (!string.IsNullOrEmpty(Vote)) q.Add("vote=" + Uri.EscapeDataString(Vote));
        var l = lignes ?? (Lignes == "tous" ? Toutes : int.TryParse(Lignes, out var ln0) ? ln0 : (int?)null);
        if (l is int ln && ln != ParPage) q.Add(ln == Toutes ? "lignes=tous" : "lignes=" + ln);
        if (ToutAfficher) q.Add("portee=tout");
        if (!string.IsNullOrEmpty(Du)) q.Add("du=" + Uri.EscapeDataString(Du));
        if (!string.IsNullOrEmpty(Au)) q.Add("au=" + Uri.EscapeDataString(Au));
        if (n > 1) q.Add("page=" + n);
        return Request.PathBase + Elu.Lien + (q.Count > 0 ? "?" + string.Join("&", q) : "");
    }
}
