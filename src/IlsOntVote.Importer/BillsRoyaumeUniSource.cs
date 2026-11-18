using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IlsOntVote.Importer;

/// <summary>Une séance d'une étape d'un projet de loi (une étape peut avoir plusieurs séances : commission, rapport…).</summary>
public sealed record EtapeBill(int Ordre, string Libelle, string? Chambre, DateTime? DateSeance);

/// <summary>Un projet de loi du Parlement britannique avec ses étapes.</summary>
public sealed record BillRow(
    long Id, string Titre, string? TitreLong, string? ChambreOrigine, string? ChambreCourante, int? SessionId, bool EstLoi, DateTime? DateLoi,
    DateTime? RetireLe, bool Rejete, string? EtapeCourante, DateTime? DateDepot, DateTime? DerniereMaj, string Url, IReadOnlyList<EtapeBill> Etapes);

/// <summary>
/// Projets de loi du Parlement britannique via <c>bills-api.parliament.uk</c> (JSON public, Open Parliament Licence). Parlement courant : sessions <see cref="Sessions"/>
/// (la 59e législature, ouverte en juillet 2024, compte les sessions 39 et 40 pour la source). Liste par pages de 100 puis un appel « Stages » par projet NOUVEAU OU MODIFIÉ
/// (lastUpdate plus récent que celui déjà en base) : un passage quotidien ne relit que quelques projets.
/// </summary>
public sealed class BillsRoyaumeUniSource : IDisposable
{
    private const string Racine = "https://bills-api.parliament.uk/api/v1";
    public static readonly int[] Sessions = [39, 40];
    private const int Page = 100;
    private readonly HttpClient _http = Telechargement.CreerClient(TimeSpan.FromMinutes(2));

    /// <summary>Résumés (sans étapes détaillées) de tous les projets des sessions courantes.</summary>
    public async Task<IReadOnlyList<JsonElement>> ListerAsync()
    {
        var items = new Dictionary<long, JsonElement>();
        foreach (var session in Sessions)
            for (var skip = 0; ; skip += Page)
            {
                using var doc = JsonDocument.Parse(await _http.GetStringAsync($"{Racine}/Bills?Session={session}&Take={Page}&Skip={skip}"));
                var tab = doc.RootElement.GetProperty("items");
                foreach (var b in tab.EnumerateArray()) items[b.GetProperty("billId").GetInt64()] = b.Clone();   // un projet reporté d'une session à l'autre n'est compté qu'une fois
                if (tab.GetArrayLength() < Page) break;
                await Task.Delay(150);
            }
        return [.. items.Values];
    }

    /// <summary>Étapes d'un projet (appel dédié) puis conversion en ligne complète ; <paramref name="detail"/> = résumé de la liste.</summary>
    public async Task<BillRow> LireAsync(JsonElement resume)
    {
        var id = resume.GetProperty("billId").GetInt64();
        using var etapes = JsonDocument.Parse(await _http.GetStringAsync($"{Racine}/Bills/{id}/Stages"));
        string? longTitle = null;
        try
        {
            using var detail = JsonDocument.Parse(await _http.GetStringAsync($"{Racine}/Bills/{id}"));
            longTitle = Texte(detail.RootElement, "longTitle");
        }
        catch (HttpRequestException) { /* objet du projet indisponible : le titre court suffit */ }
        return Convertir(resume, etapes.RootElement.GetProperty("items"), longTitle);
    }

    /// <summary>Conversion pure (testée sans réseau) : résumé de la liste + étapes + objet du projet.</summary>
    public static BillRow Convertir(JsonElement resume, JsonElement etapes, string? titreLong)
    {
        var liste = new List<EtapeBill>();
        foreach (var e in etapes.EnumerateArray())
        {
            var ordre = e.TryGetProperty("sortOrder", out var o) && o.ValueKind == JsonValueKind.Number ? o.GetInt32() : 0;
            var libelle = Texte(e, "description");
            var chambre = Texte(e, "house");
            var seances = e.TryGetProperty("stageSittings", out var ss) && ss.ValueKind == JsonValueKind.Array ? ss.EnumerateArray().ToList() : [];
            if (seances.Count == 0) liste.Add(new EtapeBill(ordre, libelle, Vide(chambre), null));
            foreach (var s in seances) liste.Add(new EtapeBill(ordre, libelle, Vide(chambre), Date(s, "date")));
        }
        var depot = liste.Where(e => e.DateSeance is not null).Select(e => e.DateSeance).Min();
        var sanction = liste.Where(e => e.Libelle.Contains("Royal Assent", StringComparison.OrdinalIgnoreCase) && e.DateSeance is not null).Select(e => e.DateSeance).Max();
        var id = resume.GetProperty("billId").GetInt64();
        var courante = resume.TryGetProperty("currentStage", out var cs) && cs.ValueKind == JsonValueKind.Object ? Texte(cs, "description") : "";
        return new BillRow(
            id, Texte(resume, "shortTitle"), Vide(titreLong ?? ""), Vide(Texte(resume, "originatingHouse")), Vide(Texte(resume, "currentHouse")),
            resume.TryGetProperty("introducedSessionId", out var si) && si.ValueKind == JsonValueKind.Number ? si.GetInt32() : null,
            resume.TryGetProperty("isAct", out var act) && act.ValueKind == JsonValueKind.True,
            sanction, Date(resume, "billWithdrawn"),
            resume.TryGetProperty("isDefeated", out var df) && df.ValueKind == JsonValueKind.True,
            Vide(courante), depot, Date(resume, "lastUpdate"), $"https://bills.parliament.uk/bills/{id}", liste);
    }

