using Dapper;
using IlsOntVote.Web.Data;
using IlsOntVote.Web.Models;

namespace IlsOntVote.Web.Repositories;

/// <summary>Une pétition déposée sur petitions.senat.fr, telle que publiée (statut et nombre de soutiens non recalculés).</summary>
public sealed class Petition
{
    public string Id { get; set; } = "";
    public string Titre { get; set; } = "";
    public DateTime? DatePublication { get; set; }
    public int NbVotes { get; set; }
    public string Statut { get; set; } = "";
    public DateTime? DateLimiteVote { get; set; }
    public string Type { get; set; } = "";
    public string Url { get; set; } = "";
    /// <summary>Nombre de constats (baisse, disparition, réapparition) enregistrés pour cette pétition.</summary>
    public int NbAnomalies { get; set; }
    /// <summary>Premier relevé où la pétition manque au fichier source ; NULL si elle est présente au dernier relevé.</summary>
    public DateTime? AbsenteDepuis { get; set; }
}

/// <summary>Un relevé de l'historique : nombre de soutiens et statut à une date.</summary>
public sealed class RelevePetition
{
    public DateTime ReleveLe { get; set; }
    public int NbVotes { get; set; }
    public string Statut { get; set; } = "";
}

/// <summary>
/// Un constat entre deux relevés. Types : « baisse » (moins de soutiens qu'au relevé précédent), « disparition » (absente du fichier alors qu'elle
/// y figurait), « reapparition ». Aucune cause n'est connue : le fichier source n'en donne pas.
/// </summary>
public sealed class AnomaliePetition
{
    public string PetitionId { get; set; } = "";
    public string Titre { get; set; } = "";
    public string Type { get; set; } = "";
    public DateTime DetecteLe { get; set; }
    public DateTime ReleveAvant { get; set; }
    public int ValeurAvant { get; set; }
    public int? ValeurApres { get; set; }
    /// <summary>Soutiens perdus (baisse seulement).</summary>
    public int? Baisse { get; set; }
    /// <summary>Part des soutiens perdus, en % du nombre précédent (baisse seulement).</summary>
    public double? BaissePct { get; set; }
    public string Statut { get; set; } = "";
}

/// <summary>Critères de recherche d'anomalies (tous facultatifs, cumulables).</summary>
public sealed record CriteresAnomalies(string? Type, DateTime? Du, DateTime? Au, int? BaisseMin, double? BaissePctMin, int? AvantMin, string? Recherche);

/// <summary>Effectif d'un statut ou d'un type de pétition.</summary>
public sealed class EffectifPetition
{
    public string Libelle { get; set; } = "";
    public int Nombre { get; set; }
}

public interface IPetitionRepository
{
    Task<Resultats<Petition>> ListerAsync(string? recherche, string? statut, string? type, string tri, bool decroissant, int page, int parPage);
    Task<IReadOnlyList<EffectifPetition>> CompterParStatutAsync();
    Task<IReadOnlyList<EffectifPetition>> CompterParTypeAsync();
    Task<Petition?> GetAsync(string id);
    Task<IReadOnlyList<RelevePetition>> GetHistoriqueAsync(string id);
    Task<IReadOnlyList<AnomaliePetition>> GetAnomaliesAsync(string id);
    Task<Resultats<AnomaliePetition>> ListerAnomaliesAsync(CriteresAnomalies c, string tri, bool decroissant, int page, int parPage);
    Task<IReadOnlyList<EffectifPetition>> CompterAnomaliesParTypeAsync();
}

/// <summary>
/// Pétitions du Sénat (table <c>petitions</c>, alimentée par l'import « petitions »). Le tri vient d'une liste blanche : jamais du SQL construit
/// depuis l'adresse. La table peut ne pas exister tant que la migration 023 n'est pas appliquée : on affiche alors une liste vide.
/// </summary>
public sealed class PetitionRepository(DapperContext db) : IPetitionRepository
{
    /// <summary>Clés de tri autorisées -> expression SQL (liste blanche).</summary>
    public static readonly IReadOnlyDictionary<string, string> Tris = new Dictionary<string, string>
    {
        ["date"] = "date_publication",
        ["titre"] = "recherche_norm(titre)",
        ["soutiens"] = "nb_votes",
        ["statut"] = "statut",
        ["type"] = "type",
    };

