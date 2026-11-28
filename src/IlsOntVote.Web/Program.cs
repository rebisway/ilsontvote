using System.Threading.RateLimiting;
using Dapper;
using IlsOntVote.Web.Data;
using IlsOntVote.Web.Mcp;
using IlsOntVote.Web.Middleware;
using IlsOntVote.Web.Repositories;
using IlsOntVote.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

// « check » dit ce qui manque à l'installation (secrets, base, schéma, données, administrateur) et se termine par 1
// si quelque chose manque : le script de mise à jour le lance après chaque démarrage.
if (args.Length > 0 && args[0] == "check")
{
    Environment.ExitCode = await IlsOntVote.Web.SiteChecks.RunAsync();
    return;
}

var builder = WebApplication.CreateBuilder(args);

// Les secrets de production (chaîne de connexion, administrateur initial) vivent dans appsettings.Local.json, à côté de la dll :
// jamais publié, jamais dans le dépôt (modèle : deploy/appsettings.Local.example.json). Ajouté AVANT toute lecture de la
// configuration, sinon la chaîne de connexion ci-dessous serait introuvable.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

// Dapper : colonnes snake_case -> propriétés PascalCase.
DefaultTypeMap.MatchNamesWithUnderscores = true;

// --- Identity (EF Core) — auth/droits UNIQUEMENT -----------------------------------
var cleConnexion = builder.Environment.IsDevelopment() ? "PostgreSQL_Debug" : "PostgreSQL_Release";
var connexion = builder.Configuration.GetConnectionString(cleConnexion)
    ?? throw new InvalidOperationException($"Connection string '{cleConnexion}' introuvable.");

builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseNpgsql(connexion));

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        // Pas d'inscription publique : les comptes sont créés par un administrateur.
        options.SignIn.RequireConfirmedAccount = false;
        options.Password.RequiredLength = 12;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Identity/Account/Login";
    options.LogoutPath = "/Identity/Account/Logout";
    options.AccessDeniedPath = "/Identity/Account/AccessDenied";
    options.Cookie.Name = ".ilsontvote.auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromDays(14);
    options.SlidingExpiration = true;
});

builder.Services.AddAuthorization(o => o.AddPolicy("AdminOnly", p => p.RequireRole("Admin")));

// --- Dapper — tout le reste ---------------------------------------------------------
builder.Services.AddSingleton<DapperContext>();
builder.Services.AddSingleton<Themes>();
builder.Services.AddSingleton<Traductions>();
builder.Services.AddScoped<IScrutinRepository, ScrutinRepository>();
builder.Services.AddScoped<IParlementaireRepository, ParlementaireRepository>();
builder.Services.AddScoped<ITexteRepository, TexteRepository>();
builder.Services.AddScoped<IBillRepository, BillRepository>();
builder.Services.AddScoped<IVotationRepository, VotationRepository>();
builder.Services.AddScoped<IPageViewRepository, PageViewRepository>();
builder.Services.AddScoped<IAdminRepository, AdminRepository>();
builder.Services.AddScoped<IQualiteRepository, QualiteRepository>();
builder.Services.AddScoped<IMcpAppelRepository, McpAppelRepository>();
// Données ouvertes : dossier de l'export nocturne (Export:Dir, ou ILSONVOTE_EXPORT_DIR, ou /var/lib/ilsontvote-export en production).
builder.Services.AddSingleton(new DonneesOuvertes(
    builder.Configuration["Export:Dir"] ?? Environment.GetEnvironmentVariable("ILSONVOTE_EXPORT_DIR") ?? "/var/lib/ilsontvote-export"));
builder.Services.AddSingleton<IIpHasher, IpHasher>();
builder.Services.AddSingleton<PaysResolver>();
builder.Services.AddSingleton<ReseauResolver>();
builder.Services.AddSingleton<ApercuImage>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<Langue>();
builder.Services.AddSingleton<PaysSite>();
builder.Services.AddScoped<IStatistiquesRepository, StatistiquesRepository>();
builder.Services.AddScoped<IPetitionRepository, PetitionRepository>();
builder.Services.AddHostedService<PurgeVuesService>();
// Journal consultable dans /Admin/Journal (appels MCP, avertissements, erreurs), écrit en arrière-plan.
builder.Services.AddSingleton<JournalSite>();
builder.Services.AddSingleton<ILoggerProvider>(s => s.GetRequiredService<JournalSite>());
builder.Services.AddHostedService<EcritureJournalService>();

