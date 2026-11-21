using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IlsOntVote.Importer;

/// <summary>Un vote de l'Assemblée fédérale (Conseil national ou Conseil des États). Les totaux ne sont PAS dans la source : ils se calculent à partir des bulletins.</summary>
public sealed record VoteCh(
    long Id, int Legislature, int Session, string SessionNom, string AffaireNumero, string AffaireCourt, string Affaire, string Projet, string Objet,
    string SensOui, string SensNon, DateOnly Date, DateTime HeureUtc);

/// <summary>Bulletin d'un conseiller : un par conseiller et par vote (même excusé). <c>Decision</c> : 1 oui, 2 non, 3 abstention, 5 n'a pas pris part, 6 excusé, 7 le président ne vote pas.</summary>
public sealed record BulletinCh(long IdVote, int Personne, string Prenom, string Nom, string Canton, string GroupeCode, string GroupeNom, string? GroupeCouleur, int Decision);

/// <summary>Une période de mandat d'un conseiller dans un conseil (annuaire <c>MemberCouncil</c>) ; <c>Conseil</c> = « CN » (national) ou « CE » (des États).</summary>
public sealed record MandatCh(int Personne, string Prenom, string Nom, string Conseil, string Canton, DateOnly Debut, DateOnly? Fin);

/// <summary>
/// Assemblée fédérale suisse, SOURCE OFFICIELLE : service OData des Services du Parlement (<c>ws.parlament.ch/odata.svc</c>, JSON, sans clé constatée). Conditions d'utilisation (page « open data »
/// du Parlement) : données non modifiées, source citée (« Parlamentsdienste der Bundesversammlung, Bern ») avec la date de téléchargement, pas de présentation comme publication officielle.
/// Pièges constatés : (1) le serveur TRONQUE SILENCIEUSEMENT les réponses (plafond de 20 000 lignes pour <c>Voting</c>, 1 000 pour <c>MemberCouncil</c>) sans lien « page suivante » : on pagine
/// toujours par pages de 1 000 avec <c>$orderby=ID</c> ; (2) le pare-feu rejette certaines requêtes (<c>$count</c> avec filtre) : on s'en passe ; (3) une entité existe en plusieurs langues (clé ID + Language) :
/// on lit les lignes « FR » (la ligne « EN » est peu fiable) ; (4) <c>Vote</c> n'a ni totaux ni résultat ni conseil : totaux et résultat sont CALCULÉS d'après les bulletins, le conseil se déduit de
/// l'annuaire ; (5) les votes de l'Assemblée fédérale réunie (élections) mêlent les deux conseils : ils ne sont pas importés.
/// </summary>
public sealed class SwitzerlandSource : IDisposable
{
    private const string Base = "https://ws.parlament.ch/odata.svc/";
    private const int Page = 1000;
    private readonly HttpClient _http;

