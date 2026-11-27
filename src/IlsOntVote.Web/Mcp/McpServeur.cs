using System.ComponentModel;
using System.Threading.RateLimiting;
using IlsOntVote.Web.Models;
using IlsOntVote.Web.Repositories;
using IlsOntVote.Web.Services;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace IlsOntVote.Web.Mcp;

/// <summary>Déclaration du serveur MCP (Model Context Protocol) : lecture seule, public, sans clé, sur <see cref="Chemin"/>.</summary>
public static class McpServeur
{
    public const string Chemin = "/mcp";
    public const string Limite = "mcp";
    /// <summary>Requêtes par minute et par adresse IP : large pour un assistant, trop bas pour aspirer la base (les fichiers de données ouvertes servent à cela).</summary>
    private const int RequetesParMinute = 60;

    public static void AjouterMcp(this IServiceCollection services)
    {
        // Un type de document = une classe ; l'ordre est celui présenté aux clients.
        services.AddSingleton<Sources>();
        services.AddScoped<VotesParlementaires>();
        services.AddScoped<Elus>();
        services.AddScoped<ITypeDocument>(s => s.GetRequiredService<VotesParlementaires>());
        services.AddScoped<ITypeDocument, VotesPopulaires>();
        services.AddScoped<ITypeDocument, Lois>();
        services.AddScoped<ITypeDocument, ProjetsDeLoi>();
        services.AddScoped<ITypeDocument, Petitions>();
        services.AddScoped<ITypeDocument>(s => s.GetRequiredService<Elus>());

        services.Configure<Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>(o => o.AddPolicy(Limite, ctx => RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "inconnue",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = RequetesParMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));

        services.AddMcpServer(o =>
            {
                o.ServerInfo = new() { Name = "ilsontvote", Title = "They voted (ilsontvote.org)", Version = "1.0.0" };
                o.ServerInstructions =
                    "Read-only data from ilsontvote.org: who voted how. Start with list_types and rely on its descriptions to state what is covered. " +
                    "EU is the European Parliament, not a country: do not count it as one. Not every source is official (see 'sources'). " +
                    "Cite the 'url' of each document. Respect the caveats: 'computed' or 'inferred' is not official; " +
                    "null means missing data (not zero); 'absent' is inferred and no reason is known: never assume one. " +
                    "Each answer carries a 'sources' block: give the requested attribution, respect commercial_use and share_alike, " +
                    "and never treat 'unknown' or 'unverified' as permission.";
            })
            // Sans état : aucune session à conserver entre deux requêtes (redémarrages et cache sans effet).
            .WithHttpTransport(o => o.Stateless = true)
            .WithTools<McpOutils>();
    }
}