// nginx en reverse proxy devant l'appli : sans ça, l'IP vue serait toujours celle de nginx.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Clés de protection des données (cookies) : emplacement explicite, www-data sous systemd n'a pas de HOME.
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys")));

// Anti-force-brute de la connexion : 10 essais par minute et par IP.
builder.Services.AjouterMcp();   // Serveur MCP (assistants IA), lecture seule : Mcp/McpServeur.cs

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "inconnue",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/Admin", "AdminOnly");

    // Une seule page pour les deux chambres : la chambre se déduit de l'URL.
    options.Conventions.AddPageRoute("/Parlementaires", "/deputes");
    options.Conventions.AddPageRoute("/Parlementaires", "/senateurs");
    options.Conventions.AddPageRoute("/Parlementaire", "/depute/{slug}");
    options.Conventions.AddPageRoute("/Parlementaire", "/senateur/{slug}");
    options.Conventions.AddPageRoute("/Parlementaires", "/eurodeputes");
    options.Conventions.AddPageRoute("/Parlementaire", "/eurodepute/{slug}");
    options.Conventions.AddPageRoute("/Parlementaires", "/members");
    options.Conventions.AddPageRoute("/Parlementaire", "/member/{slug}");
    options.Conventions.AddPageRoute("/Parlementaires", "/lords");
    options.Conventions.AddPageRoute("/Parlementaire", "/lord/{slug}");
    options.Conventions.AddPageRoute("/Parlementaires", "/representatives");
    options.Conventions.AddPageRoute("/Parlementaire", "/representative/{slug}");
    options.Conventions.AddPageRoute("/Parlementaires", "/senators");
    options.Conventions.AddPageRoute("/Parlementaire", "/senator/{slug}");
    options.Conventions.AddPageRoute("/Parlementaires", "/deputies");
    options.Conventions.AddPageRoute("/Parlementaire", "/deputy/{slug}");
    options.Conventions.AddPageRoute("/Parlementaires", "/senatori");
    options.Conventions.AddPageRoute("/Parlementaire", "/senatore/{slug}");
    options.Conventions.AddPageRoute("/Parlementaires", "/mps");
    options.Conventions.AddPageRoute("/Parlementaire", "/mp/{slug}");
    options.Conventions.AddPageRoute("/Parlementaires", "/canadian-senators");
    options.Conventions.AddPageRoute("/Parlementaire", "/canadian-senator/{slug}");
    options.Conventions.AddPageRoute("/Parlementaires", "/tds");
    options.Conventions.AddPageRoute("/Parlementaire", "/td/{slug}");
    options.Conventions.AddPageRoute("/Parlementaires", "/seanad");
    options.Conventions.AddPageRoute("/Parlementaire", "/seanadoir/{slug}");
    options.Conventions.AddPageRoute("/Parlementaires", "/conseillers-nationaux");
    options.Conventions.AddPageRoute("/Parlementaire", "/conseiller-national/{slug}");
    options.Conventions.AddPageRoute("/Parlementaires", "/conseillers-etats");
    options.Conventions.AddPageRoute("/Parlementaire", "/conseiller-etats/{slug}");
});