    public SwitzerlandSource()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("IlsOntVote", "1.0"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public static string IdChambre(string conseil) => conseil == "CE" ? Chambres.ChSr : Chambres.ChNr;
    public static string IdMembre(string conseil, int personne) => $"{IdChambre(conseil)}-{personne}";
    public static string IdScrutin(string conseil, int legislature, long vote) => $"{IdChambre(conseil)}-{legislature}-{vote}";
    public static string IdGroupe(string conseil, int legislature, string code) => $"{IdChambre(conseil)}-{legislature}-{code}";

    /// <summary>Adresse OFFICIELLE de l'affaire (Curia Vista, en français), vérifiée en HTTP 200.</summary>
    public static string LienAffaire(string numero) => $"https://www.parlament.ch/fr/ratsbetrieb/suche-curia-vista/geschaeft?AffairId={numero}";

    /// <summary>Lit toutes les pages d'une requête OData (pages de 1 000, ordre stable par ID) : jamais de lecture unique, le serveur tronquerait sans prévenir.</summary>
    private async Task<List<JsonElement>> LireToutAsync(string chemin)
    {
        var toutes = new List<JsonElement>();
        for (var skip = 0; ; skip += Page)
        {
            var url = $"{Base}{chemin}{(chemin.Contains('?') ? "&" : "?")}$top={Page}&$skip={skip}&$orderby=ID&$format=json";
            JsonDocument doc;
            for (var essai = 1; ; essai++)
            {
                try
                {
                    using var r = await _http.GetAsync(url);
                    r.EnsureSuccessStatusCode();
                    doc = await JsonDocument.ParseAsync(await r.Content.ReadAsStreamAsync());
                    break;
                }
                catch (Exception) when (essai < 3) { await Task.Delay(2000 * essai); }
            }
            using (doc)
            {
                var d = doc.RootElement.GetProperty("d");
                var lignes = d.ValueKind == JsonValueKind.Array ? d : d.GetProperty("results");
                var n = 0;
                foreach (var l in lignes.EnumerateArray()) { toutes.Add(l.Clone()); n++; }
                if (n < Page) return toutes;
            }
            await Task.Delay(150);   // politesse envers le service
        }
    }

    /// <summary>Numéro de la législature la plus récente qui a des votes (celle du dernier vote enregistré).</summary>
    public async Task<int> LegislatureCouranteAsync()
    {
        using var r = await _http.GetAsync($"{Base}Vote?$filter=Language%20eq%20%27FR%27&$orderby=ID%20desc&$top=1&$select=IdLegislativePeriod&$format=json");
        r.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await r.Content.ReadAsStreamAsync());
        var d = doc.RootElement.GetProperty("d");
        var l = (d.ValueKind == JsonValueKind.Array ? d : d.GetProperty("results"))[0];
        return int.Parse(l.GetProperty("IdLegislativePeriod").ToString());
    }

    /// <summary>Tous les votes d'une législature (les deux conseils mêlés).</summary>
    public async Task<IReadOnlyList<VoteCh>> LireVotesAsync(int legislature)
    {
        var lignes = await LireToutAsync($"Vote?$filter=IdLegislativePeriod%20eq%20{legislature}%20and%20Language%20eq%20%27FR%27");
        var liste = new List<VoteCh>();
        foreach (var l in lignes) if (ConvertirVote(l) is { } v) liste.Add(v);
        return liste;
    }

    /// <summary>Bulletins d'un lot de votes. Un lot de 40 votes pèse au plus 8 000 lignes (200 conseillers par vote national) : sous le plafond de 20 000 du serveur, et la pagination couvre le reste.</summary>
    public async Task<IReadOnlyList<BulletinCh>> LireBulletinsAsync(IEnumerable<long> votes)
    {
        var filtre = string.Join("%20or%20", votes.Select(v => $"IdVote%20eq%20{v}"));
        var lignes = await LireToutAsync($"Voting?$filter=({filtre})%20and%20Language%20eq%20%27FR%27");
        var liste = new List<BulletinCh>();
        foreach (var l in lignes) if (ConvertirBulletin(l) is { } b) liste.Add(b);
        return liste;
    }

    /// <summary>Annuaire des conseillers : toutes les périodes de mandat de tous les temps (quelques milliers de lignes).</summary>
    public async Task<IReadOnlyList<MandatCh>> LireMandatsAsync()
    {
        var lignes = await LireToutAsync("MemberCouncil?$filter=Language%20eq%20%27FR%27");
        var liste = new List<MandatCh>();
        foreach (var l in lignes) if (ConvertirMandat(l) is { } m) liste.Add(m);
        return liste;
    }

    // ---------------------------------------------------------------------------------------------------------------- conversions (statiques : testées sans réseau)

    private static string S(JsonElement e, string nom) => e.TryGetProperty(nom, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString().Trim() : "";

    private static readonly Regex DateOdata = new(@"/Date\((-?\d+)(?:[+-]\d{4})?\)/", RegexOptions.Compiled);

    /// <summary>« /Date(1790934036907)/ » (millisecondes depuis 1970, UTC) → instant UTC ; null si la forme est inattendue.</summary>
    public static DateTime? InstantOdata(string s)
    {
        var m = DateOdata.Match(s);
        return m.Success && long.TryParse(m.Groups[1].Value, out var ms) ? DateTime.UnixEpoch.AddMilliseconds(ms) : null;
    }

    private static readonly TimeZoneInfo Zurich = TimeZoneInfo.FindSystemTimeZoneById("Europe/Zurich");

    /// <summary>Jour (à l'heure de Zurich) d'un instant UTC : un vote tardif ne bascule pas au lendemain.</summary>
    public static DateOnly JourZurich(DateTime utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zurich));

