using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace IlsOntVote.Importer;

/// <summary>Un vote nominal du Sénat du Canada, tel que publié dans la liste des votes d'une session (décomptes officiels, résultat, projet de loi).</summary>
public sealed record VoteSenatCa(
    int Parlement, int Session, int Id, int Ordre, DateOnly Date, string TitreEn, string? TitreFr,
    int Oui, int Non, int Abstentions, int Total, string? Resultat, string? Projet);

/// <summary>Ligne du tableau d'un vote : un sénateur en fonction ce jour-là, avec le sigle de son groupe et son vote (<c>Valeur</c> = Yea | Nay | Abstention ; null = aucune case cochée = n'a pas voté).</summary>
public sealed record BulletinSenatCa(int SenateurId, string Prenom, string Nom, string Groupe, string? Province, string? Valeur);

/// <summary>
/// Sénat du Canada. Il n'existe PAS de fichier de données ouvertes pour les votes : la source est le site officiel du Sénat (<c>sencanada.ca</c>), dont les pages sont lues telles
/// quelles (HTML rendu côté serveur, sans clé, <c>robots.txt</c> vide). Deux pages par session :
/// - la LISTE des votes (<c>/en/in-the-chamber/votes/{législature}-{session}</c>, et la version française pour les titres) : date, titre, décomptes officiels, projet de loi, résultat ;
/// - le DÉTAIL d'un vote (<c>…/votes/details/{id}/{législature}-{session}</c>) : un tableau de TOUS les sénateurs en fonction ce jour-là, avec une case cochée pour Yea, Nay ou Abstention.
/// Un sénateur sans case cochée n'a pas voté : il est absent (aucune raison affichée). Le tableau liste les sénateurs en fonction, donc les mandats se DÉDUISENT des listes
/// (premier et dernier vote où le sénateur figure), comme pour l'Union européenne : la source ne publie pas d'annuaire daté exploitable.
/// Permission du Sénat (<c>sencanada.ca/en/intellectual-property</c>, lue le 2026-11-22) : reproduction exacte, sans prétendre à l'officiel, hors but lucratif — dit dans la méthodologie et
/// les mentions. Le HTML peut changer sans préavis : tout ce qui n'est pas reconnu est ignoré et signalé, jamais deviné.
/// </summary>
public sealed class CanadaSenatSource : IDisposable
{
    private const string Base = "https://sencanada.ca";
    private readonly HttpClient _http;