// Les données ne changent qu'après un import : un cache de quelques minutes suffit. JAMAIS de cache
// pour /Admin et /Identity : une page d'administrateur mise en cache serait servie à n'importe qui.
builder.Services.AddOutputCache(options =>
{
    options.AddBasePolicy(b => b
        .With(c => !(c.HttpContext.Request.Path.StartsWithSegments("/Admin", StringComparison.OrdinalIgnoreCase)
                  || c.HttpContext.Request.Path.StartsWithSegments("/Identity", StringComparison.OrdinalIgnoreCase)
                  || c.HttpContext.Request.Path.StartsWithSegments(McpServeur.Chemin)
                  || c.HttpContext.Request.Path.StartsWithSegments("/donnees/fichier", StringComparison.OrdinalIgnoreCase)))   // gros fichiers : jamais en cache mémoire
        .Expire(builder.Environment.IsDevelopment() ? TimeSpan.FromSeconds(1) : TimeSpan.FromMinutes(5))
        .SetVaryByQuery("*")
        .VaryByValue(c => new KeyValuePair<string, string>("lang", c.Items["lang"] as string ?? "fr")));
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// --- Rôle Admin + compte administrateur initial -------------------------------------
// AdminBootstrapEmail + AdminBootstrapPassword (appsettings.*.json ignoré par Git, ou variables
// d'environnement en production) : crée le compte s'il n'existe pas et le promeut Admin.
// À retirer de la configuration une fois le compte en place.
using (var scope = app.Services.CreateScope())
{
    var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    if (!await roles.RoleExistsAsync("Admin")) await roles.CreateAsync(new IdentityRole("Admin"));

    var email = app.Configuration["AdminBootstrapEmail"];
    var motDePasse = app.Configuration["AdminBootstrapPassword"];
    if (!string.IsNullOrWhiteSpace(email))
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync(email);
        if (user is null && !string.IsNullOrEmpty(motDePasse))
        {
            user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, CreatedAt = DateTime.UtcNow };
            var creation = await users.CreateAsync(user, motDePasse);
            if (!creation.Succeeded)
            {
                app.Logger.LogError("Création de l'administrateur initial impossible : {Erreurs}",
                    string.Join(" ; ", creation.Errors.Select(e => e.Description)));
                user = null;
            }
        }
        if (user is not null && !await users.IsInRoleAsync(user, "Admin")) await users.AddToRoleAsync(user, "Admin");
    }
}

app.UseForwardedHeaders();
// Pays : /uk/… -> mêmes pages, PathBase /uk, interface anglaise ; inactif tant qu'aucun pays autre que la France n'est publié (table pays). AVANT la langue.
app.Use((ctx, next) => PaysSite.Middleware(ctx, next));
// Version anglaise : /en/… -> mêmes pages, PathBase /en (avant fichiers statiques, routage et cache de sortie).
app.Use((ctx, next) => Langue.Middleware(ctx, next));
app.UseHttpsRedirection();
// Fichiers statiques : CSS référencée avec asp-append-version (?v=empreinte) => cache d'un an sans risque ; le reste (favicon…)
// un jour seulement. Les pages HTML, elles, ne sont jamais concernées.
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        if (app.Environment.IsDevelopment()) return;
        ctx.Context.Response.Headers.CacheControl = ctx.Context.Request.Query.ContainsKey("v")
            ? "public,max-age=31536000,immutable"
            : "public,max-age=86400";
    },
});
app.UseRouting();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
// Après le routing (endpoint résolu) et AVANT le cache : une page servie depuis le cache compte aussi.
app.UseMiddleware<PageViewTrackingMiddleware>();
app.UseOutputCache();
// --- SEO : robots.txt et sitemap.xml (adresse canonique fixe : les alias .com / www redirigent déjà vers .org via nginx) ---
const string SiteUrl = "https://www.ilsontvote.org";
// Téléchargement d'un fichier de données ouvertes (liste blanche : nom valide ET présent au manifeste). Reprise de téléchargement permise.
app.MapGet("/donnees/fichier/{nom}", (string nom, DonneesOuvertes donnees) =>
    donnees.Chemin(nom) is { } chemin
        ? Results.File(chemin, "application/gzip", fileDownloadName: nom, enableRangeProcessing: true)
        : Results.NotFound());
app.MapGet("/robots.txt", () => Results.Text(
    string.Join('\n',
        "User-agent: *",
        "Allow: /",
        "Disallow: /Admin",
        "Disallow: /Identity",
        "# Variantes d'une même liste (tri, taille de page, export) : pas d'intérêt à les explorer, la page de base suffit.",
        "Disallow: /*?*tri=",
        "Disallow: /*?*sens=",
        "Disallow: /*?*lignes=",
        "Disallow: /*?*format=",
        "",
        $"Sitemap: {SiteUrl}/sitemap.xml",
        "") , "text/plain"));
