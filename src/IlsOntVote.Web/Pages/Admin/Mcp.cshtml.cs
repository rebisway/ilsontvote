using IlsOntVote.Web.Mcp;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IlsOntVote.Web.Pages.Admin;

/// <summary>Réglage temporaire de la limite de débit du serveur MCP (l'usage se lit dans /Admin/Stats).</summary>
public class McpModel(LimiteMcp limite) : PageModel
{
    public int? Courante { get; private set; }
    /// <summary>Échéance UTC de la dérogation en cours ; null si la limite normale s'applique.</summary>
    public DateTime? Fin { get; private set; }
    [TempData] public string? Message { get; set; }

    public void OnGet()
    {
        var r = limite.Lire();
        Fin = r.FinLe is { } f && DateTime.UtcNow < f ? f : null;
        Courante = limite.Courante();
    }

    /// <summary>Active une dérogation. <paramref name="mode"/> : « limite » (valeur saisie) ou « aucune ».</summary>
    public async Task<IActionResult> OnPostActiverAsync(string? mode, int? valeur, int? jours)
    {
        // Saisie validée ici : un formulaire peut être envoyé sans les contrôles du navigateur.
        if (jours is not (>= 1 and <= LimiteMcp.JoursMax) || mode is not ("limite" or "aucune") || (mode == "limite" && valeur is not (>= 1 and <= 10000)))
        {
            Message = $"Saisie refusée : durée de 1 à {LimiteMcp.JoursMax} jours, limite de 1 à 10 000.";
            return RedirectToPage();
        }
        await limite.DerogerAsync(mode == "aucune" ? null : valeur, jours.Value);
        Message = "Dérogation activée.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRetablirAsync()
    {
        await limite.RetablirAsync();
        Message = "Limite normale rétablie.";
        return RedirectToPage();
    }
}
