using System.Text.RegularExpressions;
using IlsOntVote.Web.Repositories;
using IlsOntVote.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IlsOntVote.Web.Middleware;

/// <summary>
/// Enregistre une vue par Razor Page GET réussie (200), hors /Admin et /Identity (navigation de
/// l'admin et flux d'authentification ne sont pas du trafic public). Placé après UseRouting
/// (l'endpoint doit être résolu) et AVANT le cache de sortie : une page servie depuis le cache
/// compte aussi. Vie privée : ni IP ni User-Agent en clair, cf. <see cref="IIpHasher"/>.
/// </summary>
public sealed class PageViewTrackingMiddleware(RequestDelegate next)
{
    // Crawlers/outils qui s'annoncent comme tels — ne détecte pas les bots furtifs.
    private static readonly Regex BotUserAgent = new(
        "bot|crawler|spider|slurp|crawl|archive[.]org|python-requests|python-urllib|curl|wget|scrapy|" +
        "headless|phantomjs|node-fetch|axios|go-http-client|java/|libwww-perl",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task InvokeAsync(HttpContext context, IPageViewRepository vues, IIpHasher hasher, PaysResolver pays, ReseauResolver reseau)
    {
        await next(context);

        if (!DoitEtreSuivi(context)) return;
        // Choix du visiteur respecté : DNT: 1 ou Sec-GPC: 1 = visite non comptée (ni page, ni pays, ni empreinte).
        if (context.Request.Headers["DNT"] == "1" || context.Request.Headers["Sec-GPC"] == "1") return;

        // Chemin complet avec le préfixe /en : les statistiques distinguent les visites des deux langues.
        var path = context.Request.PathBase.Add(context.Request.Path).Value ?? "/";
        var userAgent = context.Request.Headers.UserAgent.ToString();
        // Robot annoncé (User-Agent) OU adresse d'un hébergeur cloud (collecteur déguisé en navigateur) : classé robot, jamais bloqué.
        var estBot = string.IsNullOrWhiteSpace(userAgent) || BotUserAgent.IsMatch(userAgent) || reseau.EstHebergeur(context.Connection.RemoteIpAddress);

        // Best-effort : une erreur de statistiques ne doit jamais casser une page déjà servie.
        try
        {
            await vues.RecordAsync(path.Length > 300 ? path[..300] : path, hasher.Hash(context.Connection.RemoteIpAddress), estBot,
                pays.Resolve(context.Connection.RemoteIpAddress));
        }
        catch (Exception ex)
        {
            context.RequestServices.GetRequiredService<ILogger<PageViewTrackingMiddleware>>()
                .LogWarning(ex, "Échec d'enregistrement de la vue de page pour {Path}", path);
        }
    }

    private static bool DoitEtreSuivi(HttpContext context)
    {
        if (context.Request.Method != HttpMethods.Get || context.Response.StatusCode != StatusCodes.Status200OK) return false;

        var path = context.Request.Path.Value ?? "";
        if (path.StartsWith("/Admin", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/Identity", StringComparison.OrdinalIgnoreCase)) return false;

        return context.GetEndpoint()?.Metadata.GetMetadata<PageActionDescriptor>() is not null;
    }
}
