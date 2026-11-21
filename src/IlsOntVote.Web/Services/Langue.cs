using IlsOntVote.Web.Models;
using System.Globalization;
using Microsoft.AspNetCore.Html;

namespace IlsOntVote.Web.Services;

/// <summary>
/// Langue de la requête : français (racine du site) ou anglais (préfixe <c>/en</c>). Le préfixe est retiré de l'adresse par
/// <see cref="Langue.Middleware"/> et placé dans <c>PathBase</c> : le routage voit les mêmes pages dans les deux langues, et les liens
/// générés par ASP.NET gardent le préfixe. Les traductions s'écrivent À CÔTÉ du français, dans les pages : <c>@L.T("lois-b08d", "Lois", "Laws")</c>.
/// Les DONNÉES (titres de lois, noms de groupes) restent en français : ce sont les sources officielles.
/// </summary>
public sealed class Langue(IHttpContextAccessor accessor, Traductions? traductions = null)
{
    private const string Cle_Items = "lang";
    private static readonly CultureInfo Fr = new("fr-FR");
    private static readonly CultureInfo En = new("en-GB");
    /// <summary>Cultures des langues supplémentaires (dates, nombres) : à compléter avec la langue.</summary>
    private static readonly Dictionary<string, CultureInfo> AutresCultures = new() { ["es"] = new("es-ES"), ["it"] = new("it-IT"), ["de"] = new("de-DE") };

    private HttpContext Ctx => accessor.HttpContext!;

    /// <summary>
    /// Vrai pour toute interface NON française (adresses <c>/en/…</c>, mais aussi l'italien sous /it) : les pages qui écrivent « Anglais ? x : y » en dur prennent la branche anglaise
    /// pour une langue traduite par la table (elles ne sauraient pas afficher du français à un lecteur italien). Pour tester la langue exacte : <see cref="Code"/>.
    /// </summary>
    // Suisse : l'anglais n'est pas une langue du pays (choix de Thomas) ; allemand et italien retombent sur le FRANÇAIS tant que le texte n'est pas traduit : jamais de branche anglaise.
    public bool Anglais => Pays != "CH" && Code != "fr";
    /// <summary>Code de la langue de la page : « fr », « en », ou une langue ajoutée dans la table de traduction (« es », « it »…). Attribut <c>lang</c>, balises <c>hreflang</c>.</summary>
    public string Code => Ctx.Items[Cle_Items] as string is { Length: > 0 } c ? c : "fr";
    /// <summary>Culture pour les dates et les nombres (jamais la culture du serveur).</summary>
    public CultureInfo Culture => Code switch { "fr" => Fr, "en" => En, _ => AutresCultures.GetValueOrDefault(Code, En) };

    /// <summary>
    /// Texte dans la langue courante (texte simple : il sera encodé à l'affichage). <paramref name="cle"/> = identifiant STABLE du texte (jamais dérivé de la phrase : retoucher le français ou
    /// l'anglais ne casse pas les traductions). Ordre : traduction de la table (langue, clé), puis le texte du code (français, anglais) ; pour une autre langue sans traduction, l'anglais
    /// s'affiche et le texte est signalé « à traduire » dans /Admin/Traductions.
    /// </summary>
    public string T(string cle, string francais, string anglais)
    {
        var code = Code;
        if (traductions is null) return code == "fr" ? francais : anglais;
        var t = traductions.Get(code, cle, francais);
        if (t is not null) return t;
        if (code is "fr") return francais;
        if (code is not "en") traductions.Manque(code, cle, francais, anglais);
        return Pays == "CH" ? francais : anglais;
    }

    /// <summary>Texte contenant du HTML écrit par nous (liens, gras) : affiché tel quel. Jamais pour une donnée venant de la base ou de l'URL.</summary>
    public HtmlString TH(string cle, string francais, string anglais) => new(T(cle, francais, anglais));

    /// <summary>
    /// Texte à valeurs (« {0} votes ») : la clé désigne le MODÈLE, jamais le texte final, pour que la traduction soit la même quelle que soit la valeur. Mise en forme dans la culture de la
    /// langue (séparateurs de milliers, dates). Texte simple : encodé à l'affichage.
    /// </summary>
    public string Tf(string cle, string francais, string anglais, params object?[] valeurs) => string.Format(Culture, T(cle, francais, anglais), valeurs);

