using System.Globalization;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace IlsOntVote.Importer;

/// <summary>Une division (vote nominal) de la Chambre des communes du Canada, telle que publiée dans la liste des votes d'une session.</summary>
public sealed record DivisionCa(
    int Parlement, int Session, int Numero, DateTime DateHeure, string SujetEn, string? SujetFr, string? Resultat,
    int Oui, int Non, int Jumeles, string? TypeDocument, string? Projet);

/// <summary>Bulletin d'un député dans une division : <c>Valeur</c> = Yea | Nay | Paired (libellé anglais de la source).</summary>
public sealed record BulletinCa(int PersonId, string Valeur);

/// <summary>Appartenance d'un député à un caucus (parti), avec ses dates.</summary>
public sealed record PartiCa(string Nom, DateOnly Debut, DateOnly? Fin);

/// <summary>Député d'une législature : mandat daté (premier début → dernière fin, ouvert si l'une des périodes l'est) et caucus datés.</summary>
public sealed record MembreCa(int PersonId, string Prenom, string Nom, string? Circonscription, string? Province, DateOnly Debut, DateOnly? Fin, IReadOnlyList<PartiCa> Partis);

/// <summary>
/// Chambre des communes du Canada, SOURCE OFFICIELLE : données ouvertes de la Chambre (<c>ourcommons.ca/members/en/…/xml</c>, XML sans clé).
/// - liste des votes d'une session : <c>votes/xml?parlSession=44-1</c> (décomptes officiels, résultat, type de document, projet de loi) ;
/// - détail d'une division : <c>votes/{législature}/{session}/{n}/xml</c> (un bulletin par député ayant voté ou JUMELÉ) ;
/// - annuaire daté : <c>search/xml?parliament=44&amp;caucusId=all&amp;province=all&amp;gender=all</c> (mandats et caucus datés).
/// Seuls les députés qui ont voté ou se sont jumelés sont listés : les absents se DÉDUISENT des mandats datés. Un député « jumelé » (entente avec un adversaire pour ne pas voter)
/// n'a pas voté : il n'est pas stocké, le nombre est dit dans la note de la division (absent = absent, aucune raison affichée comme motif).
/// La liste française (<c>/members/fr/…</c>, redirigée vers noscommunes.ca) fournit le sujet en français. Permission de la Présidence : reproduction exacte, sans prétendre à l'officiel,
/// hors but lucratif (dit dans la méthodologie et les mentions).
/// </summary>
public sealed class CanadaSource : IDisposable
{
    private const string Base = "https://www.ourcommons.ca/members/";
    private readonly HttpClient _http;

