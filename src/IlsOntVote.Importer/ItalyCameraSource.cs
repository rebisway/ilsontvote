using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IlsOntVote.Importer;

/// <summary>Une votation de la Chambre des députés italienne (métadonnées et totaux officiels).</summary>
public sealed record VotazioneIt(
    string Uri, string Id, int Legislature, int Seduta, int NumeroDansSeduta, DateOnly Date, string Denominazione, string Description, string Tipo,
    int Favorevoli, int Contrari, int Astenuti, int Presenti, int Votanti, int Maggioranza, bool Approvata, bool Finale, bool Fiducia, bool Segreta, string? Fiche);

/// <summary>Un vote individuel brut : identifiant du député, type de vote (libellé de la source), groupe au moment du vote.</summary>
public sealed record VotoIt(string VotazioneUri, string DeputeId, string Type, string? GroupeUri, string? GroupeSigle);

/// <summary>Un député de l'annuaire de la législature (noms en MAJUSCULES dans la source).</summary>
public sealed record DeputeIt(string Id, string Prenom, string Nom);

/// <summary>
/// Chambre des députés d'Italie, SOURCE OFFICIELLE : point SPARQL <c>dati.camera.it/sparql</c> (open data de la Chambre, licence CC BY-SA 4.0), sans clé ni blocage. Une « votazione » porte les
/// totaux officiels ; chaque « voto » relie un député, son groupe du jour et son vote (Favorevole / Contrario / Astensione / Non ha votato). Pièges : (1) le point d'accès renvoie au plus
/// 10 000 lignes par requête : les votes se lisent par lots de quelques votations ; (2) chaque triplet est renvoyé en double : <c>DISTINCT</c> ; (3) au scrutin SECRET, la source ne donne
/// que « Ha votato » (a voté, sans dire quoi) : aucune position individuelle n'est affichable ; (4) les descriptions sont HTML échappé DEUX fois (« &amp;amp;agrave; ») ; (5) « votanti » =
/// pour + contre : les abstentions sont comptées à part (« presenti » = votanti + abstentions).
/// </summary>
public sealed class ItalyCameraSource : IDisposable
{
    private const string Point = "https://dati.camera.it/sparql";
    private const string Prefixes = "PREFIX ocd: <http://dati.camera.it/ocd/> PREFIX dc: <http://purl.org/dc/elements/1.1/> PREFIX foaf: <http://xmlns.com/foaf/0.1/> PREFIX rdfs: <http://www.w3.org/2000/01/rdf-schema#> ";
    /// <summary>Limite de lignes d'une réponse du point d'accès (au-delà, la réponse est tronquée sans erreur).</summary>
    private const int LimiteLignes = 10000;
    private readonly HttpClient _http = Telechargement.CreerClient(TimeSpan.FromMinutes(3));

    /// <summary>Législature courante (XIXe, depuis octobre 2022).</summary>
    public const int Courante = 19;

    /// <summary>Bornes des législatures importables (première votation → dernière ; null = en cours).</summary>
    public static readonly IReadOnlyDictionary<int, (DateOnly Debut, DateOnly? Fin)> Legislatures = new Dictionary<int, (DateOnly, DateOnly?)>
    {
        [19] = (new DateOnly(2022, 10, 13), null),
        [18] = (new DateOnly(2018, 3, 23), new DateOnly(2022, 10, 12)),
        [17] = (new DateOnly(2013, 3, 15), new DateOnly(2018, 3, 22)),
    };

    public static string UriLegislature(int l) => $"http://dati.camera.it/ocd/legislatura.rdf/repubblica_{l}";

