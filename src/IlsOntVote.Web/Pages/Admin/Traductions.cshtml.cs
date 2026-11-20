using IlsOntVote.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IlsOntVote.Web.Pages.Admin;

/// <summary>
/// Édition de la table de traduction de l'interface (voir <see cref="Traductions"/>). Une ligne = une clé STABLE, le texte français traduit (<c>source_fr</c>) et sa traduction dans une
/// langue. Trois listes : les textes demandés par les pages SANS traduction depuis le démarrage (« à traduire », avec le français et l'anglais sous les yeux), les traductions PÉRIMÉES
/// (la phrase française de la page a changé depuis qu'elles ont été écrites : à relire) et les traductions enregistrées (modifiables, supprimables). Un texte supprimé retombe sur celui
/// du code. Le changement est immédiat.
/// </summary>
public class TraductionsModel(Traductions traductions) : PageModel
{
    [BindProperty(SupportsGet = true)] public string Langue { get; set; } = "en";
    /// <summary>Code d'une langue nouvelle saisi dans le formulaire (prioritaire sur la liste).</summary>
    [BindProperty(SupportsGet = true)] public string? Nouvelle { get; set; }
    [BindProperty(SupportsGet = true, Name = "q")] public string? Recherche { get; set; }
    public IReadOnlyList<string> Langues { get; private set; } = [];
    public IReadOnlyList<LigneTraduction> Lignes { get; private set; } = [];
    public IReadOnlyList<TexteATraduire> AFaire { get; private set; } = [];
    /// <summary>Traductions utilisées dont le français de la page a changé depuis (à relire).</summary>
    public IReadOnlyList<TraductionPerimee> Perimees { get; private set; } = [];
    [TempData] public string? Message { get; set; }

    public async Task OnGetAsync()
    {
        Langue = Normaliser(string.IsNullOrWhiteSpace(Nouvelle) ? Langue : Nouvelle.Trim().ToLowerInvariant());
        Langues = await traductions.LanguesAsync();
        Lignes = await traductions.ListerAsync(Langue, Recherche);
        AFaire = Langue is "fr" or "en" ? [] : traductions.Manquants(Langue);
        Perimees = traductions.Perimees(Langue);
    }

    /// <summary>Enregistre (ou modifie) une traduction. Un texte vide supprime la ligne : le texte du code s'applique de nouveau.</summary>
    public async Task<IActionResult> OnPostEnregistrerAsync(string langue, string cle, string texte, string? sourceFr)
    {
        langue = Normaliser(langue);
        if (string.IsNullOrWhiteSpace(cle)) { Message = "Clé manquante."; return RedirectToPage(new { langue }); }
        if (string.IsNullOrWhiteSpace(texte)) { await traductions.SupprimerAsync(langue, cle); Message = "Traduction supprimée."; }
        else { await traductions.EnregistrerAsync(langue, cle, texte.Trim(), string.IsNullOrWhiteSpace(sourceFr) ? null : sourceFr); Message = "Traduction enregistrée (effet immédiat)."; }
        return RedirectToPage(new { langue });
    }

    public async Task<IActionResult> OnPostSupprimerAsync(string langue, string cle)
    {
        langue = Normaliser(langue);
        await traductions.SupprimerAsync(langue, cle);
        Message = "Traduction supprimée : le texte du code s'applique de nouveau.";
        return RedirectToPage(new { langue });
    }

    /// <summary>Code de langue valide (deux lettres minuscules, comme la contrainte de la table) ; « en » par défaut.</summary>
    private static string Normaliser(string? l) => l is { Length: 2 } && l.All(char.IsAsciiLetterLower) ? l : "en";
}
