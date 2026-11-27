using System.Text.Json.Nodes;
using Dapper;
using IlsOntVote.Web.Data;

namespace IlsOntVote.Web.Mcp;

/// <summary>Un lot de données et ses conditions de réutilisation (table <c>licences</c>, migration 078).</summary>
public sealed class Licence
{
    public string Code { get; set; } = "";
    public string Producteur { get; set; } = "";
    public string[] Types { get; set; } = [];
    public string[] Chambres { get; set; } = [];
    public string? NomLicence { get; set; }
    public string? UrlConditions { get; set; }
    public string Attribution { get; set; } = "";
    public string UsageCommercial { get; set; } = "unknown";
    public string PartageIdentique { get; set; } = "unknown";
    public string Statut { get; set; } = "unverified";
    public DateTime? VerifieLe { get; set; }
    public string? Remarque { get; set; }
    /// <summary>Fin du dernier import réussi des tâches du lot (UTC) ; null si inconnue.</summary>
    public DateTime? MisAJour { get; set; }

    /// <summary>Forme publiée par l'API (anglais).</summary>
    public object VersApi() => new
    {
        Producer = Producteur, DocumentTypes = Types, Chambers = Chambres, License = NomLicence, TermsUrl = UrlConditions, Attribution,
        CommercialUse = UsageCommercial, ShareAlike = PartageIdentique, Status = Statut, VerifiedOn = Reponse.Jour(VerifieLe), Note = Remarque,
        LastSuccessfulImport = Reponse.Jour(MisAJour),
    };
}

/// <summary>
/// Joint à chaque réponse les conditions des SEULS lots présents dans les données renvoyées. Copie en mémoire relue toutes les 10 minutes.
/// </summary>
public sealed class Sources(DapperContext db)
{
    private IReadOnlyList<Licence> _liste = [];
    private DateTime _charge = DateTime.MinValue;

    /// <summary>Types dont les documents ne portent pas toujours de chambre : tous leurs lots possibles sont cités.</summary>
    private static readonly Dictionary<string, string[]> ChambresImplicites = new()
    {
        ["law"] = ["AN", "SENAT"], ["bill"] = ["COMMONS", "LORDS"], ["popular_vote"] = ["CH"],
    };

    public async Task<IReadOnlyList<Licence>> ListerAsync()
    {
        if (DateTime.UtcNow - _charge < TimeSpan.FromMinutes(10)) return _liste;
        using var cx = db.CreateConnection();
        // Table absente (migration 078 non appliquée) : liste vide, signalée au client par « sources_unavailable ».
        if (!await cx.ExecuteScalarAsync<bool>("SELECT to_regclass('public.licences') IS NOT NULL")) return _liste;
        // Dernière mise à jour = fin la plus récente d'une exécution réussie parmi les tâches du lot.
        _liste = (await cx.QueryAsync<Licence>(
            """
            SELECT l.code, l.producteur, l.types, l.chambres, l.licence AS nom_licence, l.url_conditions, l.attribution,
                   l.usage_commercial, l.partage_identique, l.statut, l.verifie_le, l.remarque,
                   (SELECT max(e.fin) FROM import_executions e WHERE e.tache = ANY(l.taches) AND e.ok) AS mis_a_jour
              FROM licences l ORDER BY l.ordre, l.code
            """)).ToList();
        _charge = DateTime.UtcNow;
        return _liste;
    }

    /// <summary>
    /// Ajoute le bloc « sources » à une réponse. Les chambres sont relevées dans la réponse elle-même (champs « chamber » et « platform ») ;
    /// aucune donnée renvoyée = aucune source. <paramref name="type"/> restreint aux lots de ce type de document (AN et SENAT désignent
    /// aussi des plateformes de pétitions, sous d'autres conditions).
    /// </summary>
    public async Task<string> JoindreAsync(string type, object reponse)
    {
        var json = JsonNode.Parse(Reponse.Json(reponse))!.AsObject();
        var chambres = new HashSet<string>();
        Relever(json, chambres);
        var vide = json["results"] is JsonArray { Count: 0 };
        if (!vide && ChambresImplicites.TryGetValue(type, out var implicites)) chambres.UnionWith(implicites);

        var liste = await ListerAsync();
        json["sources"] = JsonNode.Parse(Reponse.Json(liste.Where(l => l.Types.Contains(type) && l.Chambres.Any(chambres.Contains)).Select(l => l.VersApi())));
        if (liste.Count == 0) json["sources_unavailable"] = "Licence information is unavailable: do not assume the data is free to reuse.";
        return json.ToJsonString(Reponse.Options);
    }

    private static void Relever(JsonNode? noeud, HashSet<string> chambres)
    {
        if (noeud is JsonObject o)
            foreach (var (cle, valeur) in o)
            {
                if (cle is "chamber" or "platform" && valeur is JsonValue v && v.TryGetValue<string>(out var code)) chambres.Add(code);
                else Relever(valeur, chambres);
            }
        else if (noeud is JsonArray a)
            foreach (var element in a) Relever(element, chambres);
    }
}