    // ------------------------------------------------------------------ rattachement des divisions

    /// <summary>Titre normalisé pour comparer : minuscules, sans « [HL] » (projet des Lords), ponctuation et espaces écrasés.</summary>
    public static string Normaliser(string titre)
    {
        var t = Regex.Replace(titre.ToLowerInvariant(), @"\[(?:hl|lords)\]", " ");
        t = Regex.Replace(t, @"[^\p{L}\p{N}]+", " ");
        return t.Trim();
    }

    /// <summary>
    /// Titres normalisés sous lesquels les divisions peuvent désigner un projet. Piège constaté : quand un projet devient loi, la source RENOMME son titre (« Steel Industry (Nationalisation) Bill »
    /// devient « Steel Industry (Nationalisation) Act 2026 ») alors que les divisions votées avant la sanction gardent « … Bill » : sans cette variante, aucune division d'un projet
    /// devenu loi (la majorité) n'était rattachée. Variantes : le titre tel quel, et pour « … Act [année] » la forme « … Bill ».
    /// </summary>
    public static IReadOnlyList<string> VariantesTitre(string titre)
    {
        var norm = Normaliser(titre);
        var variantes = new List<string> { norm };
        var loi = Regex.Match(norm, @"^(.*?)\s+act(?:\s+\d{4})?$");
        if (loi.Success && loi.Groups[1].Value.Length > 0) variantes.Add(loi.Groups[1].Value + " bill");
        return variantes;
    }

    /// <summary>Projet candidat pour le rattachement : titre normalisé et période pendant laquelle des divisions le concernent.</summary>
    public sealed record Candidat(long Id, string TitreNorm, DateTime Debut, DateTime Fin);

    /// <summary>
    /// Projet auquel se rattache une division, d'après son titre. Règle : le titre normalisé de la division COMMENCE par le titre normalisé du projet, suivi d'une frontière de mot ;
    /// le plus long titre l'emporte (« Finance Bill » ne capte pas « Finance (No. 2) Bill ») ; à titre égal (un même nom revient chaque année), le projet dont la période
    /// [première séance, dernière séance + 7 jours] contient la date de la division ; sinon aucun rattachement (jamais une approximation). Renvoie aussi la fin du titre
    /// (étape ou amendement : « Third Reading », « New Clause 2 »…).
    /// </summary>
    public static (long BillId, string? Etape)? Rattacher(string titreDivision, DateTime date, IReadOnlyList<Candidat> candidats)
    {
        var t = Normaliser(titreDivision);
        Candidat? meilleur = null;
        foreach (var c in candidats)
        {
            if (c.TitreNorm.Length == 0 || !(t == c.TitreNorm || t.StartsWith(c.TitreNorm + " ", StringComparison.Ordinal))) continue;
            if (date < c.Debut || date > c.Fin) continue;
            if (meilleur is null || c.TitreNorm.Length > meilleur.TitreNorm.Length || (c.TitreNorm.Length == meilleur.TitreNorm.Length && c.Debut > meilleur.Debut)) meilleur = c;
        }
        if (meilleur is null) return null;
        // Fin du titre d'origine (après le nom du projet, avec ses éventuels « [HL] » : on repart du titre brut et retire le plus petit préfixe correspondant).
        var brut = Regex.Replace(titreDivision, @"\s+", " ").Trim();
        var etape = ExtraireEtape(brut, meilleur.TitreNorm);
        return (meilleur.Id, etape);
    }

    private static string? ExtraireEtape(string brut, string titreNorm)
    {
        // Avance dans le titre brut jusqu'à ce que sa forme normalisée égale (ou dépasse) celle du nom du projet : le reste est l'étape.
        for (var i = titreNorm.Length; i <= brut.Length; i++)
        {
            if (Normaliser(brut[..i]) != titreNorm) continue;
            var reste = brut[i..].TrimStart(' ', ':', '-', '–').Trim();
            return reste.Length == 0 ? null : (reste.Length > 160 ? reste[..160] : reste);
        }
        return null;
    }

    private static string Texte(JsonElement e, string nom) =>
        e.TryGetProperty(nom, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";

    private static string? Vide(string s) => s.Length == 0 ? null : s;

    private static DateTime? Date(JsonElement e, string nom) =>
        e.TryGetProperty(nom, out var v) && v.ValueKind == JsonValueKind.String
        && DateTime.TryParse(v.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? d : null;

    public void Dispose() => _http.Dispose();
}
