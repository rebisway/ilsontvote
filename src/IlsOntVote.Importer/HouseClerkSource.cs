using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace IlsOntVote.Importer;

/// <summary>Un vote nominal de la Chambre des représentants tel que publié par le greffe : métadonnées, totaux officiels et vote de chaque représentant (clé = identifiant « bioguide »).</summary>
public sealed record AppelChambre(
    int Congres, int Session, int Numero, DateOnly Date, string Projet, string Question, string Type, string Resultat, string Description,
    int Pour, int Contre, int Present, int NonVotants, IReadOnlyList<(string Bioguide, string Vote)> Votes);

/// <summary>
/// Chambre des représentants des États-Unis, SOURCE OFFICIELLE : le greffe (<c>clerk.house.gov</c>) publie un fichier XML par vote nominal,
/// <c>https://clerk.house.gov/evs/{année}/roll{NNN}.xml</c>, avec les totaux officiels et le vote de chaque représentant. Remplace Voteview pour la Chambre (le Sénat, lui,
/// refuse les clients automatiques : il reste sur Voteview). Pièges : (1) un vote inexistant renvoie HTTP 200 avec le texte « Error sanitizing file » (pas un 404) : on reconnaît
/// un vote à son élément racine ; (2) la numérotation repart de 1 chaque session : le site numérote en continu sur le Congrès (décalage de session ajouté à l'import) ;
/// (3) les appels de quorum (« Call of the House ») et l'élection du Président de la Chambre ne sont pas des votes pour/contre : ils ne sont pas importés.
/// </summary>
public sealed class HouseClerkSource : IDisposable
{
    private readonly HttpClient _http = Telechargement.CreerClient(TimeSpan.FromMinutes(2));

    /// <summary>Première année d'un Congrès : le 119e s'ouvre en 2025 (1789 + 2 × 118 = 2025).</summary>
    public static int PremiereAnnee(int congres) => 1789 + 2 * (congres - 1);

    public static string Url(int annee, int numero) => $"https://clerk.house.gov/evs/{annee}/roll{numero:000}.xml";

    /// <summary>Lit un vote ; null s'il n'existe pas (fin de la liste de l'année).</summary>
    public async Task<AppelChambre?> LireAsync(int annee, int numero)
    {
        var xml = await _http.GetStringAsync(Url(annee, numero));
        return Analyser(xml);
    }