    /// <summary>Comme <see cref="Tf"/> pour un modèle contenant du HTML écrit par nous ; les valeurs insérées doivent être SÛRES (adresses internes, nombres) : elles ne sont pas encodées.</summary>
    public HtmlString THf(string cle, string francais, string anglais, params object?[] valeurs) => new(Tf(cle, francais, anglais, valeurs));

    // Libellés issus de données (noms de chambres, positions, thèmes, pays) : le texte français / anglais vient du code ou de la base, la clé est FIXE et dérive du code de la donnée
    // (« chambre.SENAT.nom »), jamais du libellé.
    /// <summary>Nom du site dans la langue courante (« Ils ont voté », « They voted », « Hanno votato », « Sie haben abgestimmt »).</summary>
    public string Marque => Pays == "CH" ? Code switch { "de" => "Sie haben abgestimmt", "it" => "Hanno votato", _ => "Ils ont voté" }
        : Code == "it" ? "Hanno votato" : Pays is not null ? "They voted" : "Ils ont voté";

    /// <summary>La marque avec le dernier mot en rouge (<c>&lt;em&gt;</c>), comme dans l'en-tête du site.</summary>
    public HtmlString MarqueHtml
    {
        get
        {
            var m = Marque; var i = m.LastIndexOf(' ');
            return new HtmlString(System.Net.WebUtility.HtmlEncode(m[..(i + 1)]) + "<em>" + System.Net.WebUtility.HtmlEncode(m[(i + 1)..]) + "</em>");
        }
    }

    /// <summary>Nom d'une chambre (« Sénat », « Chambre des Communes »).</summary>
    public string Chambre(string code) => T("chambre." + code + ".nom", Chambres.Nom(code), Chambres.Nom(code, true));
    /// <summary>Un membre de la chambre au singulier (« sénateur », « MP »).</summary>
    public string Membre(string code) => T("chambre." + code + ".membre", Chambres.Membre(code), Chambres.Membre(code, true));
    /// <summary>Les membres de la chambre au pluriel (« sénateurs », « MPs »).</summary>
    public string Membres(string code) => T("chambre." + code + ".membres", Chambres.Membres(code), Chambres.Membres(code, true));
    /// <summary>Sigle court de la chambre pour les étiquettes de tableau.</summary>
    public string ChambreCourt(string code) => T("chambre." + code + ".court", Chambres.Court(code), Chambres.Court(code, true));
    /// <summary>Libellé d'une position de vote (« Pour », « Contre »…).</summary>
    public string Position(string position) => T("position." + position, Pos.Libelle(position), Pos.Libelle(position, true));
    /// <summary>Nom d'un thème de la liste de filtrage.</summary>
    public string Theme(Theme theme) => T("theme." + theme.Code, theme.NomFr, theme.NomEn);
    /// <summary>Nom d'un pays (sélecteur d'en-tête) ; la clé dérive du code ISO.</summary>
    public string NomPays(string code, string francais, string anglais) => T("pays." + code, francais, anglais);

    /// <summary>Adresse interne (« /lois ») avec le préfixe de la langue courante.</summary>
    public string Lien(string chemin)
    {
        // PathBase porte déjà le préfixe courant (« /en », « /uk » ou rien) : le middleware l'y a déplacé. Même résultat qu'avant pour fr et en.
        var b = Ctx.Request.PathBase.Value ?? "";
        return chemin == "/" ? (b.Length == 0 ? "/" : b) : b + chemin;
    }

    /// <summary>Code ISO du pays de la page (« GB »), ou null pour la France (racine du site). Voir <see cref="PaysSite"/>.</summary>
    public string? Pays => Ctx.Items[PaysSite.CleItems] as string;

    /// <summary>Même page dans l'autre langue (sélecteur FR | EN et balises hreflang), paramètres de l'adresse conservés.</summary>
    public string AutreLangue => Variante(!Anglais);

    private const string Cookie_Langue = "ilsontvote_lang";