    public async Task<Resultats<Petition>> ListerAsync(string? recherche, string? statut, string? type, string tri, bool decroissant, int page, int parPage)
    {
        using var cx = db.CreateConnection();
        if (!await TableExisteAsync(cx)) return new Resultats<Petition>([], 0, 1, parPage);
        var mots = RechercheSql.Mots(recherche);
        // Filtre sur le titre sans accents ni tirets (comme les autres recherches du site) ; statut et type : égalité exacte avec le libellé publié.
        const string filtre =
            """
             WHERE (cardinality(@mots) = 0 OR recherche_norm(titre) ILIKE ALL (@mots))
               AND (@statut IS NULL OR statut = @statut)
               AND (@type IS NULL OR type = @type)
            """;
        var p = new { mots, statut = Vide(statut), type = Vide(type), limit = parPage, offset = (page - 1) * parPage };
        var total = await cx.ExecuteScalarAsync<int>($"SELECT count(*) FROM petitions {filtre}", p);
        var ordre = Tris.GetValueOrDefault(tri, Tris["date"]);
        var lignes = (await cx.QueryAsync<Petition>(
            $"""
             SELECT id, titre, date_publication AS DatePublication, nb_votes AS NbVotes, statut, date_limite_vote AS DateLimiteVote, type, url,
                    absente_depuis AS AbsenteDepuis, (SELECT count(*) FROM petitions_anomalies a WHERE a.petition_id = petitions.id)::int AS NbAnomalies
               FROM petitions {filtre}
              ORDER BY {ordre} {(decroissant ? "DESC" : "ASC")} NULLS LAST, id
              {(parPage > 0 ? "LIMIT @limit OFFSET @offset" : "")}
             """, p)).ToList();
        return new Resultats<Petition>(lignes, total, page, parPage == 0 ? Math.Max(1, total) : parPage);
    }

    public Task<IReadOnlyList<EffectifPetition>> CompterParStatutAsync() => CompterAsync("statut");
    public Task<IReadOnlyList<EffectifPetition>> CompterParTypeAsync() => CompterAsync("type");

    /// <summary>Effectifs par colonne (nom en dur, jamais saisi) : statut ou type.</summary>
    private async Task<IReadOnlyList<EffectifPetition>> CompterAsync(string colonne)
    {
        using var cx = db.CreateConnection();
        if (!await TableExisteAsync(cx)) return [];
        return (await cx.QueryAsync<EffectifPetition>($"SELECT {colonne} AS Libelle, count(*)::int AS Nombre FROM petitions GROUP BY {colonne} ORDER BY 2 DESC")).ToList();
    }

    /// <summary>Clés de tri des anomalies (liste blanche) -> expression SQL.</summary>
    public static readonly IReadOnlyDictionary<string, string> TrisAnomalies = new Dictionary<string, string>
    {
        ["date"] = "a.detecte_le",
        ["titre"] = "recherche_norm(p.titre)",
        ["type"] = "a.type",
        ["avant"] = "a.valeur_avant",
        ["apres"] = "a.valeur_apres",
        ["baisse"] = "(a.valeur_avant - a.valeur_apres)",
        ["pct"] = "((a.valeur_avant - a.valeur_apres) * 100.0 / NULLIF(a.valeur_avant, 0))",
    };

    public async Task<Petition?> GetAsync(string id)
    {
        using var cx = db.CreateConnection();
        if (!await TableExisteAsync(cx)) return null;
        return await cx.QuerySingleOrDefaultAsync<Petition>(
            """
            SELECT id, titre, date_publication AS DatePublication, nb_votes AS NbVotes, statut, date_limite_vote AS DateLimiteVote, type, url,
                   absente_depuis AS AbsenteDepuis, (SELECT count(*) FROM petitions_anomalies a WHERE a.petition_id = petitions.id)::int AS NbAnomalies
              FROM petitions WHERE id = @id
            """, new { id });
    }

    public async Task<IReadOnlyList<RelevePetition>> GetHistoriqueAsync(string id)
    {
        using var cx = db.CreateConnection();
        if (!await TableExisteAsync(cx)) return [];
        return (await cx.QueryAsync<RelevePetition>(
            "SELECT releve_le AS ReleveLe, nb_votes AS NbVotes, statut FROM petitions_historique WHERE petition_id = @id ORDER BY releve_le", new { id })).ToList();
    }

