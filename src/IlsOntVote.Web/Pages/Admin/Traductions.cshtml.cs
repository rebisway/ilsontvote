using IlsOntVote.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IlsOntVote.Web.Pages.Admin;

/// <summary>
/// Édition de la table de traduction de l'interface (voir <see cref="Traductions"/>). Une ligne = un texte français (la clé) et sa traduction dans une langue. Deux listes :
/// les traductions enregistrées (modifiables, supprimables) et, pour une langue autre que le français et l'anglais, les textes demandés par les pages SANS traduction depuis le
/// démarrage (« à traduire »). Un texte supprimé retombe sur celui du code. Le changement est immédiat.
/// </summary>
public class TraductionsModel(Traductions traductions) : PageModel
{
    [BindProperty(SupportsGet = true)] public string Langue { get; set; } = "en";
    /// <summary>Code d'une langue nouvelle saisi dans le formulaire (prioritaire sur la liste).</summary>
    [BindProperty(SupportsGet = true)] public string? Nouvelle { get; set; }
    [BindProperty(SupportsGet = true, Name = "q")] public string? Recherche { get; set; }
    public IReadOnlyList<string> Langues { get; private set; } = [];
    public IReadOnlyList<LigneTraduction> Lignes { get; private set; } = [];
    public IReadOnlyList<(string Cle, string Repli)> AFaire { get; private set; } = [];
    [TempData] public string? Message { get; set; }

    public async Task OnGetAsync()
    {
        Langue = Normaliser(string.IsNullOrWhiteSpace(Nouvelle) ? Langue : Nouvelle.Trim().ToLowerInvariant());
        Langues = await traductions.LanguesAsync();
        Lignes = await traductions.ListerAsync(Langue, Recherche);
        AFaire = Langue is "fr" or "en" ? [] : traductions.Manquants(Langue);
    }

    /// <summary>Enregistre (ou modifie) une traduction. Un texte vide supprime la ligne : le texte du code s'applique de nouveau.</summary>
    public async Task<IActionResult> OnPostEnregistrerAsync(string langue, string cle, string texte)
    {
        langue = Normaliser(langue);
        if (string.IsNullOrWhiteSpace(cle)) { Message = "Texte français manquant."; return RedirectToPage(new { langue }); }
        if (string.IsNullOrWhiteSpace(texte)) { await traductions.SupprimerAsync(langue, cle); Message = "Traduction supprimée."; }
        else { await traductions.EnregistrerAsync(langue, cle, texte.Trim()); Message = "Traduction enregistrée (effet immédiat)."; }
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