// Toutes les pages publiques stables (accueil, listes, scrutins, textes, élus) : ~30 000 URL, sous la limite de 50 000 par fichier.
// Plan du site en TROIS fichiers : un index et un fichier par langue (la limite est de 50 000 adresses par fichier ; le français seul
// en compte ~30 000). Chaque adresse annonce sa version dans l'autre langue (xhtml:link hreflang), comme les balises de chaque page.
async Task<List<(string Url, string? Date)>> PagesPubliques(DapperContext db)
{
    using var cx = db.CreateConnection();
    var aujourdhui = DateTime.UtcNow.ToString("yyyy-MM-dd");
    var urls = new List<(string, string?)>();
    foreach (var u in new[] { "/", "/lois", "/textes", "/deputes", "/senateurs", "/eurodeputes", "/statistiques", "/elus", "/petitions", "/petitions/anomalies", "/anomalies", "/donnees" }) urls.Add((u, aujourdhui));
    foreach (var u in new[] { "/methodologie", "/mentions-legales", "/soutenir", "/mcp-info", "/mcp-confidentialite" }) urls.Add((u, null));
    // Un scrutin ne change plus après son vote : sa date sert de « dernière modification ».
    urls.AddRange((await cx.QueryAsync<(string, DateTime)>("SELECT '/scrutin/' || id, date_scrutin FROM scrutins WHERE chambre IN (SELECT code FROM chambres WHERE pays IN ('FR', 'EU'))")).Select(r => (r.Item1, (string?)r.Item2.ToString("yyyy-MM-dd"))));
    urls.AddRange((await cx.QueryAsync<string>("SELECT '/texte/' || id FROM textes")).Select(u => (u, (string?)null)));
    urls.AddRange((await cx.QueryAsync<string>(
        "SELECT CASE chambre WHEN 'SENAT' THEN '/senateur/' WHEN 'UE' THEN '/eurodepute/' ELSE '/depute/' END || slug FROM parlementaires WHERE chambre IN (SELECT code FROM chambres WHERE pays IN ('FR', 'EU'))")).Select(u => (u, (string?)null)));
    return urls;
}
string Absolue(string chemin, bool en) => SiteUrl + (en ? "/en" + (chemin == "/" ? "" : chemin) : chemin);
string Plan(List<(string Url, string? Date)> urls, bool en)
{
    var sb = new System.Text.StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\" xmlns:xhtml=\"http://www.w3.org/1999/xhtml\">");
    foreach (var (u, date) in urls)
    {
        string E(string x) => System.Security.SecurityElement.Escape(x);
        sb.Append("<url><loc>").Append(E(Absolue(u, en))).Append("</loc>")
          .Append(date is null ? "" : $"<lastmod>{date}</lastmod>")
          .Append("<xhtml:link rel=\"alternate\" hreflang=\"fr\" href=\"").Append(E(Absolue(u, false))).Append("\"/>")
          .Append("<xhtml:link rel=\"alternate\" hreflang=\"en\" href=\"").Append(E(Absolue(u, true))).Append("\"/></url>");
    }
    return sb.Append("</urlset>").ToString();
}
// Index : un fichier par langue (France) + un fichier par pays ACTIF hors France (/uk → sitemap-uk.xml). Un pays inactif n'apparaît nulle part.
app.MapGet("/sitemap.xml", (PaysSite pays) => Results.Text(
    "<?xml version=\"1.0\" encoding=\"UTF-8\"?><sitemapindex xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">" +
    $"<sitemap><loc>{SiteUrl}/sitemap-fr.xml</loc></sitemap><sitemap><loc>{SiteUrl}/sitemap-en.xml</loc></sitemap>" +
    string.Concat(pays.Actifs.Where(p => p.Prefixe.Length > 0).Select(p => $"<sitemap><loc>{SiteUrl}/sitemap-{p.Prefixe}.xml</loc></sitemap>")) +
    "</sitemapindex>", "application/xml"))
    .CacheOutput(p => p.Expire(TimeSpan.FromHours(6)));
