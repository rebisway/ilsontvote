using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using IlsOntVote.Web.Services;
using ModelContextProtocol;

namespace IlsOntVote.Web.Mcp;

/// <summary>Filtres communs d'une recherche. Un type de document ignore ceux qui ne le concernent pas et les annonce dans <see cref="ITypeDocument.Filtres"/>.</summary>
public sealed record Recherche(string? Texte, string? Pays, string? Chambre, string? Du, string? Au, string? Theme, bool SurEnsemble, int Page, int Limite, bool ParSignatures = false)
{
    public int Decalage => (Page - 1) * Limite;
}

/// <summary>Une page de résultats d'une recherche.</summary>
public sealed record PageDocuments(int Total, IReadOnlyList<object> Resultats);

/// <summary>
/// Un type de document exposé par le serveur MCP (vote parlementaire, votation populaire, loi, pétition…). Ajouter un type = une classe
/// qui implémente cette interface et une ligne dans <see cref="McpServeur"/> : les outils ne changent pas.
/// </summary>
public interface ITypeDocument
{
    /// <summary>Code stable, utilisé par les clients dans le paramètre « type ».</summary>
    string Code { get; }
    string Nom { get; }
    /// <summary>Ce que contient ce type, et surtout ce qu'il NE contient PAS (limites à répéter à l'utilisateur).</summary>
    string Description { get; }
    /// <summary>Filtres de la recherche que ce type prend en compte.</summary>
    string[] Filtres { get; }
    /// <summary>Codes des pays publiés pour lesquels ce type existe ; vide = type indisponible (rien d'un pays non publié n'est exposé).</summary>
    IReadOnlyList<string> Pays(Perimetre perimetre);
    Task<PageDocuments> RechercherAsync(Recherche r, Perimetre perimetre);
    /// <summary>Fiche complète d'un document ; null s'il n'existe pas ou n'est pas publié.</summary>
    Task<object?> ConsulterAsync(string id, Perimetre perimetre);
}

/// <summary>
/// Ce que le serveur MCP a le droit de montrer : mêmes règles que le site (<see cref="PaysSite.ChambresVisibles"/>), réunies pour la France,
/// l'Union européenne et chaque pays ACTIF. Garde-fou : toute requête d'un type de document doit se limiter à ces chambres et à ces pays.
/// </summary>
public sealed class Perimetre
{
    public const string Site = "https://www.ilsontvote.org";
    private readonly Dictionary<string, string> _prefixes;
    /// <summary>Chambre visible -> pays du site qui la publie (« UE » est rattachée à la France, à la racine).</summary>
    public IReadOnlyDictionary<string, string> Chambres { get; }

    private Perimetre(Dictionary<string, string> prefixes, Dictionary<string, string> chambres) => (_prefixes, Chambres) = (prefixes, chambres);

    public static async Task<Perimetre> ChargerAsync(PaysSite site)
    {
        await site.ChargerAsync();
        var prefixes = new Dictionary<string, string> { ["FR"] = "" };
        var chambres = site.ChambresVisibles(null).ToDictionary(c => c, c => c == "UE" ? "EU" : "FR");
        foreach (var p in site.Actifs.Where(p => p.Code is not ("FR" or "EU")))
        {
            prefixes[p.Code] = "/" + p.Prefixe;
            // TryAdd : le Parlement européen, visible aussi sous /it et /ie, reste rattaché à la racine.
            foreach (var c in site.ChambresVisibles(p.Code)) chambres.TryAdd(c, p.Code);
        }
        return new Perimetre(prefixes, chambres);
    }

    public IReadOnlyList<string> Pays => [.. _prefixes.Keys];
    public bool Publie(string pays) => _prefixes.ContainsKey(pays);

    /// <summary>Chambres visibles restreintes par les filtres ; un pays ou une chambre inconnus (ou non publiés) sont une erreur dite au client, jamais un filtre ignoré.</summary>
    public string[] Filtrer(string? pays, string? chambre)
    {
        var p = pays?.Trim().ToUpperInvariant() is { Length: > 0 } x ? x : null;
        var c = chambre?.Trim().ToUpperInvariant() is { Length: > 0 } y ? y : null;
        if (p is not null && p != "EU" && !Publie(p)) throw new McpException($"Country not published: {p}. See the list_types tool.");
        if (c is not null && !Chambres.ContainsKey(c)) throw new McpException($"Chamber not published: {c}. See the list_types tool.");
        return [.. Chambres.Where(k => (p is null || k.Value == p) && (c is null || k.Key == c)).Select(k => k.Key)];
    }

    /// <summary>Adresse publique d'une page, avec le préfixe du pays qui la publie.</summary>
    public string Url(string pays, string chemin) => Site + _prefixes.GetValueOrDefault(pays, "") + chemin;
}

/// <summary>Mise en forme commune des réponses.</summary>
public static class Reponse
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,   // une donnée manquante reste visible (null), jamais omise
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // accents lisibles ; la réponse n'est jamais injectée dans du HTML
    };

    /// <summary>Résultat d'un vote tel que stocké (« adopté », « rejetée »…) en anglais ; une valeur inconnue reste telle que publiée.</summary>
    public static string? Resultat(string? sort) =>
        sort is null ? null
        : sort.StartsWith("adopt", StringComparison.OrdinalIgnoreCase) ? "adopted"
        : sort.StartsWith("rejet", StringComparison.OrdinalIgnoreCase) ? "rejected"
        : sort;

    /// <summary>Positions de l'API (anglais) -> valeurs stockées en base.</summary>
    private static readonly Dictionary<string, string> Positions = new()
    {
        ["for"] = "pour", ["against"] = "contre", ["abstention"] = "abstention", ["non_voting"] = "non_votant", ["absent"] = "absent",
        ["delegated"] = "delegation", ["corrected"] = "mise_au_point",
    };

    /// <summary>Position stockée -> nom de l'API ; null reste null.</summary>
    public static string? Position(string? stockee) => stockee is null ? null : Positions.FirstOrDefault(x => x.Value == stockee).Key ?? stockee;

    /// <summary>Filtre de position reçu du client -> valeur stockée ; null si absent. <paramref name="attributs"/> ajoute « delegated » et « corrected ».</summary>
    public static string? PositionSaisie(string? saisie, bool attributs)
    {
        if (string.IsNullOrWhiteSpace(saisie)) return null;
        if (Positions.TryGetValue(saisie.Trim(), out var v) && (attributs || v is not ("delegation" or "mise_au_point"))) return v;
        throw new McpException("Expected position: for, against, abstention, non_voting" + (attributs ? ", absent, delegated or corrected." : " or absent."));
    }

    public static string Json(object valeur) => JsonSerializer.Serialize(valeur, Options);

    /// <summary>Date sans heure (« 2026-03-12 ») ; null si inconnue.</summary>
    public static string? Jour(DateTime? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Valide une date reçue du client ; renvoie la forme attendue par PostgreSQL (conversion « ::date »).</summary>
    public static string? DateSaisie(string? saisie) =>
        string.IsNullOrWhiteSpace(saisie) ? null
        : DateOnly.TryParseExact(saisie.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        : throw new McpException($"Invalid date: '{saisie}' (expected YYYY-MM-DD).");
}
