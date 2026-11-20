using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IlsOntVote.Importer;

/// <summary>Une votation du Sénat italien : totaux et résultat officiels (le vote de chaque sénateur se lit à part).</summary>
public sealed record VotazioneSen(
    string Uri, string Id, int Legislature, int Seduta, int Numero, DateOnly Date, string Titre, string Tipo,
    int Favorevoli, int Contrari, int Astenuti, int Presenti, int Votanti, int Maggioranza, string Esito, int CongedoMissione,
    string? DdlUri = null, string? DdlFase = null, string? DdlTitre = null);

/// <summary>Position d'un sénateur à une votation, d'après la liste où il figure (favorevole / contrario / astenuto).</summary>
public sealed record VotoSen(string VotazioneUri, string SenateurId, string Liste);

/// <summary>Sénateur d'une législature avec son mandat officiel (début, fin éventuelle).</summary>
public sealed record SenateurIt(string Id, string Prenom, string Nom, DateOnly Debut, DateOnly? Fin);

/// <summary>Appartenance d'un sénateur à un groupe, avec dates (fin vide = en cours).</summary>
public sealed record AdesioneSen(string SenateurId, string GroupeNumero, DateOnly Debut, DateOnly? Fin);

/// <summary>Nom d'un groupe (sigle court et titre) valable depuis une date : un groupe se renomme au fil du temps.</summary>
public sealed record DenominazioneSen(string GroupeNumero, string Sigle, string Titre, DateOnly Debut);

/// <summary>
/// Sénat d'Italie, SOURCE OFFICIELLE : point SPARQL <c>dati.senato.it/sparql</c> (open data du Sénat ; licence CC BY 3.0, voir la méthodologie). Chaque « votazione » porte les totaux
/// officiels, le résultat (« esito ») et trois listes de sénateurs : favorevole, contrario, astenuto. Contrairement à la Chambre, la source donne les MANDATS (début, fin) et les groupes DATÉS :
/// rien n'est déduit des votes. Pièges : (1) un vote final se reconnaît à son libellé « Votazione finale » (pas de champ dédié) ; (2) « votanti » INCLUT les abstentions (pour + contre + abstenus) ;
/// (3) les requêtes sont lentes : les votes se lisent par petits lots, les votations par tranches de dates ; (4) au scrutin secret la source ne donnerait pas de positions : totaux seuls ;
/// (5) les séances d'une législature ne sont pas triées : la date passe par la séance.
/// </summary>
public sealed class ItalySenateSource : IDisposable
{
    private const string Point = "https://dati.senato.it/sparql";
    private const string Prefixes = "PREFIX osr: <http://dati.senato.it/osr/> PREFIX ocd: <http://dati.camera.it/ocd/> PREFIX foaf: <http://xmlns.com/foaf/0.1/> PREFIX rdfs: <http://www.w3.org/2000/01/rdf-schema#> ";
    private const int LimiteLignes = 10000;
    private readonly HttpClient _http = Telechargement.CreerClient(TimeSpan.FromMinutes(5));

    /// <summary>Libellé d'un vote final dans la source (aucun champ booléen dédié).</summary>
    public const string LibelleVoteFinal = "Votazione finale";
    public const int Courante = 19;

    /// <summary>Bornes des législatures (première séance → dernière ; null = en cours).</summary>
    public static readonly IReadOnlyDictionary<int, (DateOnly Debut, DateOnly? Fin)> Legislatures = new Dictionary<int, (DateOnly, DateOnly?)>
    {
        [19] = (new DateOnly(2022, 10, 13), null),
        [18] = (new DateOnly(2018, 3, 23), new DateOnly(2022, 10, 12)),
        [17] = (new DateOnly(2013, 3, 15), new DateOnly(2018, 3, 22)),
    };

