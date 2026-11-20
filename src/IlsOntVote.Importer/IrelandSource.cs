using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IlsOntVote.Importer;

/// <summary>Une division (vote nominal) de l'Oireachtas, avec les listes nominatives Tá / Níl / Staon fournies dans la même réponse.</summary>
public sealed record DivisionIe(
    string Maison, int Legislature, DateOnly Date, string VoteId, string Debat, string Question, string? Issue,
    int Ta, int Nil, int Staon, IReadOnlyList<VoteIe> Votes);

/// <summary>Position d'un membre dans une division : <c>Code</c> = <c>memberCode</c> de l'API (identifiant stable), <c>Liste</c> = ta | nil | staon.</summary>
public sealed record VoteIe(string Code, string Liste);

/// <summary>Appartenance d'un membre à un parti, avec ses dates (un membre peut changer de parti en cours de mandature).</summary>
public sealed record PartiIe(string Code, string Nom, DateOnly Debut, DateOnly? Fin);

/// <summary>Membre d'une chambre pour une mandature (Dáil n° 34, Seanad n° 27…) : mandat daté, circonscription ou panel, partis datés.</summary>
public sealed record MembreIe(string Code, string Prenom, string Nom, string? Circonscription, DateOnly Debut, DateOnly? Fin, IReadOnlyList<PartiIe> Partis);

/// <summary>Une mandature d'une chambre : numéro et dates telles que publiées par <c>/v1/houses</c>.</summary>
public sealed record MandatureIe(string Maison, int Numero, DateOnly Debut, DateOnly? Fin);

/// <summary>
/// Parlement d'Irlande (Oireachtas), SOURCE OFFICIELLE : API publique <c>api.oireachtas.ie/v1</c> (JSON, sans clé), licence CC BY 4.0 (« Oireachtas (Open Data) PSI Licence »).
/// Chaque division porte DANS LA MÊME RÉPONSE les listes nominatives Tá (pour), Níl (contre) et Staon (abstention) ; seuls les membres qui ont voté sont listés,
/// donc les absents se DÉDUISENT des mandats datés (<c>/v1/members</c>), comme au Sénat d'Italie. Le filtre par mandature est <c>chamber_id</c> = adresse de la mandature
/// (<c>…/house/dail/34</c>) : le paramètre <c>house_no</c> est IGNORÉ par l'API (constaté : il renvoyait la 34e Dáil pour house_no=33).
/// </summary>
public sealed class IrelandSource : IDisposable
{
    private const string Base = "https://api.oireachtas.ie/v1/";
    private const string UriMaison = "https://data.oireachtas.ie/ie/oireachtas/house/";
    private const int Page = 100;
    private readonly HttpClient _http;

