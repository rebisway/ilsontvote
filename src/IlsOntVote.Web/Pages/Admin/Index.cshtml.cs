using IlsOntVote.Web.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IlsOntVote.Web.Pages.Admin;

/// <summary>Tableau de bord de l'administration : volumes de données, fréquentation de la semaine, dernier import de chaque source.</summary>
public class IndexModel(IAdminRepository admin, IPageViewRepository vues) : PageModel
{
    public ChiffresAdmin Chiffres { get; private set; } = new();
    public ApercuStats Semaine { get; private set; } = new();
    public IReadOnlyList<ExecutionImport> DerniersImports { get; private set; } = [];

    /// <summary>
    /// Pays dont on suit la fréquentation du jour : choisi dans la liste, mémorisé dans un cookie (le choix revient à la visite suivante).
    /// Vide = tous les pays ; « ? » = visites dont le pays est inconnu.
    /// </summary>
    [BindProperty(SupportsGet = true)] public string? Pays { get; set; }
    public IReadOnlyList<CompteVues> PaysDisponibles { get; private set; } = [];
    public JourPays Aujourdhui { get; private set; } = new();
    public JourPays Hier { get; private set; } = new();
    /// <summary>Début du jour affiché, en heure de Paris (pour le libellé) : « aujourd'hui » suit l'heure de Paris, pas l'heure UTC du serveur.</summary>
    public DateTime DebutJourParis { get; private set; }
    private const string CookiePays = "admin_pays";

    public async Task OnGetAsync()
    {
        // Choix du pays : paramètre d'adresse (même vide = « tous », choix explicite), sinon cookie, sinon France.
        if (Request.Query.ContainsKey("pays")) Response.Cookies.Append(CookiePays, Pays ?? "", new CookieOptions { MaxAge = TimeSpan.FromDays(365), HttpOnly = true, SameSite = SameSiteMode.Lax });
        else Pays = Request.Cookies.TryGetValue(CookiePays, out var memo) ? memo : "FR";
        var paris = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");
        var jourParis = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, paris).Date;
        DebutJourParis = jourParis;
        // Bornes en UTC (page_views.visited_at est en UTC) : minuit de Paris aujourd'hui, hier et demain.
        DateTime Utc(DateTime localParis) => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localParis, DateTimeKind.Unspecified), paris);
        var debut = Utc(jourParis); var fin = Utc(jourParis.AddDays(1)); var hierDebut = Utc(jourParis.AddDays(-1));
        PaysDisponibles = await vues.GetPaysDisponiblesAsync(DateTime.UtcNow.AddDays(-30));
        Aujourdhui = await vues.GetJourPaysAsync(debut, fin, Pays);
        Hier = await vues.GetJourPaysAsync(hierDebut, debut, Pays);

        Chiffres = await admin.GetChiffresAsync();
        Semaine = await vues.GetStatsAsync(DateTime.UtcNow.Date.AddDays(-6), null, Granularite.Jour, SourceVues.Humains);

        // Dernière exécution de chaque source : on lit les 200 dernières lignes du journal puis on garde la plus récente
        // par source (plus simple et assez rapide pour quelques sources ; à revoir s'il y en a des dizaines).
        DerniersImports = (await admin.GetImportsAsync(200))
            .GroupBy(i => i.Source).Select(g => g.First()).OrderBy(i => i.Source).ToList();
    }
}