    /// <summary>Exécute une requête SPARQL (JSON) ; trois essais sur erreur réseau, délai ou 5xx.</summary>
    public async Task<List<Dictionary<string, string>>> RequeteAsync(string sparql)
    {
        // GET uniquement : le serveur répond 403 aux requêtes POST (on ne le contourne pas). Il ne connaît pas la clause VALUES (erreur 400) : les listes passent par FILTER(... IN (...)).
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
            catch (Exception ex) when (essai < 3 && ex is HttpRequestException or TaskCanceledException or JsonException) { await Task.Delay(3000 * essai); }
        }
    }

    private static DateOnly? Date(string? s) => DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    private static int Nb(IReadOnlyDictionary<string, string> l, string k) => int.TryParse(l.GetValueOrDefault(k), NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) ? x : 0;
    /// <summary>« http://dati.senato.it/senatore/17811 » → « 17811 » (idem pour un groupe).</summary>
    public static string? NumeroDe(string? uri) => Regex.Match(uri ?? "", @"/(\d+)$") is { Success: true } m ? m.Groups[1].Value : null;

    /// <summary>
    /// Votations d'une législature entre deux dates, par tranches de trois mois. Par défaut seulement les VOTES FINAUX (libellé « Votazione finale ») ; <paramref name="tout"/> importe aussi
    /// amendements, articles, motions… (la vérification du nombre légal, sans vote de fond, n'est jamais importée).
    /// </summary>
    public async Task<List<VotazioneSen>> ListerVotazioniAsync(int legislature, DateOnly depuis, DateOnly jusqua, bool tout)
    {
        var resultat = new Dictionary<string, VotazioneSen>();
        for (var debut = depuis; debut <= jusqua;)
        {
            var fin = debut.AddMonths(3) > jusqua.AddDays(1) ? jusqua.AddDays(1) : debut.AddMonths(3);
            var filtre = tout ? "FILTER(STR(?t) != 'verifica numero legale')" : $"FILTER(STR(?l) = '{LibelleVoteFinal}' && STR(?t) != 'verifica numero legale')";
            var q = $@"SELECT DISTINCT ?v ?d ?ns ?n ?l ?t ?fav ?con ?ast ?pre ?vot ?mag ?esi ?cm ?dd ?df ?dt WHERE {{
  ?v a osr:Votazione ; osr:legislatura {legislature} ; osr:seduta ?q ; osr:numero ?n ; rdfs:label ?l ; osr:tipoVotazione ?t .
  ?q osr:dataSeduta ?d ; osr:numeroSeduta ?ns .
  FILTER(STR(?d) >= '{debut:yyyy-MM-dd}' && STR(?d) < '{fin:yyyy-MM-dd}')
  {filtre}
  OPTIONAL {{ ?v osr:favorevoli ?fav }} OPTIONAL {{ ?v osr:contrari ?con }} OPTIONAL {{ ?v osr:astenuti ?ast }} OPTIONAL {{ ?v osr:presenti ?pre }}
  OPTIONAL {{ ?v osr:votanti ?vot }} OPTIONAL {{ ?v osr:maggioranza ?mag }} OPTIONAL {{ ?v osr:esito ?esi }} OPTIONAL {{ ?v osr:congedoMissione ?cm }}
  OPTIONAL {{ ?v osr:oggetto ?og . ?og osr:relativoA ?dd . OPTIONAL {{ ?dd osr:fase ?df }} OPTIONAL {{ ?dd osr:titolo ?dt }} }} }}";
            var lignes = await RequeteAsync(q);
            if (lignes.Count >= LimiteLignes) throw new InvalidOperationException($"Réponse tronquée ({lignes.Count} lignes) pour {debut:yyyy-MM-dd} → {fin:yyyy-MM-dd} : réduire la tranche de dates.");
            foreach (var l in lignes)
            {
                var v = Convertir(l, legislature);
                if (v is not null) resultat[v.Uri] = v;
            }
            debut = fin;
        }
        return resultat.Values.OrderBy(v => v.Date).ThenBy(v => v.Seduta).ThenBy(v => v.Numero).ToList();
    }

    /// <summary>Convertit une ligne de résultat ; null si la date ou l'identifiant sont illisibles.</summary>
    public static VotazioneSen? Convertir(IReadOnlyDictionary<string, string> l, int legislature)
    {
        var uri = l.GetValueOrDefault("v") ?? "";
        var m = Regex.Match(uri, @"votazione/(\d+)-(\d+)-(\d+)$");
        if (!m.Success || Date(l.GetValueOrDefault("d")) is not { } date) return null;
        return new VotazioneSen(uri, $"{m.Groups[1].Value}-{m.Groups[2].Value}-{m.Groups[3].Value}", legislature, Nb(l, "ns"), Nb(l, "n"), date,
            ItalyCameraSource.Nettoyer(l.GetValueOrDefault("l")), (l.GetValueOrDefault("t") ?? "").Trim(),
            Nb(l, "fav"), Nb(l, "con"), Nb(l, "ast"), Nb(l, "pre"), Nb(l, "vot"), Nb(l, "mag"), (l.GetValueOrDefault("esi") ?? "").Trim().ToLowerInvariant(), Nb(l, "cm"),
            l.GetValueOrDefault("dd") is { Length: > 0 } dd ? "https://" + dd.Replace("http://", "") : null, l.GetValueOrDefault("df"), ItalyCameraSource.Nettoyer(l.GetValueOrDefault("dt")) is { Length: > 0 } dt ? dt : null);
    }

    /// <summary>Positions des sénateurs pour quelques votations (UNION des trois listes). Lot petit : ~200 lignes par votation, 10 000 au plus par réponse.</summary>
    public async Task<List<VotoSen>> LireVotesAsync(IReadOnlyCollection<string> votazioniUri)
    {
        var valeurs = string.Join(", ", votazioniUri.Select(u => $"<{u}>"));
        var q = $@"SELECT DISTINCT ?v ?p ?s WHERE {{
  ?v ?p ?s . FILTER(?v IN ({valeurs})) FILTER(?p IN (osr:favorevole, osr:contrario, osr:astenuto)) }}";
        var lignes = await RequeteAsync(q);
        if (lignes.Count >= LimiteLignes) throw new InvalidOperationException($"Réponse pleine ({lignes.Count} lignes) pour {votazioniUri.Count} votations : réduire la taille du lot.");
        var votes = new List<VotoSen>();
        foreach (var l in lignes)
        {
            var liste = l["p"].Split('/')[^1];
            if (NumeroDe(l["s"]) is { } id) votes.Add(new VotoSen(l["v"], id, liste));
        }
        return votes;
    }

    /// <summary>Sénateurs ayant un mandat dans la législature (prénom, nom, début et fin éventuelle du mandat).</summary>
    public async Task<List<SenateurIt>> LireSenateursAsync(int legislature)
    {
        var lignes = await RequeteAsync($@"SELECT DISTINCT ?s ?p ?n ?i ?f WHERE {{
  ?s osr:mandato ?m ; foaf:firstName ?p ; foaf:lastName ?n . ?m osr:legislatura {legislature} ; osr:inizio ?i . OPTIONAL {{ ?m osr:fine ?f }} }}");
        var liste = new Dictionary<string, SenateurIt>();
        foreach (var l in lignes)
        {
            if (NumeroDe(l["s"]) is not { } id || Date(l.GetValueOrDefault("i")) is not { } debut) continue;
            var s = new SenateurIt(id, ItalyCameraSource.CasseNom(l["p"]), ItalyCameraSource.CasseNom(l["n"]), debut, Date(l.GetValueOrDefault("f")));
            // Un sénateur a plusieurs mandats dans la même législature s'il a quitté puis repris son siège : on garde le plus large.
            liste[id] = liste.TryGetValue(id, out var o) ? s with { Debut = o.Debut < s.Debut ? o.Debut : s.Debut, Fin = o.Fin is null || s.Fin is null ? null : (o.Fin > s.Fin ? o.Fin : s.Fin) } : s;
        }
        return liste.Values.ToList();
    }

    /// <summary>Appartenances aux groupes de la législature, avec dates.</summary>
    public async Task<List<AdesioneSen>> LireAdesioniAsync(int legislature)
    {
        var lignes = await RequeteAsync($@"SELECT DISTINCT ?s ?g ?i ?f WHERE {{
  ?s ocd:aderisce ?a . ?a osr:legislatura {legislature} ; osr:gruppo ?g ; osr:inizio ?i . OPTIONAL {{ ?a osr:fine ?f }} }}");
        var liste = new List<AdesioneSen>();
        foreach (var l in lignes)
            if (NumeroDe(l["s"]) is { } s && NumeroDe(l["g"]) is { } g && Date(l.GetValueOrDefault("i")) is { } i) liste.Add(new AdesioneSen(s, g, i, Date(l.GetValueOrDefault("f"))));
        return liste;
    }

    /// <summary>
    /// Noms successifs des groupes de la législature (sigle court + titre, avec date de début). Requête par la législature des appartenances plutôt que par une liste de groupes (VALUES) : le point
    /// d'accès répond 400 quand l'adresse devient trop longue.
    /// </summary>
    public async Task<List<DenominazioneSen>> LireDenominazioniAsync(int legislature)
    {
        var lignes = await RequeteAsync($@"SELECT DISTINCT ?g ?b ?t ?i WHERE {{
  ?a osr:legislatura {legislature} ; osr:gruppo ?g . ?g osr:denominazione ?d . ?d osr:titoloBreve ?b ; osr:titolo ?t ; osr:inizio ?i }}");
        var liste = new List<DenominazioneSen>();
        foreach (var l in lignes)
            if (NumeroDe(l["g"]) is { } g && Date(l.GetValueOrDefault("i")) is { } i) liste.Add(new DenominazioneSen(g, ItalyCameraSource.Nettoyer(l["b"]), ItalyCameraSource.Nettoyer(l["t"]), i));
        return liste;
    }

    // ------------------------------------------------------------ conversion

    public static string IdScrutin(int legislature, int numero) => $"ITSENATE-{legislature}-{numero}";
    public static string IdMembre(string id) => $"ITSENATE-{id}";
    public static string IdGroupe(int legislature, string numero) => $"ITSENATE-{legislature}-g{numero}";
    /// <summary>Numéro du site : séance × 10 000 + numéro du vote dans la séance (unique dans la législature ; le détail est dans la note).</summary>
    public static int Numero(VotazioneSen v) => v.Seduta * 10000 + v.Numero;

    private static readonly string[] Palette = ["#d4543a", "#3b82c4", "#e0b400", "#3f9e5b", "#7a5cc2", "#2a9d8f", "#c2557a", "#5a3e2b", "#8a8f98", "#b36b00"];

    /// <summary>Groupe d'une législature : sigle et titre = dernier nom connu avant la fin de la législature ; ordre gauche → droite initial d'après le sigle, couleur d'après l'ordre ou le numéro.</summary>
    public static GroupeRow Groupe(string numero, int legislature, IEnumerable<DenominazioneSen> noms, DateOnly finLegislature)
    {
        var courant = noms.Where(n => n.GroupeNumero == numero && n.Debut <= finLegislature).OrderByDescending(n => n.Debut).FirstOrDefault()
            ?? noms.Where(n => n.GroupeNumero == numero).OrderBy(n => n.Debut).FirstOrDefault();
        var sigle = courant?.Sigle is { Length: > 0 } s ? s : "Group " + numero;
        var bas = sigle.ToLowerInvariant();
        var ordre = bas.Contains("misto") ? 5 : bas.Contains("lega") || bas.StartsWith("lsp") ? 8 : bas.StartsWith("fdi") ? 9 : bas.StartsWith("fi") || bas.Contains("forza") ? 7
            : bas.Contains("avs") || bas.Contains("sinistra") ? 1 : bas.StartsWith("pd") ? 2 : bas.Contains("m5s") || bas.Contains("5s") ? 3 : bas.StartsWith("iv") || bas.StartsWith("az") ? 4 : bas.StartsWith("aut") || bas.StartsWith("cd") ? 6 : 50;
        var couleur = Palette[(ordre < 50 ? ordre - 1 : int.Parse(numero) % Palette.Length) % Palette.Length];
        return new GroupeRow(IdGroupe(legislature, numero), Chambres.ItSenate, legislature, sigle, courant?.Titre ?? sigle, couleur, ordre);
    }

    /// <summary>Groupe du sénateur à une date : l'appartenance dont l'intervalle couvre la date (null si aucune : sans groupe connu).</summary>
    public static string? GroupeA(IEnumerable<AdesioneSen> adesioni, string senateurId, DateOnly date) =>
        adesioni.Where(a => a.SenateurId == senateurId && a.Debut <= date && (a.Fin is null || a.Fin >= date)).OrderByDescending(a => a.Debut).FirstOrDefault()?.GroupeNumero;

    /// <summary>Position d'après la liste : favorevole / contrario / astenuto ; autre = pas un vote exprimé.</summary>
    public static string? Position(string liste) => liste switch
    {
        "favorevole" => Positions.Pour,
        "contrario" => Positions.Contre,
        "astenuto" => Positions.Abstention,
        _ => null,
    };

    /// <summary>
    /// Convertit une votation en scrutin. Totaux et résultat = OFFICIELS (« approvato » / « respinto » ; autre valeur : pas de résultat). Les positions viennent des listes ; l'écart entre le total
    /// officiel et les listes est dit dans la note. Scrutin secret : totaux seuls, marqué « décompte incomplet ». <paramref name="groupeA"/> donne le groupe d'un sénateur à la date du vote.
    /// </summary>
    public static ScrutinAvecVotes VersScrutin(VotazioneSen v, IReadOnlyList<VotoSen> votes, IReadOnlySet<string> senateursConnus, HashSet<string> inconnus, Func<string, DateOnly, string?> groupeA)
    {
        var id = IdScrutin(v.Legislature, Numero(v));
        var secret = v.Tipo.Equals("segreta", StringComparison.OrdinalIgnoreCase);
        var lignes = new List<VoteRow>();
        var sansFiche = 0;
        if (!secret)
        {
            foreach (var x in votes)
            {
                if (Position(x.Liste) is not { } pos) continue;
                if (!senateursConnus.Contains(x.SenateurId)) { inconnus.Add(x.SenateurId); sansFiche++; continue; }
                var g = groupeA(x.SenateurId, v.Date);
                lignes.Add(new VoteRow(id, IdMembre(x.SenateurId), g is null ? null : IdGroupe(v.Legislature, g), pos, false, false, pos));
            }
        }
        var pour = lignes.Count(l => l.Position == Positions.Pour);
        var contre = lignes.Count(l => l.Position == Positions.Contre);
        var abst = lignes.Count(l => l.Position == Positions.Abstention);
        var attendus = v.Favorevoli + v.Contrari + v.Astenuti;
        var notes = new List<string> { $"Sitting {v.Seduta}, vote {v.Numero}." };
        if (secret) notes.Add("Secret ballot: the Senate publishes only the official totals, not how each senator voted.");
        else if (sansFiche > 0) notes.Add($"{sansFiche} senator(s) who voted have no entry in the members directory used by this site and are not shown as dots.");
        else if (pour != v.Favorevoli || contre != v.Contrari || abst != v.Astenuti) notes.Add($"Official count: {v.Favorevoli} for, {v.Contrari} against, {v.Astenuti} abstentions; member list: {pour} for, {contre} against, {abst} abstentions.");
        var manquants = secret ? attendus : Math.Max(0, attendus - lignes.Count);
        var sort = v.Esito switch { "approvato" => "adopté", "respinto" => "rejeté", _ => null };
        // « Votazione finale » seul ne dit rien : on y ajoute le projet de loi (numéro de fascicule + titre) quand la source le relie.
        var fase = v.DdlFase is { Length: > 0 } f ? f + " — " : "";
        var titre = v.DdlTitre is not null ? $"{v.Titre}: {fase}{v.DdlTitre}" : v.Titre.Length > 0 ? v.Titre : $"Vote {v.Id}";
        var scrutin = new ScrutinRow(
            id, Chambres.ItSenate, v.Legislature, Numero(v), v.Date, titre, "roll call", sort, v.DdlUri, v.Titre.Equals(LibelleVoteFinal, StringComparison.OrdinalIgnoreCase),
            v.Votanti, v.Favorevoli, v.Contrari, v.Astenuti, 0, null, manquants, string.Join(" ", notes),
            v.Tipo.Length > 0 ? v.Tipo : null, "Threshold published by the Senate (its basis is not documented in the data)", v.Votanti, v.Maggioranza, titre);   // le champ « maggioranza » n'est pas toujours la moitié des votants (39 pour 129 votants constaté) : présenté tel que publié, sans interprétation
        return new ScrutinAvecVotes(scrutin, lignes);
    }

    public void Dispose() => _http.Dispose();
}
