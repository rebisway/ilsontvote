using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IlsOntVote.Importer;

/// <summary>Un pair vu dans une division (a voté « Content » ou « Not content »).</summary>
public sealed record VotantLords(int MemberId, string Nom, string Sigle, string Parti, string? Couleur);

/// <summary>Une division de la Chambre des Lords telle que publiée, avec les pairs qui ont voté (les absents ne sont pas listés par la source).</summary>
public sealed record DivisionLords(
    int Id, DateOnly Date, string Titre, string? Notes, int OfficielContent, int OfficielNonContent,
    IReadOnlyList<VotantLords> Content, IReadOnlyList<VotantLords> NonContent);

/// <summary>Un pair avec ses dates d'appartenance à la Chambre (annuaire daté de la source des membres).</summary>
public sealed record PairLords(int Id, string Nom, string? Sigle, string? Parti, string? Couleur, DateOnly Debut, DateOnly? Fin);

/// <summary>
/// Chambre des Lords (Royaume-Uni) : divisions via <c>lordsvotes-api.parliament.uk</c> (JSON public) et pairs via <c>members-api.parliament.uk</c> (Open Parliament Licence).
/// Différence essentielle avec les Communes : la source ne liste QUE les pairs qui ont voté (pas de « sans vote enregistré »). Les absents se déduisent donc des
/// mandats (dates d'entrée et de sortie de l'annuaire des membres), comme pour l'Assemblée. Limite à dire : un pair en congé (« leave of absence ») ou suspendu
/// reste compté absent, car la source ne donne que le statut du jour, pas son historique.
/// </summary>
public sealed class LordsSource : IDisposable
{
    private const string RacineVotes = "https://lordsvotes-api.parliament.uk/data";
    private const string RacineMembres = "https://members-api.parliament.uk/api";

    /// <summary>L'API des divisions plafonne à 25 par page, celle des membres à 20 (constaté).</summary>
    private const int PageDivisions = 25, PageMembres = 20;

    /// <summary>Code de la Chambre des Lords dans l'API des membres (1 = Communes, 2 = Lords).</summary>
    private const int ChambreLords = 2;

    private readonly HttpClient _http = Telechargement.CreerClient(TimeSpan.FromMinutes(2));

    /// <summary>Divisions de <paramref name="depuis"/> à <paramref name="jusqua"/> inclus, avec leurs listes de votants (une seule requête par page de 25).</summary>
    public async Task<IReadOnlyList<DivisionLords>> ListerDivisionsAsync(DateOnly depuis, DateOnly? jusqua = null)
    {
        var liste = new List<DivisionLords>();
        var vus = new HashSet<int>();
        var fin = jusqua is null ? "" : $"&queryParameters.endDate={jusqua:yyyy-MM-dd}";
        // Nombre total : l'API des divisions ne rend JAMAIS une page vide (au-delà de la fin elle renvoie encore 25 divisions : constaté), une boucle « jusqu'à la page vide »
        // ne s'arrêterait donc pas. On pagine jusqu'au total annoncé, et on s'arrête aussi dès qu'une page n'apporte aucune division nouvelle.
        var total = int.Parse((await _http.GetStringAsync($"{RacineVotes}/Divisions/searchTotalResults?queryParameters.startDate={depuis:yyyy-MM-dd}{fin}")).Trim(), CultureInfo.InvariantCulture);
        for (var skip = 0; skip < total; skip += PageDivisions)
        {
            var json = await _http.GetStringAsync($"{RacineVotes}/Divisions/search?queryParameters.startDate={depuis:yyyy-MM-dd}{fin}&skip={skip}&take={PageDivisions}");
            var nouvelles = AnalyserPage(json).Where(d => vus.Add(d.Id)).ToList();
            if (nouvelles.Count == 0) break;
            liste.AddRange(nouvelles);
            await Task.Delay(200);   // politesse : API publique partagée
        }
        return liste;
    }