    /// <summary>Colonnes communes de lecture d'une anomalie (jointure sur la pétition pour le titre et le statut courant).</summary>
    private const string SelectAnomalie =
        """
        SELECT a.petition_id AS PetitionId, p.titre, a.type, a.detecte_le AS DetecteLe, a.releve_precedent AS ReleveAvant,
               a.valeur_avant AS ValeurAvant, a.valeur_apres AS ValeurApres,
               (a.valeur_avant - a.valeur_apres) AS Baisse,
               ((a.valeur_avant - a.valeur_apres) * 100.0 / NULLIF(a.valeur_avant, 0))::float8 AS BaissePct, p.statut
        """;

    public async Task<IReadOnlyList<AnomaliePetition>> GetAnomaliesAsync(string id)
    {
        using var cx = db.CreateConnection();
        if (!await TableExisteAsync(cx)) return [];
        return (await cx.QueryAsync<AnomaliePetition>(
            $"{SelectAnomalie} FROM petitions_anomalies a JOIN petitions p ON p.id = a.petition_id WHERE a.petition_id = @id ORDER BY a.detecte_le, a.type", new { id })).ToList();
    }

    public async Task<Resultats<AnomaliePetition>> ListerAnomaliesAsync(CriteresAnomalies c, string tri, bool decroissant, int page, int parPage)
    {
        using var cx = db.CreateConnection();
        if (!await TableExisteAsync(cx)) return new Resultats<AnomaliePetition>([], 0, 1, parPage);
        // Critères cumulables, tous paramétrés. « Baisse » : valeur_avant - valeur_apres, donc inexistante pour une disparition (les critères
        // de baisse excluent alors ces lignes, ce qui est voulu). « AvantMin » : soutiens au relevé précédent (ex. « proche d'un seuil »).
        const string filtre =
            """
              FROM petitions_anomalies a JOIN petitions p ON p.id = a.petition_id
             WHERE (@type IS NULL OR a.type = @type)
               AND (@du::date IS NULL OR a.detecte_le >= @du::date)
               AND (@au::date IS NULL OR a.detecte_le <= @au::date)
               AND (@baisseMin::int IS NULL OR (a.valeur_avant - a.valeur_apres) >= @baisseMin::int)
               AND (@pctMin::float8 IS NULL OR ((a.valeur_avant - a.valeur_apres) * 100.0 / NULLIF(a.valeur_avant, 0)) >= @pctMin::float8)
               AND (@avantMin::int IS NULL OR a.valeur_avant >= @avantMin::int)
               AND (cardinality(@mots) = 0 OR recherche_norm(p.titre) ILIKE ALL (@mots))
            """;
        var p = new
        {
            type = string.IsNullOrWhiteSpace(c.Type) ? null : c.Type, du = c.Du, au = c.Au, baisseMin = c.BaisseMin, pctMin = c.BaissePctMin,
            avantMin = c.AvantMin, mots = RechercheSql.Mots(c.Recherche), limit = parPage, offset = (page - 1) * parPage,
        };
        var total = await cx.ExecuteScalarAsync<int>($"SELECT count(*) {filtre}", p);
        var ordre = TrisAnomalies.GetValueOrDefault(tri, TrisAnomalies["date"]);
        var lignes = (await cx.QueryAsync<AnomaliePetition>(
            $"{SelectAnomalie} {filtre} ORDER BY {ordre} {(decroissant ? "DESC" : "ASC")} NULLS LAST, a.id {(parPage > 0 ? "LIMIT @limit OFFSET @offset" : "")}", p)).ToList();
        return new Resultats<AnomaliePetition>(lignes, total, page, parPage == 0 ? Math.Max(1, total) : parPage);
    }

    public async Task<IReadOnlyList<EffectifPetition>> CompterAnomaliesParTypeAsync()
    {
        using var cx = db.CreateConnection();
        if (!await TableExisteAsync(cx)) return [];
        return (await cx.QueryAsync<EffectifPetition>("SELECT type AS Libelle, count(*)::int AS Nombre FROM petitions_anomalies GROUP BY type ORDER BY 2 DESC")).ToList();
    }

    /// <summary>Vrai si les tables des pétitions existent (migrations 023 et 024 appliquées) ; sinon les pages affichent une liste vide au lieu d'échouer.</summary>
    private static async Task<bool> TableExisteAsync(System.Data.IDbConnection cx) =>
        await cx.ExecuteScalarAsync<bool>("SELECT to_regclass('public.petitions_anomalies') IS NOT NULL AND to_regclass('public.petitions') IS NOT NULL");

    private static string? Vide(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
