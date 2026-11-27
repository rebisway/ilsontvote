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
                    "Read-only data from ilsontvote.org: who voted how. Start with list_types. " +
                    "Cite the 'url' of each document. Respect the caveats: 'computed' or 'inferred' is not official; " +
                    "null means missing data (not zero); 'absent' is inferred and no reason is known: never assume one. " +
                    "Source licences are listed at " + Perimetre.Site + "/en/mentions-legales (and /{country}/mentions-legales).";
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
    PaysSite site, Themes themes, ILogger<McpOutils> journal)
{
    private const int MaxDocuments = 50;
    private const int MaxLignes = 200;

    private Task<Perimetre> PerimetreAsync(string outil)
    {
        journal.LogInformation("MCP : outil {Outil}", outil);   // mesure d'usage ; ni paramètres ni adresse IP
        return Perimetre.ChargerAsync(site);
    }

    private ITypeDocument Type(string code, Perimetre p) =>
        types.FirstOrDefault(t => t.Code == code && t.Pays(p).Count > 0)
        ?? throw new McpException($"Unknown type: '{code}'. Types: {string.Join(", ", types.Where(t => t.Pays(p).Count > 0).Select(t => t.Code))}.");

    [McpServerTool(Name = "list_types", Title = "Document types", ReadOnly = true, OpenWorld = false)]
    [Description("Lists the available document types (parliamentary vote, popular vote, law, bill, petition, member), their filters, the published countries and chambers, and the themes.")]
    public async Task<string> ListerTypes()
    {
        var p = await PerimetreAsync("list_types");
        return Reponse.Json(new
        {
            Types = types.Where(t => t.Pays(p).Count > 0).Select(t => new { Type = t.Code, Name = t.Nom, t.Description, Filters = t.Filtres, Countries = t.Pays(p) }),
            Chambers = p.Chambres.Select(c => new { Code = c.Key, Country = c.Value, Name = Models.Chambres.Nom(c.Key, true) }),
            Themes = themes.Lister().Select(t => new { t.Code, Name = t.NomEn }),
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
        return Reponse.Json(new { Type = t.Code, res.Total, r.Page, PerPage = r.Limite, Results = res.Resultats });
    }

    [McpServerTool(Name = "get_document", Title = "Get a document", ReadOnly = true, OpenWorld = false)]
    [Description("Full record of a document found with search.")]
    public async Task<string> Consulter(
        [Description("Type code")] string type,
        [Description("Document 'id'")] string id)
    {
        var p = await PerimetreAsync("get_document");
        var document = await Type(type, p).ConsulterAsync(id, p) ?? throw new McpException($"No document '{id}' of type '{type}'.");
        return Reponse.Json(document);
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
        return Reponse.Json(new
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
        return Reponse.Json(new
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
