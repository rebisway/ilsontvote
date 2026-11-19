using System.Net;
using Microsoft.AspNetCore.Html;

namespace IlsOntVote.Web.Services;

/// <summary>
/// Nom d'un élu. Le balisage contient toujours le prénom et le nom dans deux éléments ; c'est le CSS
/// (<c>html[data-ordre]</c>, choisi par le visiteur et mémorisé dans son navigateur) qui décide de
/// l'ordre « Prénom Nom » ou « Nom Prénom ». Ainsi les pages restent identiques pour tous (cache de sortie).
/// </summary>
public static class Noms
{
    public static HtmlString Html(string prenom, string nom) =>
        // Prénom vide (pairs britanniques : le titre fait partie du nom, « Lord Ahmed ») : une seule partie, sans espace de tête.
        string.IsNullOrWhiteSpace(prenom)
            ? new($"<span class=\"np\"><span class=\"np-n\">{WebUtility.HtmlEncode(nom)}</span></span>")
            : new($"<span class=\"np\"><span class=\"np-p\">{WebUtility.HtmlEncode(prenom)}</span> <span class=\"np-n\">{WebUtility.HtmlEncode(nom)}</span></span>");
}
