using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace IlsOntVote.Importer;

/// <summary>Effectifs d'amendements d'UN dossier législatif de l'Assemblée, tels que publiés (aucune interprétation).</summary>
public sealed record AmendementsDossier(string DossierAn, int Deposes, int Adoptes, int Rejetes, int Tombes, int NonSoutenus, int Retires, int Irrecevables);

/// <summary>
/// Jeu officiel « Amendements » de l'Assemblée nationale (data.assemblee-nationale.fr, <c>…/{législature}/loi/amendements_div_legis/Amendements.json.zip</c>) :
/// un fichier JSON par amendement, rangé sous <c>json/{dossier}/{texte}/AM….json</c>. Le zip fait ~365 Mo par législature (148 760 amendements pour la 17e) :
/// il est LU EN FLUX (entrée par entrée, jamais extrait sur disque) et seuls les effectifs par dossier sont gardés.
/// </summary>
public static class AmendementsSource
{
    /// <summary>Législatures publiées : la 15e n'existe pas dans ce jeu (404 constaté), la 16e et la 17e oui.</summary>
    public static readonly int[] LegislaturesDisponibles = [16, 17];

    public static string Url(int legislature) =>
        $"https://data.assemblee-nationale.fr/static/openData/repository/{legislature}/loi/amendements_div_legis/Amendements.json.zip";

    /// <summary>Catégorie d'un amendement d'après les libellés de la source.</summary>
    public enum Categorie { Adopte, Rejete, Tombe, NonSoutenu, Retire, Irrecevable, Autre }

    /// <summary>
    /// Classe un amendement d'après son « sort » (adopté, rejeté, tombé, non soutenu, retiré) et, à défaut, son « état » (irrecevable, retiré avant discussion).
    /// Sans sort ni état concluant (« A discuter », « En traitement »…), l'amendement est « Autre » : il compte dans les déposés, pas dans les autres colonnes.
    /// Comparaison sans accents ni casse : les libellés de la source ne sont pas normalisés (« Retiré », « Retiré après publication »…).
    /// </summary>
    public static Categorie Classer(string? sort, string? etat)
    {
        switch (SansAccents(sort))
        {
            case "adopte": return Categorie.Adopte;
            case "rejete": return Categorie.Rejete;
            case "tombe": return Categorie.Tombe;
            case "non soutenu": return Categorie.NonSoutenu;
            case "retire": return Categorie.Retire;
        }
        var e = SansAccents(etat);
        if (e.StartsWith("irrecevable", StringComparison.Ordinal)) return Categorie.Irrecevable;
        if (e == "retire") return Categorie.Retire;
        return Categorie.Autre;
    }

    private static string SansAccents(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var d = s.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    /// <summary>
    /// Lit le zip et totalise par dossier. <paramref name="sansDossier"/> : fichiers dont le chemin ne porte pas de référence de dossier « DLR5L… »
    /// (ignorés, comptés pour le message : on ne devine pas un rattachement).
    /// </summary>
    public static IReadOnlyList<AmendementsDossier> Lire(string cheminZip, out int sansDossier, out int illisibles)
    {
        var tot = new Dictionary<string, int[]>();   // dossier -> [déposés, adoptés, rejetés, tombés, non soutenus, retirés, irrecevables]
        sansDossier = 0; illisibles = 0;
        using var zip = ZipFile.OpenRead(cheminZip);
        foreach (var entree in zip.Entries)
        {
            if (!entree.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
            var segments = entree.FullName.Split('/');
            // json/DLR5L17N53886/PIONANR5L17B2851/AMANR….json : le dossier est le segment qui suit « json ».
            var dossier = segments.Length >= 3 ? segments[1] : "";
            if (!dossier.StartsWith("DLR5L", StringComparison.Ordinal)) { sansDossier++; continue; }

            string? sort = null, etat = null;
            try
            {
                using var flux = entree.Open();
                using var doc = JsonDocument.Parse(flux);
                if (!doc.RootElement.TryGetProperty("amendement", out var a) || !a.TryGetProperty("cycleDeVie", out var cycle)) { illisibles++; continue; }
                // « sort » est absent ou remplacé par un objet nul (@xsi:nil) tant que l'amendement n'a pas été traité : seul un texte compte.
                if (cycle.TryGetProperty("sort", out var s) && s.ValueKind == JsonValueKind.String) sort = s.GetString();
                if (cycle.TryGetProperty("etatDesTraitements", out var t) && t.ValueKind == JsonValueKind.Object
                    && t.TryGetProperty("etat", out var et) && et.ValueKind == JsonValueKind.Object
                    && et.TryGetProperty("libelle", out var l) && l.ValueKind == JsonValueKind.String) etat = l.GetString();
            }
            catch (JsonException) { illisibles++; continue; }

            if (!tot.TryGetValue(dossier, out var c)) tot[dossier] = c = new int[7];
            c[0]++;
            switch (Classer(sort, etat))
            {
                case Categorie.Adopte: c[1]++; break;
                case Categorie.Rejete: c[2]++; break;
                case Categorie.Tombe: c[3]++; break;
                case Categorie.NonSoutenu: c[4]++; break;
                case Categorie.Retire: c[5]++; break;
                case Categorie.Irrecevable: c[6]++; break;
            }
        }
        return tot.OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new AmendementsDossier(kv.Key, kv.Value[0], kv.Value[1], kv.Value[2], kv.Value[3], kv.Value[4], kv.Value[5], kv.Value[6])).ToList();
    }
}