/// <summary>Les outils offerts aux clients MCP. Tous en lecture ; les réponses sont paginées et plafonnées. L'API (noms, champs, messages) est en anglais.</summary>
[McpServerToolType]
public sealed class McpOutils(
    IEnumerable<ITypeDocument> types, VotesParlementaires votes, Elus elus,
    IScrutinRepository scrutins, IParlementaireRepository parlementaires,
    PaysSite site, Themes themes, ILogger<McpOutils> journal,
    IQualiteRepository qualite, IMcpAppelRepository appels, IIpHasher empreintes, IHttpContextAccessor http, Sources sources)
{
    private const int MaxDocuments = 50;
    private const int MaxLignes = 200;

    // État des chambres gardé 5 minutes : requête coûteuse sur les mandats, et le serveur est public.
    private static (DateTime Charge, IReadOnlyList<ChambreQualite> Lignes) _chambres;

    /// <summary>Compte l'appel (journal et /Admin/Stats) puis charge le périmètre publié.</summary>
    private async Task<Perimetre> PerimetreAsync(string outil)
    {
        journal.LogInformation("MCP : outil {Outil}", outil);   // ni paramètres ni adresse IP
        var ctx = http.HttpContext;
        // Même choix du visiteur que pour les pages : DNT ou Sec-GPC = appel non enregistré.
        if (ctx is not null && ctx.Request.Headers["DNT"] != "1" && ctx.Request.Headers["Sec-GPC"] != "1")
        {
            // Au mieux : une erreur de statistiques ne doit jamais faire échouer un appel.
            try { await appels.EnregistrerAsync(outil, empreintes.Hash(ctx.Connection.RemoteIpAddress)); }
            catch (Exception ex) { journal.LogWarning(ex, "Échec d'enregistrement d'un appel MCP"); }
        }
        return await Perimetre.ChargerAsync(site);
    }

    private async Task<IReadOnlyList<ChambreQualite>> ChambresAsync()
    {
        if (DateTime.UtcNow - _chambres.Charge > TimeSpan.FromMinutes(5)) _chambres = (DateTime.UtcNow, await qualite.ChambresAsync());
        return _chambres.Lignes;
    }

    /// <summary>Instant UTC au format ISO ; null si inconnu.</summary>
    private static string? Instant(DateTime? d) => d?.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);

    // Volontairement absents : messages d'erreur, sorties des tâches et commandes (détails internes, réservés à /Admin).
    [McpServerTool(Name = "import_status", Title = "Import status", ReadOnly = true, OpenWorld = false)]
    [Description("State of the data imports: for each scheduled task, its last run, whether it succeeded and the last successful run (UTC).")]
    public async Task<string> EtatImports([Description("Country code (tasks common to all countries are always listed)")] string? country = null)
    {
        var p = await PerimetreAsync("import_status");
        p.Filtrer(country, null);   // pays non publié : erreur explicite
        var voulu = string.IsNullOrWhiteSpace(country) ? null : country.Trim().ToUpperInvariant();
        var publies = p.Chambres.Values.ToHashSet();
        var taches = (await qualite.TachesAsync()).Where(t => t.Pays is null || (publies.Contains(t.Pays) && (voulu is null || t.Pays == voulu)));
        return Reponse.Json(new
        {
            Tasks = taches.Select(t => new
            {
                Task = t.Code, Country = t.Pays, OneOff = t.Ponctuelle, LastRunStarted = Instant(t.Debut), LastRunEnded = Instant(t.Fin),
                LastRunSucceeded = t.Ok, LastRunErrors = t.NbErreurs, LastSuccess = Instant(t.DernierSucces),
            }),
            Note = "One-off tasks are run by hand (history loading). null means never run or not finished. A successful import does not prove the source was up to date.",
        });
    }

    [McpServerTool(Name = "data_quality", Title = "Data quality", ReadOnly = true, OpenWorld = false)]
    [Description("Known gaps in the data, per chamber: date of the latest vote, incomplete roll calls, votes without a result, members in office compared with seats, overlapping terms, members without a group.")]
    public async Task<string> QualiteDonnees(
        [Description("Country code")] string? country = null,
        [Description("Chamber code")] string? chamber = null)
    {
        var p = await PerimetreAsync("data_quality");
        var voulues = p.Filtrer(country, chamber).ToHashSet();
        return Reponse.Json(new
        {
            Chambers = (await ChambresAsync()).Where(c => voulues.Contains(c.Code)).Select(c => new
            {
                Chamber = c.Code, Country = p.Chambres[c.Code], Name = Models.Chambres.Nom(c.Code, true),
                Votes = c.Scrutins, LatestVote = Reponse.Jour(c.Dernier), DaysSinceLatestVote = c.AgeJours,
                IncompleteRollCalls = c.Incomplets, VotesWithoutResult = c.SansResultat,
                MembersInOffice = c.EnExercice, Seats = c.Sieges, MoreMembersThanSeats = c.AuDessusDesSieges,
                OverlappingTerms = c.Chevauchants, MembersWithoutGroup = c.SansGroupe,
            }),
            Note = "Computed from our database. A long time without a vote may be a recess. More members than seats or overlapping terms point to badly dated terms.",
        });
    }

    private ITypeDocument Type(string code, Perimetre p) =>
        types.FirstOrDefault(t => t.Code == code && t.Pays(p).Count > 0)
        ?? throw new McpException($"Unknown type: '{code}'. Types: {string.Join(", ", types.Where(t => t.Pays(p).Count > 0).Select(t => t.Code))}.");

    [McpServerTool(Name = "list_types", Title = "Document types", ReadOnly = true, OpenWorld = false)]
    [Description("Lists the available document types (parliamentary vote, popular vote, law, bill, petition, member), their filters, the published countries and chambers, and the themes.")]
    public async Task<string> ListerTypes()
    {
        var p = await PerimetreAsync("list_types");
        var pays = p.Chambres.Values.Distinct().ToList();
        return Reponse.Json(new
        {
            // Compte explicite : un assistant comptait « EU » parmi les pays.
            Coverage = new { Countries = pays.Where(c => c != "EU"), SupranationalParliaments = pays.Where(c => c == "EU") },
            Types = types.Where(t => t.Pays(p).Count > 0).Select(t => new { Type = t.Code, Name = t.Nom, t.Description, Filters = t.Filtres, Countries = t.Pays(p) }),
            Chambers = p.Chambres.Select(c => new { Code = c.Key, Country = c.Value, Name = Models.Chambres.Nom(c.Key, true) }),
            Themes = themes.Lister().Select(t => new { t.Code, Name = t.NomEn }),
            // Toutes les licences des chambres et plateformes publiées (les pays inactifs n'apparaissent pas).
            Sources = (await sources.ListerAsync()).Where(l => l.Chambres.Any(c => p.Chambres.ContainsKey(c) || p.Publie(c == "UK" ? "GB" : c))).Select(l => l.VersApi()),
        });
    }

    [McpServerTool(Name = "search", Title = "Search documents", ReadOnly = true, OpenWorld = false)]
    [Description("Searches documents of one type, most recent first (members: alphabetical order). Titles are in the language of the source: search with words of that language.")]
    public async Task<string> Rechercher(
        [Description("Type code (see list_types), for example parliamentary_vote")] string type,
        [Description("Words that must all appear in the title (or in the member's name)")] string? text = null,
        [Description("Country code: FR, EU, GB, US, CH…")] string? country = null,
        [Description("Chamber code: AN, SENAT, UE, COMMONS…")] string? chamber = null,
        [Description("Start date, YYYY-MM-DD")] string? from = null,
        [Description("End date, YYYY-MM-DD")] string? to = null,
        [Description("Theme code (approximate keyword classification)")] string? theme = null,
        [Description("Parliamentary votes: only final votes on a whole text")] bool final_votes_only = false,
        [Description("Page, starting at 1")] int page = 1,
        [Description("Results per page (50 at most)")] int limit = 20)
    {
        var p = await PerimetreAsync("search");
        var t = Type(type, p);
        // Un filtre non pris en charge est refusé : l'ignorer ferait croire à une réponse filtrée.
        var demandes = new (string Nom, bool Present)[]
        {
            ("country", !string.IsNullOrWhiteSpace(country)), ("chamber", !string.IsNullOrWhiteSpace(chamber)), ("from", !string.IsNullOrWhiteSpace(from)),
            ("to", !string.IsNullOrWhiteSpace(to)), ("theme", !string.IsNullOrWhiteSpace(theme)), ("final_votes_only", final_votes_only),
        };
        var refuses = demandes.Where(f => f.Present && !t.Filtres.Contains(f.Nom)).Select(f => f.Nom).ToList();
        if (refuses.Count > 0) throw new McpException($"Filter(s) not supported for '{type}': {string.Join(", ", refuses)}. Available: {string.Join(", ", t.Filtres)}.");

        string? codeTheme = null;
        if (!string.IsNullOrWhiteSpace(theme))
            codeTheme = themes.Valider(theme) ?? throw new McpException($"Unknown theme: '{theme}'. See list_types.");

        var r = new Recherche(text, country, chamber, from, to, codeTheme, final_votes_only, Math.Clamp(page, 1, 10_000), Math.Clamp(limit, 1, MaxDocuments));
        var res = await t.RechercherAsync(r, p);
        return await sources.JoindreAsync(t.Code, new { Type = t.Code, res.Total, r.Page, PerPage = r.Limite, Results = res.Resultats });
    }

    [McpServerTool(Name = "get_document", Title = "Get a document", ReadOnly = true, OpenWorld = false)]
    [Description("Full record of a document found with search.")]
    public async Task<string> Consulter(
        [Description("Type code")] string type,
        [Description("Document 'id'")] string id)
    {
        var p = await PerimetreAsync("get_document");
        var document = await Type(type, p).ConsulterAsync(id, p) ?? throw new McpException($"No document '{id}' of type '{type}'.");
        return await sources.JoindreAsync(type, document);
    }

    [McpServerTool(Name = "vote_positions", Title = "Who voted how", ReadOnly = true, OpenWorld = false)]
    [Description("For a parliamentary vote: breakdown by political group and the position of each member (for, against, abstention, non_voting, absent).")]
    public async Task<string> PositionsVote(
        [Description("Parliamentary vote id")] string id,
        [Description("Filter: for, against, abstention, non_voting or absent")] string? position = null,
        [Description("Filter by group short name")] string? group = null,
        [Description("Page, starting at 1")] int page = 1,
        [Description("Members per page (200 at most)")] int limit = 100)
    {
        var p = await PerimetreAsync("vote_positions");
        var s = await votes.LireAsync(id, p) ?? throw new McpException($"No vote '{id}'.");
        var voulue = Reponse.PositionSaisie(position, attributs: false);

        var sieges = await scrutins.GetSiegesAsync(s.Id);
        var groupes = sieges.GroupBy(x => x.GroupeSigle).OrderBy(g => g.Min(x => x.GroupeOrdre)).Select(g => new
        {
            Group = g.Key,
            For = g.Count(x => x.Position == Pos.Pour), Against = g.Count(x => x.Position == Pos.Contre),
            Abstention = g.Count(x => x.Position == Pos.Abstention), NonVoting = g.Count(x => x.Position == Pos.NonVotant),
            Absent = g.Count(x => x.Position == Pos.Absent),
        });
        var filtres = sieges.Where(x => (voulue is null || x.Position == voulue)
            && (string.IsNullOrWhiteSpace(group) || x.GroupeSigle.Equals(group.Trim(), StringComparison.OrdinalIgnoreCase))).ToList();
        page = Math.Clamp(page, 1, 10_000);
        limit = Math.Clamp(limit, 1, MaxLignes);
        var pays = p.Chambres[s.Chambre];
        return await sources.JoindreAsync(votes.Code, new
        {
            Vote = new { s.Id, Chamber = s.Chambre, Date = Reponse.Jour(s.DateScrutin), Title = s.TitreAffiche, TitleEn = s.TitreEn, Url = p.Url(pays, "/scrutin/" + s.Id) },
            Caveats = new
            {
                IncompleteRollCall = s.NominatifIncomplet, MissingIndividualVotes = s.NbVotesManquants, s.Note,
                Absences = s.NominatifIncomplet
                    ? "UNCERTAIN: the source's roll call is incomplete; members shown as absent may have voted."
                    : "Inferred: they held a seat on the date of the vote and do not appear in it. No reason is known.",
            },
            ComputedBreakdownByGroup = groupes,
            Total = filtres.Count, Page = page, PerPage = limit,
            Members = filtres.Skip((page - 1) * limit).Take(limit).Select(x => new
            {
                Id = x.ParlementaireId, FirstName = x.Prenom, LastName = x.Nom, Group = x.GroupeSigle, Position = Reponse.Position(x.Position),
                Delegated = x.ParDelegation, Corrected = x.Rectifie, OriginalPosition = Reponse.Position(x.PositionOrigine), OriginalPositionUnknown = x.OrigineInconnue,
            }),
        });
    }

    [McpServerTool(Name = "member_history", Title = "A member's votes", ReadOnly = true, OpenWorld = false)]
    [Description("History of the recorded votes held during a member's terms (absences included, incomplete roll calls excluded).")]
    public async Task<string> HistoriqueElu(
        [Description("Member id (search, type member)")] string id,
        [Description("Words of the vote title")] string? text = null,
        [Description("for, against, abstention, non_voting, absent, delegated or corrected")] string? position = null,
        [Description("Only final votes on a whole text")] bool final_votes_only = false,
        [Description("Start date, YYYY-MM-DD")] string? from = null,
        [Description("End date, YYYY-MM-DD")] string? to = null,
        [Description("Page, starting at 1")] int page = 1,
        [Description("Votes per page (50 at most)")] int limit = 20)
    {
        var p = await PerimetreAsync("member_history");
        var e = await elus.LireAsync(id, p) ?? throw new McpException($"No member '{id}'.");
        static DateTime? Date(string? d) => Reponse.DateSaisie(d) is { } x ? DateTime.Parse(x, System.Globalization.CultureInfo.InvariantCulture) : null;
        var res = await parlementaires.GetVotesAsync(e.Id, e.Chambre, final_votes_only, Math.Clamp(page, 1, 10_000), Math.Clamp(limit, 1, MaxDocuments),
            Date(from), Date(to), text, Reponse.PositionSaisie(position, attributs: true));
        var pays = p.Chambres[e.Chambre];
        // Les votes appartiennent au lot des scrutins de la chambre.
        return await sources.JoindreAsync(votes.Code, new
        {
            Member = new { e.Id, FirstName = e.Prenom, LastName = e.Nom, Chamber = e.Chambre, Url = p.Url(pays, e.Lien) },
            res.Total, res.Page, PerPage = res.ParPage,
            Votes = res.Lignes.Select(v => new
            {
                Id = v.ScrutinId, Date = Reponse.Jour(v.DateScrutin), Title = v.TitreAffiche, TitleEn = v.TitreEn, Result = Reponse.Resultat(v.Sort), Position = Reponse.Position(v.Position),
                Delegated = v.ParDelegation, Corrected = v.Rectifie, OriginalPosition = Reponse.Position(v.PositionOrigine), OriginalPositionUnknown = v.OrigineInconnue,
                Url = p.Url(pays, "/scrutin/" + v.ScrutinId),
            }),
        });
    }
}