    public static VoteCh? ConvertirVote(JsonElement l)
    {
        if (InstantOdata(S(l, "VoteEnd")) is not { } fin) return null;
        if (!long.TryParse(S(l, "ID"), out var id) || !int.TryParse(S(l, "IdLegislativePeriod"), out var leg)) return null;
        _ = int.TryParse(S(l, "IdSession"), out var session);
        return new VoteCh(id, leg, session, S(l, "SessionName"), S(l, "BusinessNumber"), S(l, "BusinessShortNumber"), S(l, "BusinessTitle"), S(l, "BillTitle"), S(l, "Subject"),
            S(l, "MeaningYes"), S(l, "MeaningNo"), JourZurich(fin), fin);
    }

    public static BulletinCh? ConvertirBulletin(JsonElement l)
    {
        if (!long.TryParse(S(l, "IdVote"), out var vote) || !int.TryParse(S(l, "PersonNumber"), out var p) || !int.TryParse(S(l, "Decision"), out var dec)) return null;
        var couleur = S(l, "ParlGroupColour");
        return new BulletinCh(vote, p, S(l, "FirstName"), S(l, "LastName"), S(l, "Canton"), S(l, "ParlGroupCode"), S(l, "ParlGroupName"), couleur.StartsWith('#') ? couleur : null, dec);
    }

    public static MandatCh? ConvertirMandat(JsonElement l)
    {
        if (!int.TryParse(S(l, "PersonNumber"), out var p) || InstantOdata(S(l, "DateJoining")) is not { } debut) return null;
        var conseil = S(l, "CouncilAbbreviation");
        if (conseil is not ("CN" or "CE")) return null;   // l'Assemblée fédérale réunie et les autres organes n'ont pas de votes importés
        DateOnly? fin = InstantOdata(S(l, "DateLeaving")) is { } f ? DateOnly.FromDateTime(f) : null;
        return new MandatCh(p, S(l, "FirstName"), S(l, "LastName"), conseil, S(l, "CantonAbbreviation"), DateOnly.FromDateTime(debut), fin);
    }

    /// <summary>
    /// Conseil d'un vote : celui du MANDAT en cours à la date du vote pour la majorité des conseillers qui y ont un bulletin. Null si les deux conseils se mêlent (Assemblée fédérale réunie,
    /// élections) ou si aucun bulletin ne se rattache à un mandat connu : le vote n'est alors pas importé (jamais deviné).
    /// </summary>
    public static string? ConseilDuVote(IReadOnlyList<BulletinCh> bulletins, DateOnly date, ILookup<int, MandatCh> mandats)
    {
        int cn = 0, ce = 0;
        foreach (var b in bulletins)
        {
            var m = mandats[b.Personne].FirstOrDefault(x => x.Debut <= date && (x.Fin is null || x.Fin >= date));
            if (m is null) continue;
            if (m.Conseil == "CN") cn++; else ce++;
        }
        if (cn + ce == 0) return null;
        // Un seul conseil, à une poignée de bulletins près (mandats mal datés) ; un mélange franc = Assemblée fédérale réunie.
        if (cn > 0 && ce > 0 && Math.Min(cn, ce) * 10 > cn + ce) return null;
        return cn >= ce ? "CN" : "CE";
    }

    /// <summary>Position d'après le code de décision : 1 oui, 2 non, 3 abstention ; 7 = le président ne vote pas (non-votant déclaré) ; 5 et 6 (n'a pas pris part, excusé) = absent, non stocké (aucune raison affichée).</summary>
    public static string? Position(int decision) => decision switch
    {
        1 => Positions.Pour,
        2 => Positions.Contre,
        3 => Positions.Abstention,
        7 => Positions.NonVotant,
        _ => null,
    };

