using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace IlsOntVote.Importer;

/// <summary>
/// Source Parlement européen via HowTheyVote.eu (projet tiers bénévole qui retraite les votes nominatifs officiels du PE).
/// Pourquoi pas le portail officiel : ses fichiers de votes nominatifs (XML « RCV ») répondent 403 aux robots (CloudFront),
/// même depuis le serveur de production (vérifié le 2026-11-15). Le site le dit : chaque scrutin porte une note « source tierce ».
/// API : <c>GET /api/votes?page_size=100&amp;page=N</c> (liste) puis <c>GET /api/votes/{id}</c> (votes de chaque eurodéputé).
/// </summary>
public sealed class ParlementEuropeenSource : IDisposable
{
    private const string Base = "https://howtheyvote.eu/api";
    /// <summary>Législature du PE (10e, depuis juillet 2024) : sert de « législature » dans le schéma.</summary>
    public const int Legislature = 10;
    public const string NoteSource = "Source tierce : HowTheyVote.eu, à partir des votes nominatifs du Parlement européen. Titres en anglais.";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };

    public ParlementEuropeenSource() => _http.DefaultRequestHeaders.UserAgent.ParseAdd("IlsOntVote/1.0 (+https://www.ilsontvote.org)");

    /// <summary>Identifiants et dates de tous les votes (liste paginée), du plus récent au plus ancien.</summary>
    public async Task<List<(string Id, DateOnly Date)>> ListerVotesAsync()
    {
        var tous = new List<(string, DateOnly)>();
        for (var page = 1; ; page++)
        {
            using var doc = await GetAsync($"{Base}/votes?page_size=100&page={page}");
            foreach (var r in doc.RootElement.GetProperty("results").EnumerateArray())
                tous.Add((r.GetProperty("id").GetString()!, DateOnly.FromDateTime(r.GetProperty("timestamp").GetDateTime())));
            if (!doc.RootElement.GetProperty("has_next").GetBoolean()) return tous;
        }
    }

    /// <summary>
    /// Lit un vote : le scrutin, les positions (DID_NOT_VOTE n'est PAS stocké : règle « absent = absent », l'absent se déduit)
    /// et les eurodéputés rencontrés (y compris les non-votants, qui prouvent qu'ils siégeaient ce jour-là).
    /// </summary>
    public async Task<(ScrutinAvecVotes Scrutin, List<MembreUe> Membres)> LireVoteAsync(string id)
    {
        using var doc = await GetAsync($"{Base}/votes/{id}");
        var r = doc.RootElement;
        var date = DateOnly.FromDateTime(r.GetProperty("timestamp").GetDateTime());
        var scrutinId = $"EP-{id}";
        var membres = new List<MembreUe>();
        var votes = new List<VoteRow>();

        foreach (var mv in r.GetProperty("member_votes").EnumerateArray())
        {
            var m = mv.GetProperty("member");
            var g = m.GetProperty("group");
            var membre = new MembreUe(
                $"EP{m.GetProperty("id").GetInt32()}", Texte(m, "first_name"), Casse(Texte(m, "last_name")),
                m.GetProperty("country").GetProperty("iso_alpha_2").GetString() ?? "",
                m.TryGetProperty("national_party", out var np) && np.ValueKind == JsonValueKind.Object ? Texte(np, "label") : null,
                $"EP-{g.GetProperty("code").GetString()}", SigleGroupe(Texte(g, "short_label")), LibelleGroupe(Texte(g, "short_label"), Texte(g, "label")), date);
            membres.Add(membre);

            var position = mv.GetProperty("position").GetString() switch
            {
                "FOR" => Positions.Pour, "AGAINST" => Positions.Contre, "ABSTENTION" => Positions.Abstention, _ => null,
            };
            if (position is not null)
                votes.Add(new VoteRow(scrutinId, membre.Id, membre.GroupeId, position, false, false, null));
        }

        var total = r.GetProperty("stats").GetProperty("total");
        int N(string k) => total.TryGetProperty(k, out var v) ? v.GetInt32() : 0;
        int pour = N("FOR"), contre = N("AGAINST"), abst = N("ABSTENTION");
        var titre = Texte(r, "display_title");
        var sort = r.TryGetProperty("result", out var res) ? res.GetString() switch { "ADOPTED" => "adopté", "REJECTED" => "rejeté", _ => null } : null;
        var procedure = r.TryGetProperty("procedure", out var p) && p.ValueKind == JsonValueKind.Object ? Texte(p, "reference") : null;
        var majorite = r.TryGetProperty("majority_type", out var mt) && mt.ValueKind == JsonValueKind.String ? mt.GetString() : null;

        var scrutin = new ScrutinRow(
            scrutinId, Chambres.UE, Legislature, int.Parse(id), date, titre,
            null, sort, string.IsNullOrEmpty(procedure) ? null : procedure,
            r.TryGetProperty("is_main", out var im) && im.ValueKind == JsonValueKind.True,
            pour + contre + abst, pour, contre, abst, 0, "howtheyvote",
            0, NoteSource,
            TypeVoteLibelle: "scrutin public (appel nominal)", TypeMajorite: majorite,
            SuffragesExprimes: pour + contre);
        return (new ScrutinAvecVotes(scrutin, votes), membres);
    }

    private const int EssaisMax = 8;

    /// <summary>
    /// GET + JSON avec reprise. L'API est bénévole et limite le débit (429 « Too Many Requests » observé après ~100 appels rapprochés) :
    /// on respecte <c>Retry-After</c> quand il est fourni, sinon on attend de plus en plus longtemps.
    /// </summary>
    private async Task<JsonDocument> GetAsync(string url)
    {
        for (var essai = 1; ; essai++)
        {
            TimeSpan? attente = null;
            try
            {
                using var rep = await _http.GetAsync(url);
                if ((int)rep.StatusCode == 429 && essai < EssaisMax)
                {
                    attente = rep.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(20 * essai);
                    Console.Error.WriteLine($"  limite de débit (429), pause de {(int)attente.Value.TotalSeconds} s (essai {essai}/{EssaisMax})");
                }
                else
                {
                    rep.EnsureSuccessStatusCode();
                    return await JsonDocument.ParseAsync(await rep.Content.ReadAsStreamAsync());
                }
            }
            catch (Exception ex) when (essai < EssaisMax && ex is HttpRequestException or IOException or TaskCanceledException)
            {
                attente = TimeSpan.FromSeconds(5 * essai);
                Console.Error.WriteLine($"  échec ({ex.Message}), essai {essai}/{EssaisMax}");
            }
            await Task.Delay(attente!.Value);
        }
    }

    /// <summary>« Non-attached » devient « NI » (même sigle que l'AN, place en fin d'hémicycle).</summary>
    private static string SigleGroupe(string court) => court.Equals("Non-attached", StringComparison.OrdinalIgnoreCase) ? "NI" : court;
    private static string LibelleGroupe(string court, string libelle) => SigleGroupe(court) == "NI" ? "Non-inscrits" : libelle;

    private static string Texte(JsonElement e, string cle) => e.TryGetProperty(cle, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";

    /// <summary>L'API écrit les noms de famille en MAJUSCULES : « ABAD…  JOVER » -> « Abad… Jover » (mots courts en minuscules non gérés).</summary>
    private static string Casse(string nom) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(nom.ToLowerInvariant());

    /// <summary>
    /// Pays de l'UE en français (code ISO 3166-1 alpha-2). Table fixe plutôt que <c>RegionInfo</c> : selon la configuration de
    /// culture de la machine, .NET renvoie le nom dans la langue du pays (« Suomi », « España »…).
    /// </summary>
    private static readonly Dictionary<string, string> PaysFr = new()
    {
        ["AT"] = "Autriche", ["BE"] = "Belgique", ["BG"] = "Bulgarie", ["HR"] = "Croatie", ["CY"] = "Chypre", ["CZ"] = "Tchéquie",
        ["DK"] = "Danemark", ["EE"] = "Estonie", ["FI"] = "Finlande", ["FR"] = "France", ["DE"] = "Allemagne", ["GR"] = "Grèce",
        ["HU"] = "Hongrie", ["IE"] = "Irlande", ["IT"] = "Italie", ["LV"] = "Lettonie", ["LT"] = "Lituanie", ["LU"] = "Luxembourg",
        ["MT"] = "Malte", ["NL"] = "Pays-Bas", ["PL"] = "Pologne", ["PT"] = "Portugal", ["RO"] = "Roumanie", ["SK"] = "Slovaquie",
        ["SI"] = "Slovénie", ["ES"] = "Espagne", ["SE"] = "Suède",
    };

    /// <summary>Nom du pays en français à partir du code ISO à 2 lettres (« FI » -> « Finlande »), sinon le code.</summary>
    public static string Pays(string iso) => PaysFr.GetValueOrDefault(iso.ToUpperInvariant(), iso);

    public void Dispose() => _http.Dispose();
}

/// <summary>Un eurodéputé tel que vu dans un vote (groupe au jour du vote, date de ce vote).</summary>
public sealed record MembreUe(
    string Id, string Prenom, string Nom, string PaysIso, string? PartiNational,
    string GroupeId, string GroupeSigle, string GroupeLibelle, DateOnly Vu);
