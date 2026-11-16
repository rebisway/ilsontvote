using System.Globalization;
using System.Text.Json;

namespace IlsOntVote.Importer;

/// <summary>
/// Les JSON de l'Assemblée viennent d'une conversion XML : un élément absent
/// vaut null, un élément unique est un objet, un élément répété est un tableau.
/// Ces helpers absorbent ces trois formes.
/// </summary>
internal static class JsonExtensions
{
    public static JsonElement? Get(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object
        && e.TryGetProperty(name, out var v)
        && v.ValueKind != JsonValueKind.Null
            ? v
            : null;

    public static JsonElement? Dig(this JsonElement e, params string[] names)
    {
        JsonElement? cur = e;
        foreach (var n in names)
        {
            if (cur is null) return null;
            cur = cur.Value.Get(n);
        }
        return cur;
    }

    public static string? Str(this JsonElement? e) =>
        e is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

    public static string? Str(this JsonElement e, string name) => e.Get(name).Str();

    public static string? Str(this JsonElement? e, string name) => e?.Get(name).Str();

    public static int? Int(this JsonElement? e) =>
        e is { ValueKind: JsonValueKind.String } v
        && int.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
            ? i
            : null;

    public static DateOnly? Date(this JsonElement? e) =>
        e is { ValueKind: JsonValueKind.String } v
        && DateOnly.TryParseExact(v.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;

    /// <summary>Aplatit null / objet / tableau ; descend dans "votant" s'il existe.</summary>
    public static IEnumerable<JsonElement> Items(this JsonElement? e)
    {
        if (e is null) yield break;
        var v = e.Value;
        switch (v.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var child in v.EnumerateArray())
                    foreach (var x in ((JsonElement?)child).Items())
                        yield return x;
                break;
            case JsonValueKind.Object:
                if (v.Get("votant") is { } votant)
                {
                    foreach (var x in ((JsonElement?)votant).Items())
                        yield return x;
                }
                else
                {
                    yield return v;
                }
                break;
        }
    }
}
