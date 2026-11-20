using System.Net;
using Dapper;
using IlsOntVote.Web.Data;

namespace IlsOntVote.Web.Services;

/// <summary>Un pays publié : son code (ISO), son préfixe d'adresse (« uk ») et ses noms.</summary>
public sealed record PaysActif(string Code, string Prefixe, string NomFr, string NomEn);

/// <summary>
/// Pays du site : la France à la racine (sans préfixe), et chaque autre pays ACTIF sous son préfixe (<c>/uk/…</c>, table <c>pays</c>, migration 031).
/// Comme pour la langue (<see cref="Langue"/>), le préfixe est retiré de l'adresse par <see cref="Middleware"/> et placé dans <c>PathBase</c> : le routage
/// voit les mêmes pages et les liens générés gardent le préfixe. Tant qu'aucun pays autre que la France n'est actif, le middleware ne fait RIEN.
/// Règles :
/// - sous un préfixe de pays, l'interface est en ANGLAIS (seules langues d'interface : fr et en ; pas de préfixe /en en plus) ;
/// - choix explicite (<c>?pays=uk|fr</c>, sélecteur de l'en-tête) mémorisé dans un cookie fonctionnel : il prime sur la détection ;
/// - détection : UNIQUEMENT sur la page d'accueil, une fois (302, jamais 301), sans cookie, pas pour les robots, si le pays de l'adresse IP est actif.
///   Les adresses profondes (liens partagés, résultats de recherche) ne sont jamais déviées.
/// </summary>
public sealed class PaysSite(DapperContext db)
{
    public const string CleItems = "pays";
    public const string CookiePays = "ilsontvote_pays";
    private static readonly TimeSpan Duree = TimeSpan.FromMinutes(5);

    /// <summary>Une chambre telle que décrite dans la table <c>chambres</c> : son pays (FR, EU, GB…), si elle est publiée, et la disposition de son hémicycle.</summary>
    public sealed record ChambreInfo(string Code, string Pays, bool Actif, string Disposition);

    private IReadOnlyList<ChambreInfo> _chambres = [];
    private IReadOnlyList<PaysActif> _actifs = [];
    private DateTime _charge = DateTime.MinValue;

    /// <summary>Pays actifs avec préfixe (copie en mémoire, rafraîchie toutes les 5 minutes). La France n'en fait pas partie (racine du site).</summary>
    public IReadOnlyList<PaysActif> Actifs => _actifs;

    /// <summary>Force le rechargement au prochain appel : la page d'activation de /Admin le demande après chaque changement (sinon 5 minutes d'attente).</summary>
    public void Invalider() => _charge = DateTime.MinValue;

    public async Task ChargerAsync()
    {
        if (DateTime.UtcNow - _charge < Duree) return;
        try
        {
            using var cx = db.CreateConnection();
            // Table ou colonne absente (migration 031 pas encore appliquée) : aucun pays, le site reste tel quel.
            if (!await cx.ExecuteScalarAsync<bool>("SELECT to_regclass('public.pays') IS NOT NULL AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'pays' AND column_name = 'prefixe_url')"))
            { _actifs = []; _chambres = []; _charge = DateTime.UtcNow; return; }
            _actifs = (await cx.QueryAsync<PaysActif>(
                "SELECT code, prefixe_url AS prefixe, nom_fr, nom_en FROM pays WHERE actif AND prefixe_url IS NOT NULL ORDER BY ordre")).ToList();
            _chambres = (await cx.QueryAsync<ChambreInfo>("SELECT code, pays, actif, disposition FROM chambres ORDER BY ordre")).ToList();
            _charge = DateTime.UtcNow;
        }
        catch (Exception) { /* base indisponible : on garde la liste connue, réessai au prochain appel */ }
    }

    /// <summary>
    /// Chambres dont les données s'affichent sur le site d'un pays : la France (null) montre ses chambres et le Parlement européen ; un pays à préfixe (« GB ») ne montre
    /// QUE les siennes. Garde-fou : les données d'un pays ne doivent jamais apparaître sur le site d'un autre (ni listes, ni fiches, ni plan du site).
    /// Sans table « chambres » (base ancienne) : les trois chambres historiques.
    /// </summary>
    public string[] ChambresVisibles(string? paysSite)
    {
        if (_chambres.Count == 0) return paysSite is null ? ["AN", "SENAT", "UE"] : [];
        return _chambres.Where(c => paysSite is null ? c.Pays is "FR" or "EU" : c.Pays == paysSite).Select(c => c.Code).ToArray();
    }

