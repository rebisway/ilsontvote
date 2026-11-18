using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IlsOntVote.Importer;

/// <summary>
/// Pétitions du Parlement britannique (petition.parliament.uk), via son API JSON publique (Open Parliament Licence) : <c>/petitions.json?state=open|closed&amp;page=N</c>,
/// 25 pétitions par page (taille imposée par la source), pages chaînées par <c>links.next</c>. Environ 2 200 pétitions ouvertes et 6 100 clôturées (330 pages).
/// Ne sont PAS importées : les pétitions rejetées avant publication (~9 000, sans soutiens significatifs, hors du sujet de l'historique des soutiens).
/// Les identifiants sont préfixés « UK- » (les numéros des plateformes françaises se recoupent) ; la plateforme est <see cref="Plateforme"/>.
/// </summary>
public sealed class PetitionsRoyaumeUniSource : IDisposable
{
    public const string Plateforme = "UK";
    private const string Racine = "https://petition.parliament.uk";

    /// <summary>États importés (valeurs du paramètre « state » de la source).</summary>
    private static readonly string[] Etats = ["open", "closed"];

    private readonly HttpClient _http = Telechargement.CreerClient(TimeSpan.FromMinutes(2));

    /// <summary>Toutes les pétitions ouvertes puis clôturées, page par page ; <paramref name="journal"/> reçoit une ligne par état (compteur).</summary>
    public async Task<IReadOnlyList<PetitionRow>> LireToutesAsync(Action<string>? journal = null)
    {
        var lignes = new Dictionary<string, PetitionRow>();
        foreach (var etat in Etats)
        {
            var url = $"{Racine}/petitions.json?state={etat}&page=1";
            var pages = 0;
            while (url is not null)
            {
                var json = await _http.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                foreach (var item in doc.RootElement.GetProperty("data").EnumerateArray())
                {
                    var p = Convertir(item);
                    lignes[p.Id] = p;   // une pétition qui change d'état entre deux pages (ouverte puis clôturée) n'est comptée qu'une fois
                }
                url = doc.RootElement.GetProperty("links").TryGetProperty("next", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                pages++;
                await Task.Delay(150);   // politesse : API publique partagée
            }
            journal?.Invoke($"  {etat} : {pages} pages lues.");
        }
        return [.. lignes.Values];
    }

    /// <summary>Convertit une entrée de l'API en ligne de pétition (séparé du réseau pour être testé).</summary>
    public static PetitionRow Convertir(JsonElement item)
    {
        var a = item.GetProperty("attributes");
        var id = item.GetProperty("id").GetInt64();
        var fond = Texte(a, "background");
        var plus = Texte(a, "additional_details");
        var description = string.IsNullOrWhiteSpace(plus) ? fond : fond + "\n\n" + plus;

        JsonElement? reponse = a.TryGetProperty("government_response", out var r) && r.ValueKind == JsonValueKind.Object ? r : null;
        JsonElement? debat = a.TryGetProperty("debate", out var d) && d.ValueKind == JsonValueKind.Object ? d : null;
        return new PetitionRow(
            $"UK-{id}", Plateforme, Texte(a, "action"), description.Length == 0 ? null : description,
            Date(a, "opened_at") ?? Date(a, "created_at"),
            a.TryGetProperty("signature_count", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt32() : null,
            Texte(a, "state"),
            Date(a, "closed_at"), null, $"{Racine}/petitions/{id}", null, null, null)
        {
            SeuilReponseLe = Date(a, "response_threshold_reached_at"),
            ReponseLe = reponse is { } rr ? Date(rr, "responded_on") ?? Date(rr, "created_at") : null,
            ReponseResume = reponse is { } r2 ? Vide(Texte(r2, "summary")) : null,
            SeuilDebatLe = Date(a, "debate_threshold_reached_at"),
            DebatPrevuLe = Date(a, "scheduled_debate_date"),
            DebatLe = debat is { } dd ? Date(dd, "debated_on") : null,
            DebatUrl = debat is { } d2 ? Vide(Texte(d2, "transcript_url")) : null,
        };
    }

    /// <summary>
    /// Empreinte du relevé : identifiant, état, soutiens, réponse et débat de chaque pétition (triés). Identique = rien n'a changé depuis le relevé précédent, rien à écrire.
    /// Les dates d'étape sont incluses : une réponse ou un débat qui apparaît sans changement du nombre de soutiens doit produire un nouveau relevé.
    /// </summary>
    public static string Empreinte(IEnumerable<PetitionRow> lignes)
    {
        var sb = new StringBuilder();
        foreach (var p in lignes.OrderBy(x => x.Id, StringComparer.Ordinal))
            sb.Append(p.Id).Append('|').Append(p.Statut).Append('|').Append(p.NbVotes).Append('|').Append(p.ReponseLe?.ToString("yyyy-MM-dd")).Append('|')
              .Append(p.DebatPrevuLe?.ToString("yyyy-MM-dd")).Append('|').Append(p.DebatLe?.ToString("yyyy-MM-dd")).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }

    private static string Texte(JsonElement e, string nom) =>
        e.TryGetProperty(nom, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";

    private static string? Vide(string s) => s.Length == 0 ? null : s;

    /// <summary>Date (jour) d'un champ ISO 8601 de la source (« 2024-11-20T09:13:21.152Z » ou « 2025-01-06 ») ; NULL si absent ou illisible.</summary>
    private static DateTime? Date(JsonElement e, string nom) =>
        e.TryGetProperty(nom, out var v) && v.ValueKind == JsonValueKind.String
        && DateTime.TryParse(v.GetString(), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var d) ? d.Date : null;

    public void Dispose() => _http.Dispose();
}