    public IrelandSource()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("IlsOntVote", "1.0"));
    }

    /// <summary>Codes d'API des deux chambres : « dail » (basse) et « seanad » (haute).</summary>
    public const string Dail = "dail";
    public const string Seanad = "seanad";

    public static string IdChambre(string maison) => maison == Seanad ? Chambres.IeSeanad : Chambres.IeDail;
    public static string IdMembre(string maison, string code) => $"{IdChambre(maison)}-{code}";
    public static string IdScrutin(string maison, int legislature, int numero) => $"{IdChambre(maison)}-{legislature}-{numero}";
    public static string IdGroupe(string maison, int legislature, string parti) => $"{IdChambre(maison)}-{legislature}-{parti}";

    /// <summary>
    /// Numéro du site : jours depuis le 1970-01-01 × 1 000 + numéro du vote. Le <c>voteId</c> de la source (« vote_252 ») n'est PAS unique dans une mandature
    /// (constaté : 178 identifiants distincts pour 200 divisions de la 33e Dáil) ; le couple (date, voteId) l'est. Le détail est dans la note de chaque vote.
    /// </summary>
    public static int Numero(DivisionIe d) => (d.Date.DayNumber - new DateOnly(1970, 1, 1).DayNumber) * 1000 + NumeroVote(d.VoteId);

    /// <summary>« vote_252 » → 252 (0 si la forme est inattendue).</summary>
    public static int NumeroVote(string voteId) => int.TryParse(voteId.Replace("vote_", ""), out var n) ? n : 0;

    private async Task<JsonDocument> GetAsync(string chemin)
    {
        for (var essai = 1; ; essai++)
        {
            try
            {
                using var r = await _http.GetAsync(Base + chemin);
                r.EnsureSuccessStatusCode();
                return await JsonDocument.ParseAsync(await r.Content.ReadAsStreamAsync());
            }
            catch (Exception) when (essai < 3) { await Task.Delay(2000 * essai); }
        }
    }

    /// <summary>Mandatures publiées pour une chambre (« dail » / « seanad »), de la plus récente à la plus ancienne. Les lignes mixtes « dail &amp; seanad » sont ignorées.</summary>
    public async Task<IReadOnlyList<MandatureIe>> ListerMandaturesAsync(string maison)
    {
        using var doc = await GetAsync("houses?limit=100");
        var liste = new List<MandatureIe>();
        foreach (var r in doc.RootElement.GetProperty("results").EnumerateArray())
        {
            var h = r.GetProperty("house");
            if (h.GetProperty("houseCode").GetString() != maison || !int.TryParse(h.GetProperty("houseNo").GetString(), out var no)) continue;
            var dr = h.GetProperty("dateRange");
            if (Date(dr, "start") is { } debut) liste.Add(new MandatureIe(maison, no, debut, Date(dr, "end")));
        }
        return liste.OrderByDescending(m => m.Debut).ToList();
    }

    private static DateOnly? Date(JsonElement parent, string nom) =>
        parent.TryGetProperty(nom, out var e) && e.ValueKind == JsonValueKind.String && DateOnly.TryParse(e.GetString()![..10], CultureInfo.InvariantCulture, out var d) ? d : null;

    /// <summary>Toutes les divisions d'une mandature, par pages de 100 (la réponse contient déjà les listes nominatives).</summary>
    public async Task<IReadOnlyList<DivisionIe>> ListerDivisionsAsync(string maison, int numero)
    {
        var tout = new List<DivisionIe>();
        for (var skip = 0; ; skip += Page)
        {
            using var doc = await GetAsync($"divisions?chamber_id={UriMaison}{maison}/{numero}&limit={Page}&skip={skip}");
            var resultats = doc.RootElement.GetProperty("results");
            var n = 0;
            foreach (var r in resultats.EnumerateArray()) { n++; if (Convertir(r.GetProperty("division"), maison, numero) is { } d) tout.Add(d); }
            if (n < Page) break;
            await Task.Delay(200);   // politesse envers l'API
        }
        return tout;
    }

    /// <summary>Convertit une division JSON ; null si la date ou les totaux manquent (jamais d'approximation).</summary>
    public static DivisionIe? Convertir(JsonElement d, string maison, int legislature)
    {
        if (Date(d, "date") is not { } date) return null;
        var voteId = d.TryGetProperty("voteId", out var vi) ? vi.GetString() ?? "" : "";
        var debat = d.TryGetProperty("debate", out var db) && db.TryGetProperty("showAs", out var ds) ? Nettoyer(ds.GetString()) : "";
        var question = d.TryGetProperty("subject", out var su) && su.TryGetProperty("showAs", out var ss) ? Nettoyer(ss.GetString()) : "";
        var issue = d.TryGetProperty("outcome", out var oc) ? oc.GetString() : null;
        if (!d.TryGetProperty("tallies", out var t)) return null;
        var votes = new List<VoteIe>();
        int Liste(string cle, string code)
        {
            if (!t.TryGetProperty(cle, out var l) || l.ValueKind != JsonValueKind.Object) return 0;   // « staonVotes » vaut null au Seanad quand personne ne s'abstient
            foreach (var m in l.GetProperty("members").EnumerateArray())
                if (m.GetProperty("member").TryGetProperty("memberCode", out var mc) && mc.GetString() is { Length: > 0 } c) votes.Add(new VoteIe(c, code));
            return l.TryGetProperty("tally", out var tl) && tl.TryGetInt32(out var n) ? n : 0;
        }
        var ta = Liste("taVotes", "ta"); var nil = Liste("nilVotes", "nil"); var staon = Liste("staonVotes", "staon");
        return new DivisionIe(maison, legislature, date, voteId, debat, question, issue, ta, nil, staon, votes);
    }

    /// <summary>Retire les « Question put: » en tête (parfois doublés, « Question put: : ») et les espaces superflus.</summary>
    public static string Nettoyer(string? s) => Regex.Replace(s ?? "", @"^\s*Question put:\s*:?\s*", "").Trim();

    /// <summary>Membres d'une mandature avec mandat daté, circonscription (Dáil) ou panel (Seanad) et partis datés.</summary>
    public async Task<IReadOnlyList<MembreIe>> LireMembresAsync(string maison, int numero)
    {
        using var doc = await GetAsync($"members?chamber_id={UriMaison}{maison}/{numero}&limit=500");
        var liste = new List<MembreIe>();
        foreach (var r in doc.RootElement.GetProperty("results").EnumerateArray())
        {
            if (ConvertirMembre(r.GetProperty("member"), maison, numero) is { } m) liste.Add(m);
        }
        return liste;
    }

    /// <summary>Convertit un membre en ne gardant que l'appartenance à la mandature demandée (un ancien député peut aussi avoir siégé dans l'autre chambre).</summary>
    public static MembreIe? ConvertirMembre(JsonElement m, string maison, int numero)
    {
        var code = m.GetProperty("memberCode").GetString();
        if (code is null) return null;
        foreach (var ms in m.GetProperty("memberships").EnumerateArray())
        {
            var x = ms.GetProperty("membership");
            var h = x.GetProperty("house");
            if (h.GetProperty("houseCode").GetString() != maison || h.GetProperty("houseNo").GetString() != numero.ToString()) continue;
            var dr = x.GetProperty("dateRange");
            if (Date(dr, "start") is not { } debut) continue;
            var partis = new List<PartiIe>();
            foreach (var p in x.GetProperty("parties").EnumerateArray())
            {
                var pp = p.GetProperty("party");
                var pdr = pp.GetProperty("dateRange");
                partis.Add(new PartiIe(pp.GetProperty("partyCode").GetString() ?? "Independent", pp.GetProperty("showAs").GetString() ?? "Independent",
                    Date(pdr, "start") ?? debut, Date(pdr, "end")));
            }
            var circ = x.GetProperty("represents").EnumerateArray().Select(rp => rp.GetProperty("represent").GetProperty("showAs").GetString()).FirstOrDefault();
            var prenom = m.TryGetProperty("firstName", out var fn) ? fn.GetString() ?? "" : "";
            var nom = m.TryGetProperty("lastName", out var ln) ? ln.GetString() ?? "" : "";
            return new MembreIe(code, prenom, nom, circ, debut, Date(dr, "end"), partis);
        }
        return null;
    }

    private static readonly Dictionary<string, (string Sigle, int Ordre)> Partis = new(StringComparer.OrdinalIgnoreCase)
    {
        ["People_Before_Profit_Solidarity"] = ("PBP-S", 1), ["Sinn_Féin"] = ("SF", 2), ["Social_Democrats"] = ("SD", 3), ["Labour_Party"] = ("Lab", 4),
        ["Green_Party"] = ("GP", 5), ["Independent"] = ("Ind", 6), ["Independent_Ireland"] = ("II", 7), ["Fianna_Fáil"] = ("FF", 8), ["Fine_Gael"] = ("FG", 9), ["Aontú"] = ("Aon", 10),
    };

    private static readonly string[] Palette = ["#b02f4a", "#326760", "#7a5cc2", "#d4543a", "#3f9e5b", "#8a8f98", "#2a9d8f", "#3fa34d", "#3b82c4", "#e0b400", "#5a3e2b"];

    /// <summary>
    /// Groupe d'une mandature = un parti. Sigle usuel (FF, FG, SF…) quand on le connaît, sinon le nom complet du parti ; ordre gauche → droite initial et couleur d'après le sigle
    /// (valeur de départ, modifiable en base : l'import ne l'écrase pas). Les indépendants forment le groupe « Ind ».
    /// </summary>
    public static GroupeRow Groupe(string maison, int legislature, PartiIe p)
    {
        var (sigle, ordre) = Partis.TryGetValue(p.Code, out var c) ? c : (p.Nom.Length <= 8 ? p.Nom : p.Nom, 50);
        var couleur = Palette[(ordre < 50 ? ordre - 1 : Math.Abs(p.Code.GetHashCode()) % Palette.Length) % Palette.Length];
        return new GroupeRow(IdGroupe(maison, legislature, p.Code), IdChambre(maison), legislature, sigle, p.Nom, couleur, ordre);
    }

    /// <summary>Parti d'un membre à une date : l'appartenance qui couvre le jour (sinon la plus récente commencée avant ; null = aucun parti connu).</summary>
    public static PartiIe? PartiA(MembreIe m, DateOnly date) =>
        m.Partis.Where(p => p.Debut <= date && (p.Fin is null || p.Fin >= date)).OrderByDescending(p => p.Debut).FirstOrDefault();

    /// <summary>Position d'après la liste : ta = pour, nil = contre, staon = abstention.</summary>
    public static string? Position(string liste) => liste switch
    {
        "ta" => Positions.Pour,
        "nil" => Positions.Contre,
        "staon" => Positions.Abstention,
        _ => null,
    };

    /// <summary>« That the Bill do now pass. » : vote final d'un projet de loi (<b>calculé</b> d'après la question posée, la source ne le signale pas).</summary>
    public static bool EstVoteFinal(string question) => Regex.IsMatch(question, @"\bBill do now pass\b", RegexOptions.IgnoreCase);

    /// <summary>
    /// Convertit une division. Totaux = OFFICIELS (tallies) ; positions = listes nominatives ; « Carried » / « Lost » → adopté / rejeté (autre valeur : pas de résultat, jamais deviné).
    /// Un membre absent de l'annuaire est compté dans la note et ignoré. L'écart entre totaux et listes est dit dans la note.
    /// </summary>
    public static ScrutinAvecVotes VersScrutin(DivisionIe d, IReadOnlyDictionary<string, MembreIe> membres, HashSet<string> inconnus)
    {
        var numero = Numero(d);
        var id = IdScrutin(d.Maison, d.Legislature, numero);
        var lignes = new List<VoteRow>();
        var sansFiche = 0;
        foreach (var v in d.Votes)
        {
            if (Position(v.Liste) is not { } pos) continue;
            if (!membres.TryGetValue(v.Code, out var m)) { inconnus.Add(v.Code); sansFiche++; continue; }
            var parti = PartiA(m, d.Date);
            lignes.Add(new VoteRow(id, IdMembre(d.Maison, v.Code), parti is null ? null : IdGroupe(d.Maison, d.Legislature, parti.Code), pos, false, false, pos));
        }
        var pour = lignes.Count(l => l.Position == Positions.Pour);
        var contre = lignes.Count(l => l.Position == Positions.Contre);
        var abst = lignes.Count(l => l.Position == Positions.Abstention);
        var notes = new List<string> { $"Sitting of {d.Date:yyyy-MM-dd}, vote {NumeroVote(d.VoteId)}." };
        if (sansFiche > 0) notes.Add($"{sansFiche} member(s) who voted have no entry in the members directory used by this site and are not shown as dots.");
        else if (pour != d.Ta || contre != d.Nil || abst != d.Staon) notes.Add($"Official count: {d.Ta} Tá, {d.Nil} Níl, {d.Staon} Staon; member list: {pour} Tá, {contre} Níl, {abst} Staon.");
        var manquants = Math.Max(0, d.Ta + d.Nil + d.Staon - lignes.Count);
        var sort = d.Issue switch { "Carried" => "adopté", "Lost" => "rejeté", _ => null };
        var titre = d.Debat.Length > 0 ? d.Debat : d.Question.Length > 0 ? d.Question : $"Division {d.VoteId}";
        var scrutin = new ScrutinRow(
            id, IdChambre(d.Maison), d.Legislature, numero, d.Date, titre, "roll call", sort, null, EstVoteFinal(d.Question),
            d.Ta + d.Nil + d.Staon, d.Ta, d.Nil, d.Staon, 0, null, manquants, string.Join(" ", notes),
            d.Question.Length > 0 ? d.Question : null, "Simple majority of the votes cast (Tá against Níl)", d.Ta + d.Nil, null, titre);
        return new ScrutinAvecVotes(scrutin, lignes);
    }

    public void Dispose() => _http.Dispose();
}