    public CanadaSource()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        // Agent identifié (comme pour le Parlement suisse : un pare-feu refuse les agents génériques).
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("IlsOntVote-importer (+https://www.ilsontvote.org)");
    }

    /// <summary>Législature courante (45e, depuis l'élection du 28 avril 2025). Les précédentes : 44e (2021-2025), 43e (2019-2021).</summary>
    public const int Courante = 45;

    /// <summary>Plafond de sessions essayées par législature (la source ne dit pas combien il y en a : on s'arrête à la première session sans vote).</summary>
    private const int SessionsMax = 6;

    public static string IdMembre(int personId) => $"{Chambres.CaCommons}-{personId}";
    public static string IdScrutin(int legislature, int numero) => $"{Chambres.CaCommons}-{legislature}-{numero}";
    public static string IdGroupe(int legislature, string caucus) => $"{Chambres.CaCommons}-{legislature}-{Regex.Replace(caucus.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-')}";

    /// <summary>
    /// Numéro du site : session × 10 000 + numéro de la division. Le numéro de la source REPART DE 1 à chaque session (la 44e législature en a eu une seule, la 43e deux) : sans la session
    /// deux divisions d'une même législature porteraient le même numéro. Le détail est dans la note de chaque vote.
    /// </summary>
    public static int Numero(DivisionCa d) => d.Session * 10_000 + d.Numero;

    private async Task<XDocument?> GetAsync(string chemin)
    {
        for (var essai = 1; ; essai++)
        {
            try
            {
                using var r = await _http.GetAsync(Base + chemin);
                if (r.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
                r.EnsureSuccessStatusCode();
                // Une réponse qui n'est pas du XML (page d'erreur servie en 200) est traitée comme absente.
                var type = r.Content.Headers.ContentType?.MediaType ?? "";
                if (!type.Contains("xml", StringComparison.OrdinalIgnoreCase)) return null;
                return XDocument.Load(await r.Content.ReadAsStreamAsync());
            }
            catch (Exception) when (essai < 3) { await Task.Delay(2000 * essai); }
        }
    }

    private static string? Texte(XElement e, string nom) { var v = e.Element(nom)?.Value; return string.IsNullOrWhiteSpace(v) ? null : v.Trim(); }
    private static int Entier(XElement e, string nom) => int.TryParse(Texte(e, nom), out var n) ? n : 0;
    private static DateOnly? Jour(XElement e, string nom) =>
        Texte(e, nom) is { Length: >= 10 } s && DateOnly.TryParse(s[..10], CultureInfo.InvariantCulture, out var d) ? d : null;

    /// <summary>Divisions d'une session (liste anglaise + sujet français). Liste vide = la session n'existe pas (encore).</summary>
    public async Task<IReadOnlyList<DivisionCa>> ListerDivisionsAsync(int legislature, int session)
    {
        var en = await GetAsync($"en/votes/xml?parlSession={legislature}-{session}");
        if (en is null) return [];
        var fr = await GetAsync($"fr/votes/xml?parlSession={legislature}-{session}");
        var sujetsFr = fr?.Descendants("Vote").Where(v => Entier(v, "DecisionDivisionNumber") > 0)
            .GroupBy(v => Entier(v, "DecisionDivisionNumber")).ToDictionary(g => g.Key, g => Texte(g.First(), "DecisionDivisionSubject"))
            ?? [];
        var liste = new List<DivisionCa>();
        foreach (var v in en.Descendants("Vote"))
        {
            if (Convertir(v, sujetsFr) is { } d && d.Parlement == legislature && d.Session == session) liste.Add(d);
        }
        return liste.OrderBy(d => d.Numero).ToList();
    }

    /// <summary>Convertit une ligne de la liste ; null si la date ou le numéro manquent (jamais d'approximation).</summary>
    public static DivisionCa? Convertir(XElement v, IReadOnlyDictionary<int, string?> sujetsFr)
    {
        var n = Entier(v, "DecisionDivisionNumber");
        if (n <= 0 || !DateTime.TryParse(Texte(v, "DecisionEventDateTime"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return null;
        var sujet = Texte(v, "DecisionDivisionSubject") ?? $"Division {n}";
        return new DivisionCa(Entier(v, "ParliamentNumber"), Entier(v, "SessionNumber"), n, date, sujet, sujetsFr.GetValueOrDefault(n),
            Texte(v, "DecisionResultName"), Entier(v, "DecisionDivisionNumberOfYeas"), Entier(v, "DecisionDivisionNumberOfNays"), Entier(v, "DecisionDivisionNumberOfPaired"),
            Texte(v, "DecisionDivisionDocumentTypeName"), Texte(v, "BillNumberCode"));
    }

    /// <summary>Bulletins d'une division. Null = détail introuvable (la division est alors ignorée : un décompte sans noms ne doit pas faire croire à des absents).</summary>
    public async Task<IReadOnlyList<BulletinCa>?> LireBulletinsAsync(DivisionCa d)
    {
        var doc = await GetAsync($"en/votes/{d.Parlement}/{d.Session}/{d.Numero}/xml");
        if (doc is null) return null;
        return doc.Descendants("VoteParticipant")
            .Select(p => new BulletinCa(Entier(p, "PersonId"), Texte(p, "VoteValueName") ?? ""))
            .Where(b => b.PersonId > 0).ToList();
    }

    /// <summary>
    /// Députés d'une législature avec mandat et caucus datés (annuaire officiel). Une ligne par période de caucus : le mandat va du premier début à la dernière fin
    /// (ouvert si une période l'est), les caucus gardent leurs dates.
    /// </summary>
    public async Task<IReadOnlyList<MembreCa>> LireMembresAsync(int legislature)
    {
        var doc = await GetAsync($"en/search/xml?parliament={legislature}&caucusId=all&province=all&gender=all")
            ?? throw new InvalidOperationException($"Annuaire de la {legislature}e législature introuvable.");
        return ConvertirMembres(doc.Descendants("MemberOfParliament"));
    }

    /// <summary>Regroupe les lignes de l'annuaire par député.</summary>
    public static IReadOnlyList<MembreCa> ConvertirMembres(IEnumerable<XElement> lignes)
    {
        var liste = new List<MembreCa>();
        foreach (var g in lignes.GroupBy(l => Entier(l, "PersonId")).Where(g => g.Key > 0))
        {
            var rows = g.Select(l => (L: l, Debut: Jour(l, "FromDateTime"), Fin: Jour(l, "ToDateTime"))).Where(r => r.Debut is not null).OrderBy(r => r.Debut).ToList();
            if (rows.Count == 0) continue;
            var premier = rows[0].L;
            DateOnly? fin = rows.Any(r => r.Fin is null) ? null : rows.Max(r => r.Fin);
            var partis = rows.Select(r => new PartiCa(Texte(r.L, "CaucusShortName") ?? "Independent", r.Debut!.Value, r.Fin)).ToList();
            liste.Add(new MembreCa(g.Key, Texte(premier, "PersonOfficialFirstName") ?? "", Texte(premier, "PersonOfficialLastName") ?? "",
                Texte(rows[^1].L, "ConstituencyName"), Texte(rows[^1].L, "ConstituencyProvinceTerritoryName"), rows[0].Debut!.Value, fin, partis));
        }
        return liste;
    }

    private static readonly Dictionary<string, (string Sigle, int Ordre, string Couleur)> Caucus = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NDP"] = ("NDP", 1, "#f58220"), ["Green Party"] = ("Grn", 2, "#3d9b35"), ["Liberal"] = ("Lib", 3, "#d71920"),
        ["Bloc Québécois"] = ("BQ", 4, "#33b2cc"), ["Independent"] = ("Ind", 6, "#8a8f98"), ["Conservative"] = ("Con", 7, "#1a4782"),
    };

    /// <summary>
    /// Groupe d'une législature = un caucus. Sigle usuel (Lib, Con, BQ, NDP, Grn) quand on le connaît, sinon le nom ; ordre gauche → droite et couleur de départ (modifiables en base,
    /// l'import ne les écrase pas). Un caucus inconnu reçoit l'ordre 5 (centre) et un gris : jamais de couleur inventée pour un parti qu'on ne connaît pas.
    /// </summary>
    public static GroupeRow Groupe(int legislature, string caucus)
    {
        var (sigle, ordre, couleur) = Caucus.TryGetValue(caucus, out var c) ? c : (caucus, 5, "#8a8f98");
        return new GroupeRow(IdGroupe(legislature, caucus), Chambres.CaCommons, legislature, sigle, caucus, couleur, ordre);
    }

    /// <summary>Caucus d'un député à une date (celui qui couvre le jour, sinon le plus récent commencé avant ; null = aucun connu).</summary>
    public static PartiCa? PartiA(MembreCa m, DateOnly date) =>
        m.Partis.Where(p => p.Debut <= date && (p.Fin is null || p.Fin >= date)).OrderByDescending(p => p.Debut).FirstOrDefault();

    /// <summary>Position d'après la valeur de la source : Yea = pour, Nay = contre ; « Paired » (jumelé) et toute valeur inconnue : null (pas un vote).</summary>
    public static string? Position(string valeur) => valeur switch
    {
        "Yea" => Positions.Pour,
        "Nay" => Positions.Contre,
        _ => null,
    };

    /// <summary>« 3rd reading and adoption of Bill C-… » : vote final d'un projet de loi (<b>calculé</b> d'après le sujet, la source ne le signale pas).</summary>
    public static bool EstVoteFinal(string sujet) => Regex.IsMatch(sujet, @"^\s*(3rd|third) reading", RegexOptions.IgnoreCase);

    /// <summary>Adresse officielle de la division (page publique de la Chambre).</summary>
    public static string AdresseOfficielle(DivisionCa d) => $"https://www.ourcommons.ca/members/en/votes/{d.Parlement}/{d.Session}/{d.Numero}";

    /// <summary>
    /// Convertit une division. Décomptes = OFFICIELS (oui / non) ; positions = bulletins nominatifs ; « Agreed To » / « Negatived » → adopté / rejeté (autre valeur : pas de résultat, jamais deviné).
    /// Les jumelés ne sont pas des votes (dits dans la note) ; un député absent de l'annuaire est compté dans la note et ignoré ; un écart entre décompte et noms rend la division « incomplète ».
    /// </summary>
    public static ScrutinAvecVotes VersScrutin(DivisionCa d, IReadOnlyList<BulletinCa> bulletins, IReadOnlyDictionary<int, MembreCa> membres, HashSet<int> inconnus)
    {
        var date = DateOnly.FromDateTime(d.DateHeure);
        var id = IdScrutin(d.Parlement, Numero(d));
        var lignes = new List<VoteRow>();
        var sansFiche = 0;
        foreach (var b in bulletins)
        {
            if (Position(b.Valeur) is not { } pos) continue;
            if (!membres.TryGetValue(b.PersonId, out var m)) { inconnus.Add(b.PersonId); sansFiche++; continue; }
            var parti = PartiA(m, date);
            lignes.Add(new VoteRow(id, IdMembre(b.PersonId), parti is null ? null : IdGroupe(d.Parlement, parti.Nom), pos, false, false, pos));
        }
        var pour = lignes.Count(l => l.Position == Positions.Pour);
        var contre = lignes.Count(l => l.Position == Positions.Contre);
        var notes = new List<string> { $"Sitting of {date:yyyy-MM-dd}, division {d.Numero} of session {d.Parlement}-{d.Session}." };
        if (d.Jumeles > 0) notes.Add($"{d.Jumeles} member(s) were paired (an agreement with an opponent not to vote): they are not counted as votes.");
        if (sansFiche > 0) notes.Add($"{sansFiche} member(s) who voted have no entry in the members directory used by this site and are not shown as dots.");
        else if (pour != d.Oui || contre != d.Non) notes.Add($"Official count: {d.Oui} Yea, {d.Non} Nay; member list: {pour} Yea, {contre} Nay.");
        var manquants = Math.Max(0, d.Oui + d.Non - lignes.Count);
        var sort = d.Resultat switch { "Agreed To" => "adopté", "Negatived" => "rejeté", _ => null };
        var titreEn = d.SujetEn;
        var titre = d.SujetFr ?? d.SujetEn;
        var scrutin = new ScrutinRow(
            id, Chambres.CaCommons, d.Parlement, Numero(d), date, titre, "recorded division", sort, AdresseOfficielle(d), EstVoteFinal(d.SujetEn),
            d.Oui + d.Non, d.Oui, d.Non, 0, 0, null, manquants, string.Join(" ", notes),
            d.TypeDocument, "Simple majority of the votes cast (Yea against Nay)", d.Oui + d.Non, null, titreEn);
        return new ScrutinAvecVotes(scrutin, lignes);
    }

    public void Dispose() => _http.Dispose();
}