    /// <summary>
    /// Pays à préfixe pour lesquels le site propose les pages britanniques-seulement (projets de loi, pétitions) : le Royaume-Uni. Les autres pays (États-Unis…) n'ont que les
    /// pages communes (votes, élus, statistiques, données).
    /// </summary>
    public static bool APetitionsEtProjets(string? paysSite) => paysSite == "GB";

    /// <summary>
    /// Plateformes de pétitions visibles sur le site d'un pays (colonne petitions.chambre : AN, SENAT, UK). La France montre les plateformes françaises ; /uk celle du Parlement britannique.
    /// Même garde-fou que pour les chambres : les pétitions d'un pays ne paraissent jamais sur le site d'un autre.
    /// </summary>
    public static string[] PlateformesPetitions(string? paysSite) => paysSite is null ? ["AN", "SENAT"] : paysSite == "GB" ? ["UK"] : [];

    /// <summary>Disposition de l'hémicycle d'une chambre (« hemicycle » ou « face-a-face ») ; « hemicycle » par défaut.</summary>
    public string Disposition(string chambre) => _chambres.FirstOrDefault(c => c.Code == chambre)?.Disposition ?? "hemicycle";

    /// <summary>Pays dont le préfixe est le premier segment de l'adresse (« /uk/lois » -> UK, reste « /lois ») ; aucun si l'adresse n'en porte pas. Segment entier : « /ukraine » ne correspond pas.</summary>
    public static (PaysActif? Pays, PathString Reste) Correspond(IReadOnlyList<PaysActif> actifs, PathString chemin)
    {
        foreach (var p in actifs)
            if (chemin.StartsWithSegments("/" + p.Prefixe, StringComparison.OrdinalIgnoreCase, out var reste))
                return (p, reste.HasValue ? reste : new PathString("/"));
        return (null, chemin);
    }

    /// <summary>Faux pour ce qui n'a pas de version par pays : administration, comptes, images d'aperçu, fichiers (robots, sitemaps, CSS…).</summary>
    public static bool EstPageDuSite(PathString chemin) =>
        !chemin.StartsWithSegments("/Admin", StringComparison.OrdinalIgnoreCase)
        && !chemin.StartsWithSegments("/Identity", StringComparison.OrdinalIgnoreCase)
        && !chemin.StartsWithSegments("/apercu")
        && !(chemin.Value ?? "").Contains('.');

    /// <summary>Robot d'indexation ou client sans identité : jamais dévié (il doit voir l'adresse canonique).</summary>
    public static bool EstRobot(string? userAgent) =>
        string.IsNullOrWhiteSpace(userAgent)
        || new[] { "bot", "crawl", "spider", "slurp", "facebookexternalhit", "preview", "monitor", "curl", "wget" }
            .Any(m => userAgent.Contains(m, StringComparison.OrdinalIgnoreCase));

    /// <summary>Middleware à placer AVANT <see cref="Langue.Middleware"/> : lit le préfixe de pays, applique le choix explicite et la détection de l'accueil.</summary>
    /// <summary>Cookie de la langue choisie sous un préfixe de pays (« fr » ou « en »).</summary>
    /// <summary>Langues proposées par le sélecteur d'un pays : français et anglais partout, plus la langue du pays quand l'interface est traduite (Italie : « it »).</summary>
    public static IReadOnlyList<string> LanguesDuPays(string? pays) => pays == "IT" ? ["fr", "en", "it"] : ["fr", "en"];

    /// <summary>Langue de l'interface sans choix du lecteur : la langue du pays si elle est traduite (Italie), anglais sinon.</summary>
    public static string LangueParDefaut(string? pays) => pays == "IT" ? "it" : "en";

    public const string CookieLanguePays = "ilsontvote_lang_pays";