    /// <summary>« Vote final » (Schlussabstimmung) et « Vote sur l'ensemble » (Gesamtabstimmung) : vote sur l'ensemble d'un projet (<b>calculé</b> d'après l'objet du vote).</summary>
    public static bool EstVoteSurEnsemble(string objet) =>
        Regex.IsMatch(objet, @"^(vote final|vote sur l.ensemble|vote d.ensemble|schlussabstimmung|gesamtabstimmung|votazione finale|votazione sul complesso)", RegexOptions.IgnoreCase);

    private static readonly string[] Palette = ["#c0392b", "#27ae60", "#8bc34a", "#f39c12", "#2980b9", "#2c3e50", "#8e44ad", "#7f8c8d"];

    /// <summary>Ordre gauche → droite initial d'après le sigle du groupe (gauche 1 … droite 6 ; autres 50). Valeur de départ modifiable en base.</summary>
    public static int OrdreGroupe(string code) => code.ToUpperInvariant() switch
    {
        "G" => 1, "S" => 2, "GL" or "CEg" => 3, "CE" or "M-E" or "C" or "BD" => 4, "RL" or "R" or "FDP" => 5, "V" or "UDC" or "SVP" => 6, _ => 50,
    };

    /// <summary>Groupe parlementaire d'une législature et d'un conseil, tel que porté par les bulletins (le groupe est donné sur chaque bulletin : celui du conseiller au moment du vote).</summary>
    public static GroupeRow Groupe(string conseil, int legislature, string code, string nom, string? couleur)
    {
        var ordre = OrdreGroupe(code);
        var c = couleur ?? Palette[(ordre < 50 ? ordre - 1 : Math.Abs(code.GetHashCode()) % Palette.Length) % Palette.Length];
        return new GroupeRow(IdGroupe(conseil, legislature, code), IdChambre(conseil), legislature, code.Length > 0 ? code : "—", nom.Length > 0 ? nom : "Sans groupe", c, ordre);
    }

    /// <summary>
    /// Convertit un vote et ses bulletins. TOTAUX ET RÉSULTAT CALCULÉS (la source n'en donne aucun) : « adopté » si les oui l'emportent sur les non, rien d'indiqué à égalité. Les absents (codes 5 et 6) ne sont pas
    /// stockés. Un conseiller sans fiche dans l'annuaire est compté dans la note et ignoré.
    /// </summary>
    public static ScrutinAvecVotes VersScrutin(VoteCh v, string conseil, IReadOnlyList<BulletinCh> bulletins, IReadOnlySet<int> connus, HashSet<int> inconnus, bool totauxOfficiels = false)
    {
        var id = IdScrutin(conseil, v.Legislature, v.Id);
        var lignes = new List<VoteRow>();
        int oui = 0, non = 0, abst = 0, presidents = 0, absents = 0;
        foreach (var b in bulletins)
        {
            switch (b.Decision) { case 1: oui++; break; case 2: non++; break; case 3: abst++; break; case 7: presidents++; break; default: absents++; break; }
            if (Position(b.Decision) is not { } pos) continue;
            if (!connus.Contains(b.Personne)) { inconnus.Add(b.Personne); continue; }
            lignes.Add(new VoteRow(id, IdMembre(conseil, b.Personne), b.GroupeCode.Length > 0 ? IdGroupe(conseil, v.Legislature, b.GroupeCode) : null, pos, false, false, pos));
        }
        var notes = new List<string> { $"Vote of {TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(v.HeureUtc, DateTimeKind.Utc), Zurich):yyyy-MM-dd HH:mm}, {v.SessionNom}." };
        // Conseil national (OData) : aucun total. Conseil des États (API à clé) : décompte officiel fourni, identique au décompte des bulletins ; le résultat « adopté / rejeté », lui, reste calculé.
        notes.Add(totauxOfficiels
            ? "Totals as published by the Parliamentary Services; the adopted/rejected result is calculated by this site."
            : "The source gives no totals or result: they are calculated by this site from the individual ballots.");
        if (inconnus.Count > 0 && lignes.Count < oui + non + abst) notes.Add($"{oui + non + abst - lignes.Count} councillor(s) who voted have no entry in the members directory and are not shown as dots.");
        var sort = oui > non ? "adopté" : non > oui ? "rejeté" : null;
        var titre = v.Affaire.Length > 0 ? (v.Projet.Length > 0 && v.Projet != v.Affaire ? $"{v.Affaire} — {v.Projet}" : v.Affaire) : v.Projet.Length > 0 ? v.Projet : $"Vote {v.Id}";
        if (v.Objet.Length > 0) titre += $" ({v.Objet})";
        var scrutin = new ScrutinRow(
            id, IdChambre(conseil), v.Legislature, (int)v.Id, v.Date, titre, "roll call", sort, v.AffaireNumero.Length > 0 ? LienAffaire(v.AffaireNumero) : null, EstVoteSurEnsemble(v.Objet),
            oui + non + abst, oui, non, abst, presidents, null, 0, string.Join(" ", notes),
            v.Objet.Length > 0 ? v.Objet : null, "Simple majority of the votes cast (calculated)", oui + non, null, titre);
        return new ScrutinAvecVotes(scrutin, lignes);
    }

