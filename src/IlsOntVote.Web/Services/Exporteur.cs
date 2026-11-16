using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace IlsOntVote.Web.Services;

/// <summary>Une colonne exportable : intitulé + extraction de la valeur.</summary>
public sealed record Colonne<T>(string Entete, Func<T, object?> Valeur);

/// <summary>
/// Export de tableaux. CSV compatible Excel français (séparateur « ; », UTF-8 avec BOM, dates ISO)
/// et JSON (tableau d'objets, clés = intitulés de colonnes). Les formats sont une liste blanche :
/// l'URL ne peut pas en demander un autre.
/// </summary>
public static class Exporteur
{
    public static readonly string[] Formats = ["csv", "json"];

    public static bool EstFormat(string? format) => format is not null && Formats.Contains(format);

    public static FileContentResult Ecrire<T>(string format, string nomFichier, IReadOnlyList<Colonne<T>> colonnes, IEnumerable<T> lignes)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        return format == "json"
            ? new FileContentResult(Json(colonnes, lignes), "application/json; charset=utf-8") { FileDownloadName = $"{nomFichier}-{stamp}.json" }
            : new FileContentResult(Csv(colonnes, lignes), "text/csv; charset=utf-8") { FileDownloadName = $"{nomFichier}-{stamp}.csv" };
    }

    private static byte[] Csv<T>(IReadOnlyList<Colonne<T>> colonnes, IEnumerable<T> lignes)
    {
        var sb = new StringBuilder();
        sb.AppendJoin(';', colonnes.Select(c => Echapper(c.Entete))).Append("\r\n");
        foreach (var l in lignes)
            sb.AppendJoin(';', colonnes.Select(c => Echapper(Texte(c.Valeur(l))))).Append("\r\n");

        // BOM : Excel reconnaît ainsi l'UTF-8 (accents).
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    private static byte[] Json<T>(IReadOnlyList<Colonne<T>> colonnes, IEnumerable<T> lignes)
    {
        var objets = lignes.Select(l => colonnes.ToDictionary(c => c.Entete, c => Normaliser(c.Valeur(l))));
        return JsonSerializer.SerializeToUtf8Bytes(objets, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // accents lisibles
        });
    }

    private static object? Normaliser(object? v) => v switch
    {
        DateTime d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => v,
    };

    private static string Texte(object? v) => v switch
    {
        null => "",
        DateTime d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        bool b => b ? "oui" : "non",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString() ?? "",
    };

    // Protège aussi des « injections de formules » : une cellule commençant par = + - @ serait exécutée par Excel.
    private static string Echapper(string s)
    {
        if (s.Length > 0 && s[0] is '=' or '+' or '-' or '@' && !double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
            s = "'" + s;
        return s.IndexOfAny([';', '"', '\r', '\n']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }
}