// Plan d'un pays hors France : pages de son interface (anglaise, une seule version, pas de hreflang fr/en) + divisions, élus et projets de loi de SES chambres.
// 404 si le préfixe n'est pas celui d'un pays actif (jamais de plan pour un pays non publié).
app.MapGet("/sitemap-{prefixe}.xml", async (string prefixe, PaysSite pays, DapperContext db) =>
{
    var p = pays.Actifs.FirstOrDefault(x => x.Prefixe.Length > 0 && x.Prefixe == prefixe);
    if (p is null) return Results.NotFound();
    using var cx = db.CreateConnection();
    var chambres = pays.ChambresVisibles(p.Code);
    var urls = new List<(string, string?)>();
    var aujourdhui = DateTime.UtcNow.ToString("yyyy-MM-dd");
    // Pages communes d'un pays + celles propres au Royaume-Uni (projets de loi, pétitions) ; listes et fiches d'élus de CHAQUE chambre visible du pays.
    var communes = new List<string> { "/", "/textes", "/elus", "/statistiques", "/donnees", "/anomalies" };
    if (PaysSite.APetitionsEtProjets(p.Code)) communes.Add("/bills");
    if (PaysSite.APetitions(p.Code)) communes.Add("/petitions");
    communes.AddRange(chambres.Select(ch => "/" + IlsOntVote.Web.Models.Chambres.Liste(ch)));
    foreach (var u in communes) urls.Add((u, aujourdhui));
    foreach (var u in new[] { "/methodologie", "/mentions-legales" }) urls.Add((u, null));
    urls.AddRange((await cx.QueryAsync<(string, DateTime)>("SELECT '/scrutin/' || id, date_scrutin FROM scrutins WHERE chambre = ANY(@c)", new { c = chambres })).Select(r => (r.Item1, (string?)r.Item2.ToString("yyyy-MM-dd"))));
    foreach (var ch in chambres)
        urls.AddRange((await cx.QueryAsync<string>("SELECT @f || slug FROM parlementaires WHERE chambre = @ch", new { f = "/" + IlsOntVote.Web.Models.Chambres.Fiche(ch) + "/", ch })).Select(u => (u, (string?)null)));
    if (PaysSite.APetitionsEtProjets(p.Code)) urls.AddRange((await cx.QueryAsync<string>("SELECT '/bill/' || id FROM bills")).Select(u => (u, (string?)null)));
    var sb = new System.Text.StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
    foreach (var (u, date) in urls)
        sb.Append("<url><loc>").Append(System.Security.SecurityElement.Escape(SiteUrl + "/" + prefixe + (u == "/" ? "" : u))).Append("</loc>")
          .Append(date is null ? "" : $"<lastmod>{date}</lastmod>").Append("</url>");
    return Results.Text(sb.Append("</urlset>").ToString(), "application/xml");
}).CacheOutput(p => p.Expire(TimeSpan.FromHours(6)));
app.MapGet("/sitemap-fr.xml", async (DapperContext db) => Results.Text(Plan(await PagesPubliques(db), false), "application/xml"))
    .CacheOutput(p => p.Expire(TimeSpan.FromHours(6)));
app.MapGet("/sitemap-en.xml", async (DapperContext db) => Results.Text(Plan(await PagesPubliques(db), true), "application/xml"))
    .CacheOutput(p => p.Expire(TimeSpan.FromHours(6)));

// Images d'aperçu de partage (og:image). Mises en cache : un scrutin ne change pas, et chaque image coûte une requête + un dessin.
app.MapGet("/apercu/scrutin/{id}.png", async (string id, IScrutinRepository scrutins, ApercuImage image, Langue langue, PaysSite pays) =>
{
    var s = await scrutins.GetAsync(id);
    if (s is null) return Results.NotFound();
    var vue = HemicycleLayout.Construire(await scrutins.GetSiegesAsync(id), pays.Disposition(s.Chambre));   // demi-cercle ou croissants selon la chambre
    return Results.File(image.Scrutin(s, vue, langue, langue.Pays), "image/png");
}).CacheOutput(p => p.Expire(TimeSpan.FromHours(12)).SetVaryByRouteValue("id").VaryByValue(c => new KeyValuePair<string, string>("lang", (c.Items["lang"] as string ?? "fr") + "|" + (c.Items["pays"] as string ?? ""))));
app.MapGet("/apercu/defaut.png", (ApercuImage image, Langue langue) => Results.File(image.Defaut(langue, langue.Pays), "image/png"))
    .CacheOutput(p => p.Expire(TimeSpan.FromHours(12)).VaryByValue(c => new KeyValuePair<string, string>("lang", (c.Items["lang"] as string ?? "fr") + "|" + (c.Items["pays"] as string ?? ""))));   // la clé inclut le PAYS : /uk et /en n'ont pas la même image

// MCP : public, sans clé, limité par IP ; jamais mis en cache de sortie (réponses propres à chaque requête).
app.MapMcp(McpServeur.Chemin).RequireRateLimiting(McpServeur.Limite);

app.MapRazorPages();

app.Run();
