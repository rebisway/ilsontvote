using System.Threading.RateLimiting;
using Dapper;
using IlsOntVote.Web.Data;
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
builder.Services.AddScoped<IScrutinRepository, ScrutinRepository>();
builder.Services.AddScoped<IParlementaireRepository, ParlementaireRepository>();
builder.Services.AddScoped<ITexteRepository, TexteRepository>();
builder.Services.AddScoped<IPageViewRepository, PageViewRepository>();
builder.Services.AddScoped<IAdminRepository, AdminRepository>();
builder.Services.AddSingleton<IIpHasher, IpHasher>();
builder.Services.AddSingleton<PaysResolver>();

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
});

// Les données ne changent qu'après un import : un cache de quelques minutes suffit. JAMAIS de cache
// pour /Admin et /Identity : une page d'administrateur mise en cache serait servie à n'importe qui.
builder.Services.AddOutputCache(options =>
{
    options.AddBasePolicy(b => b
        .With(c => !(c.HttpContext.Request.Path.StartsWithSegments("/Admin", StringComparison.OrdinalIgnoreCase)
                  || c.HttpContext.Request.Path.StartsWithSegments("/Identity", StringComparison.OrdinalIgnoreCase)))
        .Expire(builder.Environment.IsDevelopment() ? TimeSpan.FromSeconds(1) : TimeSpan.FromMinutes(5))
        .SetVaryByQuery("*"));
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
app.MapGet("/robots.txt", () => Results.Text(
    $"User-agent: *{Environment.NewLine}Disallow: /Admin{Environment.NewLine}Disallow: /Identity{Environment.NewLine}{Environment.NewLine}Sitemap: {SiteUrl}/sitemap.xml{Environment.NewLine}", "text/plain"));
// Toutes les pages publiques stables (accueil, listes, scrutins, textes, élus) : ~30 000 URL, sous la limite de 50 000 par fichier.
app.MapGet("/sitemap.xml", async (DapperContext db) =>
{
    using var cx = db.CreateConnection();
    var urls = new List<string> { "/", "/lois", "/textes", "/deputes", "/senateurs", "/methodologie", "/mentions-legales", "/soutenir" };
    urls.AddRange(await cx.QueryAsync<string>("SELECT '/scrutin/' || id FROM scrutins"));
    urls.AddRange(await cx.QueryAsync<string>("SELECT '/texte/' || id FROM textes"));
    urls.AddRange(await cx.QueryAsync<string>(
        "SELECT CASE chambre WHEN 'SENAT' THEN '/senateur/' ELSE '/depute/' END || slug FROM parlementaires"));
    var sb = new System.Text.StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
    foreach (var u in urls) sb.Append("<url><loc>").Append(System.Security.SecurityElement.Escape(SiteUrl + u)).Append("</loc></url>");
    return Results.Text(sb.Append("</urlset>").ToString(), "application/xml");
}).CacheOutput(p => p.Expire(TimeSpan.FromHours(6)));

app.MapRazorPages();

app.Run();