    /// <summary>Analyse une page de résultats (séparée du réseau pour être testée).</summary>
    public static List<DivisionLords> AnalyserPage(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var liste = new List<DivisionLords>();
        foreach (var d in doc.RootElement.EnumerateArray())
        {
            liste.Add(new DivisionLords(
                d.GetProperty("divisionId").GetInt32(), DateOnly.FromDateTime(d.GetProperty("date").GetDateTime()),
                Texte(d, "title"), TexteNotes(Texte(d, "amendmentMotionNotes")),
                Entier(d, "authoritativeContentCount"), Entier(d, "authoritativeNotContentCount"),
                Votants(d, "contents"), Votants(d, "notContents")));
        }
        return liste;
    }

    /// <summary>
    /// Notes de la division : HTML de la source (« Baroness X moved amendment 16… ») réduit à du texte, sans mise en forme, borné. Elles expliquent l'objet du vote que le titre
    /// (« Railways Bill ») ne dit pas ; reprises telles que publiées, jamais réécrites.
    /// </summary>
    public static string? TexteNotes(string html)
    {
        if (html.Length == 0) return null;
        var t = WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " "));
        t = Regex.Replace(t, @"\s+", " ").Trim();
        if (t.Length == 0) return null;
        return t.Length > 600 ? t[..600] + "…" : t;
    }

    private static IReadOnlyList<VotantLords> Votants(JsonElement division, string liste)
    {
        var res = new List<VotantLords>();
        if (!division.TryGetProperty(liste, out var tab) || tab.ValueKind != JsonValueKind.Array) return res;
        foreach (var m in tab.EnumerateArray())
        {
            var couleur = Texte(m, "partyColour");
            // Sigle vide (pair sans parti, président de séance…) : le libellé du parti sert de sigle, comme aux Communes ; sans libellé : « NA » (non rattaché).
            var parti = Texte(m, "party");
            var sigle = Texte(m, "partyAbbreviation") is { Length: > 0 } s ? s : parti.Length > 0 ? parti : "NA";
            res.Add(new VotantLords(m.GetProperty("memberId").GetInt32(), Texte(m, "name"), sigle, parti.Length > 0 ? parti : sigle, couleur.Length == 0 ? null : "#" + couleur));
        }
        return res;
    }

    /// <summary>Pairs ayant été membres de la Chambre à un moment de [<paramref name="du"/>, <paramref name="au"/>] (annuaire daté).</summary>
    public async Task<IReadOnlyList<PairLords>> ListerPairsAsync(DateOnly du, DateOnly au)
    {
        var liste = new List<PairLords>();
        for (var skip = 0; ; skip += PageMembres)
        {
            var json = await _http.GetStringAsync(
                $"{RacineMembres}/Members/Search?House={ChambreLords}&MembershipInDateRange.WasMemberOnOrAfter={du:yyyy-MM-dd}&MembershipInDateRange.WasMemberOnOrBefore={au:yyyy-MM-dd}&take={PageMembres}&skip={skip}");
            var page = AnalyserPairs(json);
            liste.AddRange(page);
            if (page.Count < PageMembres) break;
            await Task.Delay(150);
        }
        return liste;
    }

    /// <summary>Analyse une page de l'annuaire des membres.</summary>
    public static List<PairLords> AnalyserPairs(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var liste = new List<PairLords>();
        foreach (var it in doc.RootElement.GetProperty("items").EnumerateArray())
        {
            var v = it.GetProperty("value");
            if (!v.TryGetProperty("latestHouseMembership", out var ms) || ms.ValueKind != JsonValueKind.Object) continue;
            if (!ms.TryGetProperty("membershipStartDate", out var sd) || sd.ValueKind != JsonValueKind.String) continue;   // sans date d'entrée : aucun mandat déductible, jamais inventé
            DateOnly? fin = ms.TryGetProperty("membershipEndDate", out var ed) && ed.ValueKind == JsonValueKind.String ? DateOnly.FromDateTime(ed.GetDateTime()) : null;
            string? sigle = null, parti = null, couleur = null;
            if (v.TryGetProperty("latestParty", out var p) && p.ValueKind == JsonValueKind.Object)
            {
                parti = Texte(p, "name"); sigle = Texte(p, "abbreviation");
                if (string.IsNullOrEmpty(sigle)) sigle = parti;
                var c = Texte(p, "backgroundColour"); couleur = c.Length == 0 ? null : "#" + c;
            }
            liste.Add(new PairLords(v.GetProperty("id").GetInt32(), Texte(v, "nameDisplayAs"), string.IsNullOrEmpty(sigle) ? null : sigle, parti, couleur,
                DateOnly.FromDateTime(sd.GetDateTime()), fin));
        }
        return liste;
    }

    // ------------------------------------------------------------------ conversion

    public static string IdMembre(int memberId) => $"LORDS-{memberId}";
    public static string IdGroupe(string sigle) => $"LORDS-{sigle}";
    public static string IdScrutin(int divisionId) => $"LORDS-{divisionId}";

    /// <summary>Groupe au gouvernement par défaut (période en cours) : le Parti travailliste depuis juillet 2024.</summary>
    public const string GroupeGouvernement = "LORDS-Lab";

    /// <summary>Phrase ajoutée à la note de chaque division : la limite des absents déduits, dite là où la donnée s'affiche (règle du doute).</summary>
    public const string NoteAbsents =
        "Only peers who voted are published. Peers shown as absent are deduced from their membership dates: peers on leave of absence or suspended cannot be told apart from absent ones.";

    /// <summary>
    /// Convertit une division en scrutin + votes. « Content » = pour, « Not content » = contre ; pas d'abstention formelle. Résultat « adopté » / « rejeté » CALCULÉ d'après
    /// les décomptes officiels (égalité : indéterminé). Si les listes nomment moins de pairs que le décompte officiel, le scrutin est marqué incomplet (les pairs non nommés
    /// apparaîtraient absents à tort) et la note donne les deux chiffres.
    /// </summary>
    public static ScrutinAvecVotes VersScrutin(DivisionLords d)
    {
        var id = IdScrutin(d.Id);
        // Un pair ne peut figurer qu'une fois : si la source le liste dans les deux camps (erreur de saisie), il est compté abstention, comme aux Communes.
        var deuxListes = d.Content.Select(m => m.MemberId).Intersect(d.NonContent.Select(m => m.MemberId)).ToHashSet();
        var votes = d.Content.Where(m => !deuxListes.Contains(m.MemberId)).Select(m => Vote(id, m, Positions.Pour))
            .Concat(d.NonContent.Where(m => !deuxListes.Contains(m.MemberId)).Select(m => Vote(id, m, Positions.Contre)))
            .Concat(d.Content.Where(m => deuxListes.Contains(m.MemberId)).Select(m => Vote(id, m, Positions.Abstention)))
            .GroupBy(v => v.ParlementaireId).Select(g => g.First()).ToList();
        var manquants = Math.Max(0, d.OfficielContent - d.Content.Count) + Math.Max(0, d.OfficielNonContent - d.NonContent.Count);
        var notes = new List<string>();
        if (d.Notes is not null) notes.Add(d.Notes);
        if (manquants > 0)
            notes.Add($"Official count: {d.OfficielContent} content, {d.OfficielNonContent} not content; peers named in the published lists: {d.Content.Count} content, {d.NonContent.Count} not content. The result is computed from the official count.");
        if (deuxListes.Count > 0) notes.Add($"{deuxListes.Count} peer(s) appear in both lists: recorded as an abstention.");
        notes.Add(NoteAbsents);
        var scrutin = new ScrutinRow(
            id, Chambres.Lords, CommonsSource.LegislatureDu(d.Date), d.Id, d.Date, d.Titre, "division", CommonsSource.Sort(d.OfficielContent, d.OfficielNonContent), null, false,
            d.OfficielContent + d.OfficielNonContent, d.OfficielContent, d.OfficielNonContent, deuxListes.Count, null, null,
            manquants, string.Join(" ", notes), "Recorded division (Content / Not content; no formal abstention in the Lords)", null, null, null, d.Titre);
        return new ScrutinAvecVotes(scrutin, votes);
    }

    private static VoteRow Vote(string scrutinId, VotantLords m, string position) =>
        new(scrutinId, IdMembre(m.MemberId), IdGroupe(m.Sigle), position, false, false, position);

    private static string Texte(JsonElement e, string nom) =>
        e.TryGetProperty(nom, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";

    private static int Entier(JsonElement e, string nom) =>
        e.TryGetProperty(nom, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

    public void Dispose() => _http.Dispose();
}