    /// <summary>
    /// Appartenances aux groupes DÉDUITES des bulletins : un intervalle par suite de votes consécutifs avec le même groupe (début = jour du premier vote, fin = veille du premier vote du groupe suivant,
    /// ouverte pour le dernier). La source ne publie pas d'historique daté exploitable ici : <b>calculé</b>, fidèle aux seuls votes importés.
    /// </summary>
    public static IReadOnlyList<AppartenanceRow> AppartenancesDeduites(string conseil, int legislature, IEnumerable<(int Personne, DateOnly Date, string Code)> votes)
    {
        var res = new List<AppartenanceRow>();
        foreach (var g in votes.Where(x => x.Code.Length > 0).GroupBy(x => x.Personne))
        {
            var suite = g.OrderBy(x => x.Date).ToList();
            var debut = suite[0].Date; var code = suite[0].Code;
            for (var i = 1; i <= suite.Count; i++)
            {
                if (i < suite.Count && suite[i].Code == code) continue;
                DateOnly? fin = i < suite.Count ? suite[i].Date.AddDays(-1) : null;
                res.Add(new AppartenanceRow(IdMembre(conseil, g.Key), IdGroupe(conseil, legislature, code), debut, fin));
                if (i < suite.Count) { debut = suite[i].Date; code = suite[i].Code; }
            }
        }
        return res;
    }

    // ------------------------------------------------------------------------------------------------------ Conseil des États : API REST à clé (ws.parl.ch)
    // Le flux OData ne contient AUCUN bulletin du Conseil des États. La nouvelle API (clé dans l'en-tête X-API-KEY, jamais commitée : <c>Parlament:ApiKey</c>) publie
    // <c>votes-cs</c> (depuis 2014, avec décompte officiel) et <c>votes-cs/{id}/votings</c> (une ligne par conseiller). Jetons de décision : + oui, - non, = ABSTENTION,
    // 0 « n'a pas pris part », E excusé, P le président ne vote pas (ne pas confondre « = » et « 0 » : le piège est dans la source).
    private const string BaseApi = "https://ws.parl.ch/api/v1/";
    private HttpClient? _api;

    /// <summary>Client de l'API à clé ; la clé ne figure jamais dans une adresse ni dans un journal.</summary>
    public void UtiliserCle(string cle)
    {
        _api = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        // Le pare-feu de l'API refuse « IlsOntVote/1.0 » (403) mais accepte un agent qui s'identifie avec une adresse : on reste honnête, sans se faire passer pour un navigateur.
        _api.DefaultRequestHeaders.UserAgent.ParseAdd("IlsOntVote-importer (+https://www.ilsontvote.org)");
        _api.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _api.DefaultRequestHeaders.Add("X-API-KEY", cle);
    }

    private async Task<JsonDocument> ApiAsync(string chemin)
    {
        for (var essai = 1; ; essai++)
        {
            try
            {
                using var r = await _api!.GetAsync(BaseApi + chemin);
                r.EnsureSuccessStatusCode();
                return await JsonDocument.ParseAsync(await r.Content.ReadAsStreamAsync());
            }
            catch (Exception) when (essai < 3) { await Task.Delay(2000 * essai); }
        }
    }

