using IlsOntVote.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IlsOntVote.Web.Pages.Identity.Account;

/// <summary>Déconnexion. Uniquement en POST (avec jeton anti-falsification) : un lien ou une image externe ne doit pas pouvoir déconnecter un visiteur.</summary>
public class LogoutModel(SignInManager<ApplicationUser> signInManager) : PageModel
{
    // GET : rien à déconnecter (évite qu'un lien externe déconnecte l'utilisateur) ; seul le POST agit.
    public IActionResult OnGet() => RedirectToPage("/Index");

    public async Task<IActionResult> OnPostAsync()
    {
        await signInManager.SignOutAsync();
        return Redirect("/");
    }
}
