using System.Globalization;
using Microsoft.AspNetCore.Html;

namespace IlsOntVote.Web.Services;

/// <summary>
/// Langue de la requête : français (racine du site) ou anglais (préfixe <c>/en</c>). Le préfixe est retiré de l'adresse par
/// <see cref="Langue.Middleware"/> et placé dans <c>PathBase</c> : le routage voit les mêmes pages dans les deux langues, et les liens
/// générés par ASP.NET gardent le préfixe. Les traductions s'écrivent À CÔTÉ du français, dans les pages : <c>@L.T("Lois", "Laws")</c>.
/// Les DONNÉES (titres de lois, noms de groupes) restent en français : ce sont les sources officielles.
/// </summary>
public sealed class Langue(IHttpContextAccessor accessor)
{
    private const string Cle_Items = "lang";
    private static readonly CultureInfo Fr = new("fr-FR");
    private static readonly CultureInfo En = new("en-GB");

    private HttpContext Ctx => accessor.HttpContext!;

    /// <summary>Vrai pour l'interface anglaise (adresses <c>/en/…</c>).</summary>
    public bool Anglais => Ctx.Items[Cle_Items] as string == "en";
    /// <summary>« fr » ou « en » (attribut <c>lang</c>, balises <c>hreflang</c>).</summary>
    public string Code => Anglais ? "en" : "fr";
    /// <summary>Culture pour les dates et les nombres (jamais la culture du serveur).</summary>
    public CultureInfo Culture => Anglais ? En : Fr;

    /// <summary>Texte dans la langue courante (texte simple : il sera encodé à l'affichage).</summary>
    public string T(string francais, string anglais) => Anglais ? anglais : francais;

    /// <summary>Texte contenant du HTML écrit par nous (liens, gras) : affiché tel quel. Jamais pour une donnée venant de la base ou de l'URL.</summary>
    public HtmlString TH(string francais, string anglais) => new(Anglais ? anglais : francais);

    /// <summary>Adresse interne (« /lois ») avec le préfixe de la langue courante.</summary>
    public string Lien(string chemin) => Anglais ? "/en" + (chemin == "/" ? "" : chemin) : chemin;

    /// <summary>Même page dans l'autre langue (sélecteur FR | EN et balises hreflang), paramètres de l'adresse conservés.</summary>
    public string AutreLangue => Variante(!Anglais);

    /// <summary>Adresse de la page dans la langue demandée (true = anglais).</summary>
    public string Variante(bool anglais)
    {
        var chemin = Ctx.Request.Path.Value ?? "/";   // sans le préfixe /en (déplacé dans PathBase)
        var requete = Ctx.Request.QueryString.Value ?? "";
        return anglais ? "/en" + (chemin == "/" ? "" : chemin) + requete : chemin + requete;
    }

    /// <summary>
    /// Middleware à placer AVANT le routage et les fichiers statiques : <c>/en/lois</c> devient chemin <c>/lois</c> avec PathBase <c>/en</c>.
    /// /Admin et /Identity restent en français seulement (pas de version anglaise).
    /// </summary>
    public static async Task Middleware(HttpContext ctx, Func<Task> suite)
    {
        if (ctx.Request.Path.StartsWithSegments("/en", out var reste)
            && !reste.StartsWithSegments("/Admin", StringComparison.OrdinalIgnoreCase)
            && !reste.StartsWithSegments("/Identity", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Request.PathBase = ctx.Request.PathBase.Add("/en");
            ctx.Request.Path = reste.HasValue ? reste : new PathString("/");
            ctx.Items[Cle_Items] = "en";
        }
        await suite();
    }
}