    /// <summary>
    /// Langue par défaut : français pour un navigateur dont la première langue est le français, anglais sinon. Appliquée UNE fois (302) sur
    /// les pages de la racine ; un choix explicite (<c>?hl=fr|en</c> du sélecteur) est mémorisé dans un cookie fonctionnel et prime ensuite.
    /// Sans en-tête Accept-Language (robots d'indexation) : français, l'adresse canonique. Renvoie vrai si la réponse est déjà faite.
    /// </summary>
    private static Task<bool> ChoixLangue(HttpContext ctx)
    {
        // Pays hors France (/uk…) : interface en anglais imposée par PaysSite, aucune redirection de langue.
        if (ctx.Items.ContainsKey(PaysSite.CleItems)) return Task.FromResult(false);
        var req = ctx.Request;
        if (!HttpMethods.IsGet(req.Method) && !HttpMethods.IsHead(req.Method)) return Task.FromResult(false);
        var chemin = req.Path;
        // Exclus : version anglaise elle-même, administration, comptes, images d'aperçu, fichiers (robots, sitemaps, CSS…).
        if (chemin.StartsWithSegments("/en") || chemin.StartsWithSegments("/Admin", StringComparison.OrdinalIgnoreCase)
            || chemin.StartsWithSegments("/Identity", StringComparison.OrdinalIgnoreCase) || chemin.StartsWithSegments("/apercu")
            || (chemin.Value ?? "").Contains('.')) return Task.FromResult(false);

        // Choix explicite : cookie, puis adresse nettoyée du paramètre hl.
        var hl = req.Query["hl"].ToString();
        if (hl is "fr" or "en")
        {
            ctx.Response.Cookies.Append(Cookie_Langue, hl, new CookieOptions
            { MaxAge = TimeSpan.FromDays(365), SameSite = SameSiteMode.Lax, Secure = req.IsHttps, HttpOnly = true, IsEssential = true });
            var autres = req.Query.Where(q => q.Key != "hl").Select(q => $"{Uri.EscapeDataString(q.Key)}={Uri.EscapeDataString(q.Value.ToString())}");
            var q = string.Join('&', autres);
            var cible = (hl == "en" ? "/en" + (chemin == "/" ? "" : chemin.Value) : chemin.Value) + (q.Length > 0 ? "?" + q : "");
            ctx.Response.Redirect(cible.Length == 0 ? "/en" : cible);
            return Task.FromResult(true);
        }
        if (req.Cookies.ContainsKey(Cookie_Langue)) return Task.FromResult(false);

        // Pas de choix mémorisé : on suit la langue du navigateur (la réponse dépend donc de Accept-Language).
        ctx.Response.Headers.Vary = "Accept-Language";
        if (!PrefereAnglais(req.Headers.AcceptLanguage.ToString())) return Task.FromResult(false);
        ctx.Response.Redirect("/en" + (chemin == "/" ? "" : chemin.Value) + req.QueryString.Value);
        return Task.FromResult(true);
    }

    /// <summary>Vrai si la langue la plus prioritaire de l'en-tête n'est pas le français (« fr-FR,fr;q=0.9,en;q=0.8 » -> faux). En-tête absent -> faux.</summary>
    public static bool PrefereAnglais(string entete)
    {
        string? meilleure = null; var qMax = -1.0;
        foreach (var part in entete.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var morceaux = part.Split(';', 2);
            var q = 1.0;
            if (morceaux.Length == 2 && morceaux[1].Trim().StartsWith("q=", StringComparison.OrdinalIgnoreCase))
                double.TryParse(morceaux[1].Trim()[2..], System.Globalization.NumberStyles.Float, CultureInfo.InvariantCulture, out q);
            if (q > qMax && morceaux[0] != "*") { qMax = q; meilleure = morceaux[0]; }
        }
        return meilleure is not null && !meilleure.StartsWith("fr", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Adresse de la page dans la langue demandée (true = anglais).</summary>
    public string Variante(bool anglais)
    {
        // Sous un préfixe de pays la langue ne change pas l'adresse (cookie) : la même page, le sélecteur ajoute « ?hl= ».
        if (Pays is not null) return (Ctx.Request.PathBase + Ctx.Request.Path).Value + (Ctx.Request.QueryString.Value ?? "");
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
        if (await ChoixLangue(ctx)) return;
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