    /// <summary>Exécute une requête SPARQL (JSON) ; trois essais sur erreur réseau ou 5xx.</summary>
    public async Task<List<Dictionary<string, string>>> RequeteAsync(string sparql)
    {
        var url = Point + "?query=" + Uri.EscapeDataString(Prefixes + sparql);
        for (var essai = 1; ; essai++)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Accept.ParseAdd("application/sparql-results+json");
                using var r = await _http.SendAsync(req);
                r.EnsureSuccessStatusCode();
                using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
                var lignes = new List<Dictionary<string, string>>();
                foreach (var b in doc.RootElement.GetProperty("results").GetProperty("bindings").EnumerateArray())
                {
                    var d = new Dictionary<string, string>();
                    foreach (var p in b.EnumerateObject()) d[p.Name] = p.Value.GetProperty("value").GetString() ?? "";
                    lignes.Add(d);
                }
                return lignes;
            }
            catch (Exception ex) when (essai < 3 && ex is HttpRequestException or TaskCanceledException or JsonException) { await Task.Delay(2000 * essai); }
        }
    }

    /// <summary>
    /// Votations d'une législature entre deux dates, par tranches de trois mois (chaque réponse reste sous la limite de lignes). Par défaut seulement les votes FINAUX et les questions de
    /// CONFIANCE (environ 480 sur 20 800 pour la XIXe : l'ensemble pèserait ~7 millions de votes individuels) ; <paramref name="tout"/> importe aussi amendements, ordres du jour, motions…
    /// </summary>
    public async Task<List<VotazioneIt>> ListerVotazioniAsync(int legislature, DateOnly depuis, DateOnly jusqua, bool tout)
    {
        var resultat = new Dictionary<string, VotazioneIt>();
        for (var debut = depuis; debut <= jusqua;)
        {
            var fin = debut.AddMonths(3) > jusqua.AddDays(1) ? jusqua.AddDays(1) : debut.AddMonths(3);
            var filtre = tout ? "" : "FILTER(?fin = 1 || ?fid = 1)";
            var q = $@"SELECT DISTINCT ?v ?d ?den ?des ?tipo ?fav ?con ?ast ?pre ?vot ?mag ?app ?fin ?fid ?seg ?rel WHERE {{
  ?v a ocd:votazione ; ocd:rif_leg <{UriLegislature(legislature)}> ; dc:date ?d ; ocd:votazioneFinale ?fin ; ocd:richiestaFiducia ?fid ; ocd:votazioneSegreta ?seg .
  FILTER(?d >= '{debut:yyyyMMdd}' && ?d < '{fin:yyyyMMdd}')
  OPTIONAL {{ ?v rdfs:label ?den }} OPTIONAL {{ ?v dc:description ?des }} OPTIONAL {{ ?v dc:type ?tipo }}
  OPTIONAL {{ ?v ocd:favorevoli ?fav }} OPTIONAL {{ ?v ocd:contrari ?con }} OPTIONAL {{ ?v ocd:astenuti ?ast }} OPTIONAL {{ ?v ocd:presenti ?pre }}
  OPTIONAL {{ ?v ocd:votanti ?vot }} OPTIONAL {{ ?v ocd:maggioranza ?mag }} OPTIONAL {{ ?v ocd:approvato ?app }} OPTIONAL {{ ?v dc:relation ?rel }}
  {filtre} }}";
            var lignes = await RequeteAsync(q);
            if (lignes.Count >= LimiteLignes) throw new InvalidOperationException($"Réponse tronquée ({lignes.Count} lignes) pour {debut:yyyy-MM-dd} → {fin:yyyy-MM-dd} : réduire la tranche de dates.");
            foreach (var l in lignes)
            {
                var v = Convertir(l, legislature);
                if (v is not null) resultat[v.Uri] = v;
            }
            debut = fin;
        }
        return resultat.Values.OrderBy(v => v.Date).ThenBy(v => v.Seduta).ThenBy(v => v.NumeroDansSeduta).ToList();
    }

    /// <summary>Convertit une ligne de résultat en votation ; null si l'identifiant ou la date sont illisibles.</summary>
    public static VotazioneIt? Convertir(IReadOnlyDictionary<string, string> l, int legislature)
    {
        var uri = l.GetValueOrDefault("v") ?? "";
        var m = Regex.Match(uri, @"votazione\.rdf/vs(\d+)_(\d+)_(\d+)$");
        if (!m.Success || !DateOnly.TryParseExact(l.GetValueOrDefault("d"), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return null;
        int N(string k) => int.TryParse(l.GetValueOrDefault(k), NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) ? x : 0;
        bool B(string k) => l.GetValueOrDefault(k) == "1";
        return new VotazioneIt(uri, $"vs{m.Groups[1].Value}_{m.Groups[2].Value}_{m.Groups[3].Value}", legislature, int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), date,
            Nettoyer(l.GetValueOrDefault("den")), Nettoyer(l.GetValueOrDefault("des")), Nettoyer(l.GetValueOrDefault("tipo")),
            N("fav"), N("con"), N("ast"), N("pre"), N("vot"), N("mag"), B("app"), B("fin"), B("fid"), B("seg"), EnHttps(l.GetValueOrDefault("rel")));
    }

    /// <summary>La source publie la fiche officielle en « http:// » alors que le site répond en HTTPS : le lien n'est affiché que s'il est en HTTPS (voir <c>Scrutin.UrlOfficielle</c>).</summary>
    public static string? EnHttps(string? url) => url is not null && url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? "https://" + url[7..] : url;

    /// <summary>Texte de la source : HTML échappé DEUX fois (« &amp;amp;agrave; » → « à »), espaces normalisés.</summary>
    public static string Nettoyer(string? t) => string.IsNullOrWhiteSpace(t) ? "" : Regex.Replace(WebUtility.HtmlDecode(WebUtility.HtmlDecode(t)), @"\s+", " ").Trim();

    /// <summary>
    /// Votes individuels de quelques votations (VALUES). Le lot doit rester petit : ~400 lignes par votation, 10 000 lignes au plus par réponse ; une réponse pleine (donc peut-être tronquée) est refusée.
    /// </summary>
    public async Task<List<VotoIt>> LireVotesAsync(IReadOnlyCollection<string> votazioniUri)
    {
        var valeurs = string.Join(" ", votazioniUri.Select(u => $"<{u}>"));
        var q = $@"SELECT DISTINCT ?v ?d ?t ?g ?s WHERE {{
  VALUES ?v {{ {valeurs} }}
  ?x ocd:rif_votazione ?v ; ocd:rif_deputato ?d ; dc:type ?t .
  OPTIONAL {{ ?x ocd:rif_gruppoParlamentare ?g }} OPTIONAL {{ ?x ocd:siglaGruppo ?s }} }}";
        var lignes = await RequeteAsync(q);
        if (lignes.Count >= LimiteLignes) throw new InvalidOperationException($"Réponse pleine ({lignes.Count} lignes) pour {votazioniUri.Count} votations : réduire la taille du lot.");
        var votes = new List<VotoIt>();
        foreach (var l in lignes)
        {
            var m = Regex.Match(l.GetValueOrDefault("d") ?? "", @"deputato\.rdf/d(\d+)_\d+$");
            if (m.Success) votes.Add(new VotoIt(l["v"], m.Groups[1].Value, l.GetValueOrDefault("t") ?? "", l.GetValueOrDefault("g"), l.GetValueOrDefault("s")));
        }
        return votes;
    }

    /// <summary>Annuaire des députés d'une législature (prénom et nom en majuscules dans la source).</summary>
    public async Task<List<DeputeIt>> LireDeputesAsync(int legislature)
    {
        var lignes = await RequeteAsync($@"SELECT DISTINCT ?d ?p ?n WHERE {{ ?d a ocd:deputato ; ocd:rif_leg <{UriLegislature(legislature)}> ; foaf:firstName ?p ; foaf:surname ?n }}");
        var liste = new Dictionary<string, DeputeIt>();
        foreach (var l in lignes)
        {
            var m = Regex.Match(l["d"], @"deputato\.rdf/d(\d+)_\d+$");
            if (m.Success) liste[m.Groups[1].Value] = new DeputeIt(m.Groups[1].Value, CasseNom(l["p"]), CasseNom(l["n"]));
        }
        return liste.Values.ToList();
    }

    /// <summary>« DE LUCA » → « De Luca », « D'ALESSANDRO » → « D'Alessandro », « MARIA-GRAZIA » → « Maria-Grazia ».</summary>
    public static string CasseNom(string s)
    {
        var sb = new StringBuilder(s.Length);
        var debut = true;
        foreach (var c in s.Trim().ToLowerInvariant())
        {
            sb.Append(debut ? char.ToUpperInvariant(c) : c);
            debut = c is ' ' or '-' or '\'' or '’';
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------ conversion en scrutin

    public static string IdScrutin(int legislature, int numero) => $"ITCAMERA-{legislature}-{numero}";
    public static string IdMembre(string deputeId) => $"ITCAMERA-{deputeId}";
    public static int Numero(VotazioneIt v) => v.Seduta * 10000 + v.NumeroDansSeduta;

    /// <summary>Groupe : identifiant stable par législature (« ITCAMERA-19-gr4134 »), sigle de la source, ordre gauche → droite initial (jamais écrasé par l'import).</summary>
    public static GroupeRow? Groupe(VotoIt v, int legislature)
    {
        var m = Regex.Match(v.GroupeUri ?? "", @"gr(\d+)$");
        if (!m.Success) return null;
        // « ------ » = député sans groupe (la source n'a pas de nom pour lui) ; sigles de la XIXe vus à l'import : AVS, PD-IDP, M5S, AIV-RE / IV-CR / APERRE / IVICRE (centre, même formation sous des noms successifs),
        // NM-M-C, FI-PPE, LEGA, FDI.
        var sigle = string.IsNullOrWhiteSpace(v.GroupeSigle) ? "?" : v.GroupeSigle.Trim();
        if (sigle.StartsWith("---")) sigle = "No group";
        var (ordre, couleur) = sigle.ToUpperInvariant() switch
        {
            "AVS" => (1, "#3f9e5b"), "PD-IDP" or "PD" => (2, "#d4543a"), "M5S" => (3, "#e0b400"), "A-IV-RE" or "AIV-RE" or "AZ-PER-RE" or "IV-CR" or "APERRE" or "IVICRE" => (4, "#7a5cc2"),
            "MISTO" or "NO GROUP" => (5, "#8a8f98"), "NM-M" or "NM-M-C" or "NM(N-C-U-I)-M" => (6, "#2a9d8f"), "FI-PPE" => (7, "#3b82c4"), "LEGA" => (8, "#1f7a4a"), "FDI" => (9, "#5a3e2b"), _ => (50, "#8a8f98"),
        };
        return new GroupeRow($"ITCAMERA-{legislature}-gr{m.Groups[1].Value}", Chambres.ItCamera, legislature, sigle, sigle, couleur, ordre);
    }

    /// <summary>Position d'après le type de vote ; null = pas un vote exprimé (« Non ha votato », « Ha votato » au scrutin secret…).</summary>
    public static string? Position(string type) => type.Trim().ToLowerInvariant() switch
    {
        "favorevole" => Positions.Pour,
        "contrario" => Positions.Contre,
        "astensione" => Positions.Abstention,
        _ => null,
    };

    /// <summary>
    /// Convertit une votation et ses votes en scrutin. Totaux = OFFICIELS (ceux de la Chambre) ; l'écart avec la liste nominative est dit dans la note. Au scrutin SECRET la source ne donne pas
    /// les positions : le scrutin garde ses totaux, sans votes individuels, et est marqué « décompte incomplet » (exclu des taux de participation). Résultat = champ « approvato » officiel.
    /// </summary>
    public static ScrutinAvecVotes VersScrutin(VotazioneIt v, IReadOnlyList<VotoIt> votes, IReadOnlySet<string> deputesConnus, HashSet<string> inconnus)
    {
        var id = IdScrutin(v.Legislature, Numero(v));
        var lignes = new List<VoteRow>();
        var sansFiche = 0;
        if (!v.Segreta)
        {
            foreach (var x in votes)
            {
                if (Position(x.Type) is not { } pos) continue;
                if (!deputesConnus.Contains(x.DeputeId)) { inconnus.Add(x.DeputeId); sansFiche++; continue; }
                lignes.Add(new VoteRow(id, IdMembre(x.DeputeId), Groupe(x, v.Legislature)?.Id, pos, false, false, pos));
            }
        }
        var pour = lignes.Count(l => l.Position == Positions.Pour);
        var contre = lignes.Count(l => l.Position == Positions.Contre);
        var abst = lignes.Count(l => l.Position == Positions.Abstention);
        var attendus = v.Favorevoli + v.Contrari + v.Astenuti;
        var notes = new List<string> { $"Sitting {v.Seduta}, vote {v.NumeroDansSeduta}." };
        if (v.Segreta) notes.Add("Secret ballot: the Chamber publishes only the official totals, not how each deputy voted.");
        else if (sansFiche > 0) notes.Add($"{sansFiche} deputy(ies) who voted have no entry in the members directory used by this site and are not shown as dots.");
        else if (pour != v.Favorevoli || contre != v.Contrari || abst != v.Astenuti) notes.Add($"Official count: {v.Favorevoli} for, {v.Contrari} against, {v.Astenuti} abstentions; member list: {pour} for, {contre} against, {abst} abstentions.");
        if (v.Fiducia) notes.Add("Question of confidence (the Government stakes its survival on the vote).");
        var manquants = v.Segreta ? attendus : Math.Max(0, attendus - lignes.Count);
        var titre = v.Description.Length > 0 && v.Denominazione.Length > 0 && !v.Description.StartsWith(v.Denominazione, StringComparison.OrdinalIgnoreCase) ? $"{v.Denominazione}: {v.Description}" : v.Description.Length > 0 ? v.Description : v.Denominazione;
        if (titre.Length == 0) titre = $"Vote {v.Id}";
        var scrutin = new ScrutinRow(
            id, Chambres.ItCamera, v.Legislature, Numero(v), v.Date, titre, "roll call", v.Approvata ? "adopté" : "rejeté", v.Fiche, v.Finale,
            v.Presenti, v.Favorevoli, v.Contrari, v.Astenuti, 0, null, manquants, string.Join(" ", notes),
            v.Tipo.Length > 0 ? v.Tipo : null, "Simple majority of those voting (abstentions not counted)", v.Votanti, v.Maggioranza, titre, v.Fiducia);   // question de confiance : drapeau OFFICIEL de la source (richiestaFiducia)
        return new ScrutinAvecVotes(scrutin, lignes);
    }

    public void Dispose() => _http.Dispose();
}