    public CanadaSenatSource()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        // Agent identifié et honnête (comme pour les autres sources : un agent générique est refusé par certains pare-feu).
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("IlsOntVote-importer (+https://www.ilsontvote.org)");
    }

    public static string IdMembre(int senateurId) => $"{Chambres.CaSenate}-{senateurId}";
    public static string IdScrutin(int legislature, int numero) => $"{Chambres.CaSenate}-{legislature}-{numero}";
    public static string IdGroupe(int legislature, string sigle) => $"{Chambres.CaSenate}-{legislature}-{Regex.Replace(sigle.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-')}";

    /// <summary>
    /// Numéro du site : session × 10 000 + rang du vote dans la session. Le rang repart de 1 à chaque session ; la source saute parfois un numéro (le n° 6 de la 45-1, le n° 118 de la 44-1 :
    /// vote jamais publié dans la liste), ce que la méthodologie dit. L'identifiant interne de la source (<c>Id</c>) reste dans la note et dans l'adresse officielle.
    /// </summary>
    public static int Numero(VoteSenatCa v) => v.Session * 10_000 + v.Ordre;

    /// <summary>Adresse officielle du vote (page publique du Sénat, version anglaise).</summary>
    public static string AdresseOfficielle(VoteSenatCa v) => $"{Base}/en/in-the-chamber/votes/details/{v.Id}/{v.Parlement}-{v.Session}";

    private async Task<string?> GetAsync(string chemin)
    {
        for (var essai = 1; ; essai++)
        {
            try
            {
                using var r = await _http.GetAsync(Base + chemin);
                if (r.StatusCode == HttpStatusCode.NotFound) return null;
                r.EnsureSuccessStatusCode();
                return await r.Content.ReadAsStringAsync();
            }
            catch (Exception) when (essai < 3) { await Task.Delay(2000 * essai); }
        }
    }

    /// <summary>
    /// Votes d'une session (liste anglaise + titres français). Liste vide = la session n'existe pas (encore) : le Sénat renvoie alors une page sans ligne ou un 404.
    /// </summary>
    public async Task<IReadOnlyList<VoteSenatCa>> ListerVotesAsync(int legislature, int session)
    {
        var en = await GetAsync($"/en/in-the-chamber/votes/{legislature}-{session}");
        if (en is null) return [];
        var fr = await GetAsync($"/fr/dans-la-chambre/votes/{legislature}-{session}");
        var titresFr = fr is null ? [] : ParserListe(fr, legislature, session).ToDictionary(v => v.Id, v => v.TitreEn);
        return ParserListe(en, legislature, session)
            .Select(v => v with { TitreFr = titresFr.GetValueOrDefault(v.Id) })
            .OrderBy(v => v.Ordre).ToList();
    }

    /// <summary>Tableau d'un vote : tous les sénateurs en fonction ce jour-là. Null = page introuvable (le vote est alors ignoré : un décompte sans noms ferait croire à des absents).</summary>
    public async Task<IReadOnlyList<BulletinSenatCa>?> LireBulletinsAsync(VoteSenatCa v)
    {
        var html = await GetAsync($"/en/in-the-chamber/votes/details/{v.Id}/{v.Parlement}-{v.Session}");
        return html is null ? null : ParserDetail(html);
    }

    private static readonly Regex Commentaires = new(@"<!--.*?-->", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Ligne = new(@"<tr\b[^>]*>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Cellule = new(@"<td\b([^>]*)>(.*?)</td>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Balises = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex OrdreDate = new(@"data-order=""(\d{4}-\d{2}-\d{2}) \d{2}:\d{2}:\d{2} (\d+)""", RegexOptions.Compiled);
    private static readonly Regex LienVote = new(@"href=""[^""]*/votes/details/(\d+)/(\d+)-(\d+)""[^>]*>(.*?)</a>", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Decomptes = new(@"(?:Yeas|Oui)[^:]*:\s*(\d+).*?(?:Nays|Non)[^:]*:\s*(\d+).*?(?:Abstentions?|Abstention)[^:]*:\s*(\d+).*?Total[^:]*:\s*(\d+)", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex LienSenateur = new(@"/votes/senator/(\d+)/", RegexOptions.Compiled);

    /// <summary>Texte brut d'un fragment HTML : balises retirées, entités décodées, espaces réduits.</summary>
    private static string Brut(string html) => Regex.Replace(WebUtility.HtmlDecode(Balises.Replace(html, " ")), @"\s+", " ").Trim();

    /// <summary>
    /// Lit la page de liste d'une session. Les lignes non reconnues (pas de lien de détail, date ou rang absents, décomptes illisibles) sont ignorées : jamais d'approximation.
    /// Le titre de la liste française se lit avec la même méthode (seul le titre y sert) ; les décomptes anglais ne s'y retrouvent pas : ils valent alors 0 et la ligne n'est utilisée que pour son titre.
    /// </summary>
    public static IReadOnlyList<VoteSenatCa> ParserListe(string html, int legislature, int session)
    {
        var liste = new List<VoteSenatCa>();
        var corps = Commentaires.Replace(html, "");
        var debut = corps.IndexOf("<tbody", StringComparison.OrdinalIgnoreCase);
        if (debut < 0) return liste;
        foreach (Match l in Ligne.Matches(corps[debut..]))
        {
            var cellules = Cellule.Matches(l.Groups[1].Value);
            if (cellules.Count < 4) continue;
            var od = OrdreDate.Match(cellules[0].Groups[1].Value);
            var lien = LienVote.Match(cellules[1].Groups[2].Value);
            if (!od.Success || !lien.Success) continue;
            if (int.Parse(lien.Groups[2].Value) != legislature || int.Parse(lien.Groups[3].Value) != session) continue;
            var d = Decomptes.Match(Brut(cellules[1].Groups[2].Value));
            var titre = Brut(lien.Groups[4].Value);
            var projet = Brut(cellules[2].Groups[2].Value);
            var resultat = Brut(cellules[3].Groups[2].Value);
            liste.Add(new VoteSenatCa(
                legislature, session, int.Parse(lien.Groups[1].Value), int.Parse(od.Groups[2].Value),
                DateOnly.ParseExact(od.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture), titre, null,
                d.Success ? int.Parse(d.Groups[1].Value) : 0, d.Success ? int.Parse(d.Groups[2].Value) : 0,
                d.Success ? int.Parse(d.Groups[3].Value) : 0, d.Success ? int.Parse(d.Groups[4].Value) : 0,
                resultat.Length == 0 ? null : resultat, projet.Length == 0 ? null : projet));
        }
        return liste;
    }

    /// <summary>
    /// Lit le tableau d'un vote : une ligne par sénateur en fonction ce jour-là (identifiant stable d'une session à l'autre, « Nom, Prénom », sigle du groupe, province) et trois cases
    /// (Yea, Nay, Abstention). Une ligne avec plus d'une case cochée est incohérente : elle est lue comme « n'a pas voté » (jamais d'invention). Liste vide si le tableau est absent.
    /// </summary>
    public static IReadOnlyList<BulletinSenatCa> ParserDetail(string html)
    {
        var liste = new List<BulletinSenatCa>();
        var i = html.IndexOf("sc-vote-details-table", StringComparison.Ordinal);
        if (i < 0) return liste;
        foreach (Match l in Ligne.Matches(Commentaires.Replace(html[i..], "")))
        {
            var c = Cellule.Matches(l.Groups[1].Value);
            if (c.Count < 6) continue;   // l'en-tête utilise <th>, jamais <td>
            var lien = LienSenateur.Match(c[0].Groups[2].Value);
            if (!lien.Success) continue;
            var nomComplet = Brut(c[0].Groups[2].Value);
            var virgule = nomComplet.IndexOf(", ", StringComparison.Ordinal);
            var (nom, prenom) = virgule < 0 ? (nomComplet, "") : (nomComplet[..virgule], nomComplet[(virgule + 2)..]);
            var marques = new[] { "Yea", "Nay", "Abstention" }.Where((_, k) => c[3 + k].Groups[2].Value.Contains("<i", StringComparison.OrdinalIgnoreCase)).ToList();
            var province = Brut(c[2].Groups[2].Value);
            liste.Add(new BulletinSenatCa(int.Parse(lien.Groups[1].Value), prenom, nom, Brut(c[1].Groups[2].Value), province.Length == 0 ? null : province, marques.Count == 1 ? marques[0] : null));
        }
        return liste;
    }

    /// <summary>
    /// Groupes du Sénat (sigles de la source, version anglaise) : sigle affiché, nom, ordre dans l'hémicycle et couleur de départ (modifiables en base, l'import ne les écrase pas).
    /// Ce ne sont pas tous des partis : le Sénat compte des groupes parlementaires indépendants, d'où le mot « groupe ». Couleurs choisies pour se distinguer, sans signification politique ;
    /// un groupe inconnu (nouveau sigle) reçoit un gris et l'ordre du milieu : jamais de nom ni de couleur inventés.
    /// </summary>
    private static readonly Dictionary<string, (string Sigle, string Nom, int Ordre, string Couleur)> Groupes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GRO"] = ("GRO", "Government Representative Office", 1, "#d71920"),
        ["PSG"] = ("PSG", "Progressive Senate Group", 2, "#b5179e"),
        ["ISG"] = ("ISG", "Independent Senators Group", 3, "#2a9d8f"),
        ["Non-affiliated"] = ("NA", "Non-affiliated", 5, "#8a8f98"),
        ["CSG"] = ("CSG", "Canadian Senators Group", 6, "#e9a23b"),
        ["C"] = ("C", "Conservative Party of Canada", 7, "#1a4782"),
    };

    /// <summary>Groupe d'une législature (un identifiant par législature, comme les caucus de la Chambre des communes).</summary>
    public static GroupeRow Groupe(int legislature, string sigleSource)
    {
        var (sigle, nom, ordre, couleur) = Groupes.TryGetValue(sigleSource, out var g) ? g : (sigleSource, sigleSource, 4, "#8a8f98");
        return new GroupeRow(IdGroupe(legislature, sigleSource), Chambres.CaSenate, legislature, sigle, nom, couleur, ordre);
    }

    /// <summary>Position d'après la case cochée : Yea = pour, Nay = contre, Abstention = abstention ; null (aucune case) = n'a pas voté.</summary>
    public static string? Position(string? valeur) => valeur switch
    {
        "Yea" => Positions.Pour,
        "Nay" => Positions.Contre,
        "Abstention" => Positions.Abstention,
        _ => null,
    };

    /// <summary>
    /// Vote sur l'ensemble d'un projet de loi : le dernier segment du titre est « Third Reading » (<b>calculé</b> d'après le titre, la source ne le signale pas). Un amendement ou un ajournement
    /// à la troisième lecture (« … – Third Reading – Amendment (Sen. X) ») n'en est pas un : le segment final est alors différent.
    /// </summary>
    public static bool EstVoteFinal(string titre) => Regex.IsMatch(titre.Trim(), @"(^|\s[–-]\s)third reading$", RegexOptions.IgnoreCase);

    /// <summary>
    /// Convertit un vote. Décomptes = OFFICIELS (liste) ; positions = cases cochées du tableau ; « Adopted » / « Defeated » → adopté / rejeté (autre valeur : pas de résultat, jamais deviné).
    /// Les sénateurs listés sans case n'ont pas voté (absents, non stockés). Un écart entre le décompte et les cases rend le vote « incomplet » et est dit dans la note.
    /// </summary>
    public static ScrutinAvecVotes VersScrutin(VoteSenatCa v, IReadOnlyList<BulletinSenatCa> bulletins)
    {
        var id = IdScrutin(v.Parlement, Numero(v));
        var lignes = new List<VoteRow>();
        foreach (var b in bulletins)
        {
            if (Position(b.Valeur) is not { } pos) continue;
            lignes.Add(new VoteRow(id, IdMembre(b.SenateurId), IdGroupe(v.Parlement, b.Groupe), pos, false, false, pos));
        }
        var pour = lignes.Count(l => l.Position == Positions.Pour);
        var contre = lignes.Count(l => l.Position == Positions.Contre);
        var abst = lignes.Count(l => l.Position == Positions.Abstention);
        var notes = new List<string> { $"Sitting of {v.Date:yyyy-MM-dd}, vote {v.Ordre} of session {v.Parlement}-{v.Session} (Senate vote id {v.Id})." };
        if (pour != v.Oui || contre != v.Non || abst != v.Abstentions)
            notes.Add($"Official count: {v.Oui} Yea, {v.Non} Nay, {v.Abstentions} Abstention; senator list: {pour} Yea, {contre} Nay, {abst} Abstention.");
        var manquants = Math.Max(0, v.Oui + v.Non + v.Abstentions - lignes.Count);
        var sort = v.Resultat switch { "Adopted" => "adopté", "Defeated" => "rejeté", _ => null };
        var scrutin = new ScrutinRow(
            id, Chambres.CaSenate, v.Parlement, Numero(v), v.Date, v.TitreFr ?? v.TitreEn, "recorded vote", sort, AdresseOfficielle(v), EstVoteFinal(v.TitreEn),
            v.Total > 0 ? v.Total : v.Oui + v.Non + v.Abstentions, v.Oui, v.Non, v.Abstentions, 0, null, manquants, string.Join(" ", notes),
            null, null, null, null, v.TitreEn);   // le numéro du projet de loi est dans le titre : pas de « type de vote » inventé
        return new ScrutinAvecVotes(scrutin, lignes);
    }

    public void Dispose() => _http.Dispose();
}