    /// <summary>Analyse un XML du greffe ; null si le contenu n'est pas un vote (message d'erreur du serveur, fichier vide).</summary>
    public static AppelChambre? Analyser(string xml)
    {
        if (!xml.Contains("<rollcall-vote")) return null;
        var doc = XDocument.Parse(xml);
        var m = doc.Root?.Element("vote-metadata");
        if (m is null) return null;
        string T(string nom) => m.Element(nom)?.Value.Trim() ?? "";
        int N(XElement? e, string nom) => int.TryParse(e?.Element(nom)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) ? x : 0;
        var totaux = m.Element("vote-totals")?.Element("totals-by-vote");
        if (!DateOnly.TryParseExact(T("action-date"), "d-MMM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return null;
        var votes = (doc.Root!.Element("vote-data")?.Elements("recorded-vote") ?? [])
            .Select(v => (Bioguide: v.Element("legislator")?.Attribute("name-id")?.Value ?? "", Vote: v.Element("vote")?.Value.Trim() ?? ""))
            .Where(v => v.Bioguide.Length > 0).ToList();
        return new AppelChambre(
            int.TryParse(T("congress"), out var c) ? c : 0, T("session").StartsWith('2') ? 2 : 1, int.TryParse(T("rollcall-num"), out var n) ? n : 0, date,
            NormaliserProjet(T("legis-num")), T("vote-question"), T("vote-type"), T("vote-result"), T("vote-desc"),
            N(totaux, "yea-total"), N(totaux, "nay-total"), N(totaux, "present-total"), N(totaux, "not-voting-total"), votes);
    }

    /// <summary>« H R 29 » → « H.R. 29 », « H RES 5 » → « H.Res. 5 », « H J RES 1 » → « H.J.Res. 1 », « S 1 » → « S. 1 » (désignations usuelles des textes).</summary>
    public static string NormaliserProjet(string brut)
    {
        var t = Regex.Replace(brut.Trim(), @"\s+", " ");
        var m = Regex.Match(t, @"^(H J RES|H CON RES|H RES|H R|S J RES|S CON RES|S RES|S) ?(.*)$", RegexOptions.IgnoreCase);
        if (!m.Success) return t;
        var forme = m.Groups[1].Value.ToUpperInvariant() switch
        {
            "H R" => "H.R.", "H RES" => "H.Res.", "H J RES" => "H.J.Res.", "H CON RES" => "H.Con.Res.",
            "S" => "S.", "S RES" => "S.Res.", "S J RES" => "S.J.Res.", "S CON RES" => "S.Con.Res.",
            var autre => autre,
        };
        return (forme + " " + m.Groups[2].Value).Trim();
    }

    /// <summary>
    /// Vote écrit en base ? Non pour un appel de quorum et pour l'élection du Président de la Chambre (le vote de chacun y est un NOM de candidat, ni « Yea » ni « Nay » : les
    /// afficher comme absents serait faux). Les deux restent absents du site et la méthodologie le dit.
    /// </summary>
    public static bool EstUnVotePourContre(AppelChambre a) =>
        !a.Type.Contains("QUORUM", StringComparison.OrdinalIgnoreCase) && !a.Question.Contains("Election of the Speaker", StringComparison.OrdinalIgnoreCase);

    /// <summary>Position d'un vote du greffe : Yea/Aye → pour ; Nay/No → contre ; Present → abstention ; « Not Voting » (et tout le reste) → null (absent, non stocké).</summary>
    public static string? Position(string vote) => vote.ToLowerInvariant() switch
    {
        "yea" or "aye" => Positions.Pour,
        "nay" or "no" => Positions.Contre,
        "present" => Positions.Abstention,
        _ => null,
    };

    /// <summary>Règle de majorité d'après le type de vote du greffe (« 2/3 YEA-AND-NAY », « 3/5 YEA-AND-NAY »), « 1/2 » sinon.</summary>
    public static string Majorite(string type) => type.Contains("2/3") ? "2/3" : type.Contains("3/5") ? "3/5" : "1/2";

    /// <summary>Adresse de l'enregistrement officiel (page du greffe) : année + numéro sur trois chiffres.</summary>
    public static string UrlOfficielle(AppelChambre a) => $"https://clerk.house.gov/Votes/{a.Date.Year}{a.Numero:000}";

    /// <summary>
    /// Convertit un vote du greffe en scrutin + votes. <paramref name="numero"/> = numéro CONTINU dans le Congrès (décalage de session ajouté) ; <paramref name="icpsrDe"/> relie l'identifiant
    /// « bioguide » du greffe à l'élu du site (clé ICPSR de Voteview, qui fournit les noms) ; un représentant inconnu de cet annuaire est compté dans <paramref name="inconnus"/>
    /// et n'a pas de point (la note du vote le dit). Totaux = officiels du greffe, qui coïncident avec la liste (écart signalé quand même).
    /// </summary>
    public static ScrutinAvecVotes VersScrutin(AppelChambre a, int numero, IReadOnlyDictionary<string, string> icpsrDe, IReadOnlyDictionary<string, string> groupeDe, HashSet<string> inconnus)
    {
        var id = UsSource.IdScrutin(Chambres.UsHouse, a.Congres, numero);
        var votes = new List<VoteRow>();
        var sansFiche = 0;
        foreach (var (bioguide, vote) in a.Votes)
        {
            if (Position(vote) is not { } pos) continue;
            if (!icpsrDe.TryGetValue(bioguide, out var icpsr)) { inconnus.Add(bioguide); sansFiche++; continue; }
            votes.Add(new VoteRow(id, UsSource.IdMembre(Chambres.UsHouse, icpsr), groupeDe.GetValueOrDefault(icpsr), pos, false, false, pos));
        }
        var y = votes.Count(v => v.Position == Positions.Pour);
        var n = votes.Count(v => v.Position == Positions.Contre);
        var notes = new List<string>();
        if (sansFiche > 0) notes.Add($"{sansFiche} member(s) who voted have no entry in the members directory used by this site and are not shown as dots.");
        else if (y != a.Pour || n != a.Contre) notes.Add($"Official count: {a.Pour} yea, {a.Contre} nay; member list of the Clerk: {y} yea, {n} nay.");
        var exprimes = a.Pour + a.Contre;
        var (libMaj, requis) = UsSource.Majorite(Majorite(a.Type), exprimes);
        var titre = a.Projet.Length > 0 && a.Description.Length > 0 ? $"{a.Projet}: {a.Description}" : a.Projet.Length > 0 ? $"{a.Projet} — {a.Question}" : a.Description.Length > 0 ? a.Description : a.Question;
        var scrutin = new ScrutinRow(
            id, Chambres.UsHouse, a.Congres, numero, a.Date, titre, "roll call", UsSource.Sort(a.Resultat), UrlOfficielle(a), UsSource.EstVoteSurEnsemble(a.Question),
            exprimes + a.Present, a.Pour, a.Contre, a.Present, a.NonVotants, null, 0, notes.Count == 0 ? null : string.Join(" ", notes),
            a.Question, libMaj, exprimes, requis, titre);
        return new ScrutinAvecVotes(scrutin, votes);
    }

    public void Dispose() => _http.Dispose();
}