    /// <summary>Dates de début et de fin d'une législature (entité <c>LegislativePeriod</c> du flux OData).</summary>
    public async Task<(DateOnly Debut, DateOnly Fin)> BornesLegislatureAsync(int legislature)
    {
        using var r = await _http.GetAsync($"{Base}LegislativePeriod?$filter=ID%20eq%20{legislature}%20and%20Language%20eq%20%27FR%27&$format=json");
        r.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await r.Content.ReadAsStreamAsync());
        var d = doc.RootElement.GetProperty("d");
        var l = (d.ValueKind == JsonValueKind.Array ? d : d.GetProperty("results"))[0];
        return (DateOnly.FromDateTime(InstantOdata(S(l, "StartDate"))!.Value), DateOnly.FromDateTime(InstantOdata(S(l, "EndDate"))!.Value));
    }

    /// <summary>Votes du Conseil des États dans une période : toutes les pages (1 000 par page, ordre chronologique), filtrées sur la date locale.</summary>
    public async Task<IReadOnlyList<VoteCh>> LireVotesCsAsync(int legislature, DateOnly du, DateOnly au)
    {
        var liste = new List<VoteCh>();
        for (var page = 0; ; page++)
        {
            using var doc = await ApiAsync($"votes-cs?size=1000&page={page}&lang=fr");
            var racine = doc.RootElement;
            foreach (var l in racine.GetProperty("content").EnumerateArray())
            {
                if (!DateTime.TryParse(S(l, "start"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var debut)) continue;
                var jour = DateOnly.FromDateTime(debut);
                if (jour < du || jour > au || !long.TryParse(S(l, "id"), out var id)) continue;
                var aff = l.TryGetProperty("business", out var b) && b.ValueKind == JsonValueKind.Object ? b : default;
                var proj = l.TryGetProperty("draft", out var dr) && dr.ValueKind == JsonValueKind.Object ? dr : default;
                // L'heure locale de la source (Zurich) est reconvertie en UTC pour le journal du vote.
                var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(debut, DateTimeKind.Unspecified), Zurich);
                liste.Add(new VoteCh(id, legislature, 0, "", aff.ValueKind == JsonValueKind.Object ? S(aff, "number") : "", "", aff.ValueKind == JsonValueKind.Object ? S(aff, "title") : "",
                    proj.ValueKind == JsonValueKind.Object ? S(proj, "title") : "", S(l, "subject"), S(l, "meaningYes"), S(l, "meaningNo"), jour, utc));
            }
            if (!racine.GetProperty("page").GetProperty("hasNext").GetBoolean()) return liste;
            await Task.Delay(100);
        }
    }

    /// <summary>Bulletins d'un vote du Conseil des États (46 conseillers) ; jeton converti vers les codes de <see cref="BulletinCh.Decision"/>.</summary>
    public async Task<IReadOnlyList<BulletinCh>> LireBulletinsCsAsync(long vote)
    {
        using var doc = await ApiAsync($"votes-cs/{vote}/votings?size=100&lang=fr");
        var liste = new List<BulletinCh>();
        foreach (var l in doc.RootElement.GetProperty("content").EnumerateArray())
        {
            var m = l.GetProperty("assemblyMember");
            var g = m.TryGetProperty("parliamentaryGroup", out var gr) && gr.ValueKind == JsonValueKind.Object ? gr : default;
            var canton = m.TryGetProperty("canton", out var ca) && ca.ValueKind == JsonValueKind.Object ? S(ca, "abbreviation") : "";
            var dec = DecisionCs(S(l.GetProperty("decision"), "token"));
            liste.Add(new BulletinCh(vote, int.Parse(S(m, "id")), S(m, "firstName"), S(m, "lastName"), canton,
                g.ValueKind == JsonValueKind.Object ? S(g, "code") : "", g.ValueKind == JsonValueKind.Object ? S(g, "name") : "", null, dec));
        }
        return liste;
    }

    /// <summary>Jeton de l'API → code de décision : « + » oui, « - » non, « = » abstention, « P » président (non-votant), « E » excusé (6), le reste (« 0 ») n'a pas pris part (5).</summary>
    public static int DecisionCs(string jeton) => jeton switch { "+" => 1, "-" => 2, "=" => 3, "P" => 7, "E" => 6, _ => 5 };

    public void Dispose() { _http.Dispose(); _api?.Dispose(); }
}
