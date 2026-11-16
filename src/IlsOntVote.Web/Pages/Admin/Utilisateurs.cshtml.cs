using IlsOntVote.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IlsOntVote.Web.Pages.Admin;

public sealed record LigneUtilisateur(string Id, string Email, DateTime CreeLe, bool Admin, bool Verrouille);

/// <summary>Gestion des comptes (auth/droits uniquement). Pas d'inscription publique : les comptes sont créés ici.</summary>
public class UtilisateursModel(UserManager<ApplicationUser> users) : PageModel
{
    public IReadOnlyList<LigneUtilisateur> Lignes { get; private set; } = [];
    public string? CurrentUserId { get; private set; }

    [TempData] public string? Message { get; set; }
    [TempData] public string? Erreur { get; set; }

    public async Task OnGetAsync() => await ChargerAsync();

    private async Task ChargerAsync()
    {
        CurrentUserId = users.GetUserId(User);
        var liste = new List<LigneUtilisateur>();
        foreach (var u in await users.Users.OrderBy(u => u.CreatedAt).ToListAsync())
        {
            liste.Add(new LigneUtilisateur(
                u.Id, u.Email ?? u.UserName ?? "?", u.CreatedAt,
                await users.IsInRoleAsync(u, "Admin"),
                await users.IsLockedOutAsync(u)));
        }
        Lignes = liste;
    }

    public async Task<IActionResult> OnPostCreerAsync(string email, string motDePasse, bool admin)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(motDePasse))
        {
            Erreur = "Adresse e-mail et mot de passe obligatoires.";
            return RedirectToPage();
        }

        var user = new ApplicationUser { UserName = email.Trim(), Email = email.Trim(), EmailConfirmed = true, CreatedAt = DateTime.UtcNow };
        var resultat = await users.CreateAsync(user, motDePasse);
        if (!resultat.Succeeded)
        {
            Erreur = string.Join(" ", resultat.Errors.Select(e => e.Description));
            return RedirectToPage();
        }

        if (admin) await users.AddToRoleAsync(user, "Admin");
        Message = $"Compte créé : {user.Email}.";
        return RedirectToPage();
    }

    /// <summary>Accorde ou retire le rôle Admin. Interdit sur soi-même : on ne doit pas pouvoir se retirer son propre accès par erreur.</summary>
    public async Task<IActionResult> OnPostRoleAsync(string id, bool devenirAdmin)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) return RedirectToPage();

        if (id == users.GetUserId(User))
        {
            Erreur = "Vous ne pouvez pas modifier vos propres droits.";
            return RedirectToPage();
        }

        if (devenirAdmin) await users.AddToRoleAsync(user, "Admin");
        else await users.RemoveFromRoleAsync(user, "Admin");

        Message = $"{user.Email} : droits d'administration {(devenirAdmin ? "accordés" : "retirés")}.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostVerrouAsync(string id, bool verrouiller)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) return RedirectToPage();

        if (id == users.GetUserId(User))
        {
            Erreur = "Vous ne pouvez pas verrouiller votre propre compte.";
            return RedirectToPage();
        }

        // Verrouiller = fin de verrouillage très lointaine (100 ans) ; déverrouiller = date nulle.
        // SetLockoutEnabled(true) est nécessaire : sans lui, une fin de verrouillage est ignorée par Identity.
        await users.SetLockoutEnabledAsync(user, true);
        await users.SetLockoutEndDateAsync(user, verrouiller ? DateTimeOffset.UtcNow.AddYears(100) : null);
        if (!verrouiller) await users.ResetAccessFailedCountAsync(user);

        Message = $"{user.Email} : compte {(verrouiller ? "verrouillé" : "déverrouillé")}.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSupprimerAsync(string id)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) return RedirectToPage();

        if (id == users.GetUserId(User))
        {
            Erreur = "Vous ne pouvez pas supprimer votre propre compte.";
            return RedirectToPage();
        }

        await users.DeleteAsync(user);
        Message = $"{user.Email} : compte supprimé.";
        return RedirectToPage();
    }
}