    public static async Task Middleware(HttpContext ctx, Func<Task> suite)
    {
        var site = ctx.RequestServices.GetRequiredService<PaysSite>();
        await site.ChargerAsync();
        var actifs = site.Actifs;
        var req = ctx.Request;
        if (actifs.Count == 0) { await suite(); return; }
        var lecture = HttpMethods.IsGet(req.Method) || HttpMethods.IsHead(req.Method);

        // 0. Préfixe de pays sur un FICHIER (/uk/css/site.css, /uk/favicon.svg…) : retiré aussi, comme /en : la page du pays les référence avec son préfixe (PathBase).
        //    Administration et comptes n'ont pas de version par pays.
        if ((req.Path.Value ?? "").Contains('.'))
        {
            var (paysFichier, resteFichier) = Correspond(actifs, req.Path);
            if (paysFichier is not null)
            {
                req.PathBase = req.PathBase.Add("/" + paysFichier.Prefixe);
                req.Path = resteFichier;
                // Pays aussi pour les ressources dynamiques à extension (images d'aperçu /uk/apercu/scrutin/x.png) : elles lisent les données du site de ce pays.
                ctx.Items[CleItems] = paysFichier.Code;
                ctx.Items["lang"] = "en";
            }
            await suite();
            return;
        }
        if (!EstPageDuSite(req.Path)) { await suite(); return; }

        // 1. Choix explicite : cookie, puis redirection vers l'accueil du pays choisi (« fr » = la racine).
        var choix = req.Query["pays"].ToString().ToLowerInvariant();
        if (lecture && choix.Length > 0)
        {
            var cible = actifs.FirstOrDefault(p => p.Prefixe == choix);
            if (cible is not null || choix == "fr")
            {
                ctx.Response.Cookies.Append(CookiePays, cible?.Prefixe ?? "fr", new CookieOptions
                { MaxAge = TimeSpan.FromDays(365), SameSite = SameSiteMode.Lax, Secure = req.IsHttps, HttpOnly = true, IsEssential = true });
                ctx.Response.Redirect(cible is null ? "/" : "/" + cible.Prefixe);
                return;
            }
        }

        // 2. Préfixe de pays dans l'adresse : retiré et placé dans PathBase ; interface en anglais.
        var (pays, reste) = Correspond(actifs, req.Path);
        if (pays is not null)
        {
            req.PathBase = req.PathBase.Add("/" + pays.Prefixe);
            req.Path = reste;
            ctx.Items[CleItems] = pays.Code;
            // Langue de l'interface : anglaise par défaut (le site d'un pays est écrit pour ses lecteurs), française si le lecteur l'a choisi (?hl=fr, cookie propre au pays :
            // distinct du cookie de langue du site français, sinon un lecteur français sur la racine verrait /uk en français sans l'avoir demandé).
            var hl = req.Query["hl"].ToString();
            if (lecture && LanguesDuPays(pays.Code).Contains(hl))
            {
                ctx.Response.Cookies.Append(CookieLanguePays, hl, new CookieOptions
                { MaxAge = TimeSpan.FromDays(365), SameSite = SameSiteMode.Lax, Secure = req.IsHttps, HttpOnly = true, IsEssential = true });
                var autres = req.Query.Where(q => q.Key != "hl").Select(q => $"{Uri.EscapeDataString(q.Key)}={Uri.EscapeDataString(q.Value.ToString())}");
                var rq = string.Join('&', autres);
                var cible = (req.PathBase + req.Path).Value + (rq.Length > 0 ? "?" + rq : "");
                ctx.Response.Redirect(cible);
                return;
            }
            // Cookie invalide pour CE pays (« it » mémorisé sous /it, puis lu sous /uk) : on retombe sur la langue par défaut du pays.
            var choisie = req.Cookies[CookieLanguePays];
            ctx.Items["lang"] = choisie is not null && LanguesDuPays(pays.Code).Contains(choisie) ? choisie : LangueParDefaut(pays.Code);
            await suite();
            return;
        }

        // 3. Détection, accueil seulement : pays de l'adresse IP actif, aucun cookie, pas un robot.
        if (lecture && req.Path == "/" && !req.Cookies.ContainsKey(CookiePays) && !EstRobot(req.Headers.UserAgent.ToString()))
        {
            var iso = ctx.RequestServices.GetRequiredService<PaysResolver>().Resolve(ctx.Connection.RemoteIpAddress);
            var detecte = iso is null ? null : actifs.FirstOrDefault(p => string.Equals(p.Code, iso, StringComparison.OrdinalIgnoreCase));
            if (detecte is not null)
            {
                ctx.Response.Headers.CacheControl = "private, no-store";
                ctx.Response.Redirect("/" + detecte.Prefixe);   // 302 : provisoire, le visiteur peut choisir un autre pays
                return;
            }
        }
        await suite();
    }
}
