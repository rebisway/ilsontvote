using System.ComponentModel.DataAnnotations;
using IlsOntVote.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace IlsOntVote.Web.Pages.Identity.Account;

[EnableRateLimiting("login")]
/// <summary>
/// Connexion à l'administration. Verrouillage du compte après plusieurs échecs (Identity) ET limitation par IP
/// (politique « login » du limiteur de débit) : la seconde protège aussi contre l'énumération de comptes.
/// </summary>
public class LoginModel(SignInManager<ApplicationUser> signInManager) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();
    public string? Erreur { get; set; }

    public class InputModel
    {
        [Required, EmailAddress] public string Email { get; set; } = "";
        [Required, DataType(DataType.Password)] public string Password { get; set; } = "";
        public bool SeSouvenir { get; set; }
    }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        if (!ModelState.IsValid) return Page();

        var resultat = await signInManager.PasswordSignInAsync(Input.Email, Input.Password, Input.SeSouvenir, lockoutOnFailure: true);
        if (resultat.Succeeded)
            // Redirection uniquement vers une URL locale (pas d'open redirect).
            return LocalRedirect(!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/Admin");

        // Même message pour « inconnu » et « mauvais mot de passe » : ne pas révéler quels comptes existent.
        Erreur = resultat.IsLockedOut
            ? "Compte temporairement verrouillé après plusieurs échecs. Réessayez plus tard."
            : "Identifiants incorrects.";
        return Page();
    }
}
