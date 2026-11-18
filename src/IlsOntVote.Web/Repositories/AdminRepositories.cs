using Dapper;
using IlsOntVote.Web.Data;
using IlsOntVote.Web.Models;

namespace IlsOntVote.Web.Repositories;

// ----------------------------------------------------------------------------- statistiques

/// <summary>Pas de l'axe du temps des statistiques (jour ou mois).</summary>
/// <summary>Fréquentation d'une journée pour un pays : vues, visiteurs uniques, pages les plus vues.</summary>
public sealed class JourPays
{
    public int Vues { get; set; }
    public int Visiteurs { get; set; }
    public IReadOnlyList<CompteVues> TopPages { get; set; } = [];
}

public enum Granularite { Jour, Mois }
/// <summary>Source des visites : lecteurs (non-robots), robots déclarés, ou tous.</summary>
public enum SourceVues { Humains, Bots, Tous }

/// <summary>Vues et visiteurs uniques pour une clé (une page).</summary>
public sealed record CompteVues(string Cle, int Vues, int Visiteurs);
/// <summary>Un point de la courbe de fréquentation (début de période, vues, visiteurs uniques).</summary>
public sealed record PointSerie(DateTime Debut, int Vues, int Visiteurs);

public sealed class ApercuStats
{
    public int TotalVues { get; set; }
    public int VisiteursUniques { get; set; }
    public IReadOnlyList<CompteVues> TopPages { get; set; } = [];
    public IReadOnlyList<PointSerie> Serie { get; set; } = [];
    /// <summary>Vues par pays (code ISO, « ? » = inconnu).</summary>
    public IReadOnlyList<CompteVues> Pays { get; set; } = [];
}

/// <summary>Enregistrement et lecture des pages vues. Rien d'identifiant n'est stocké : ni IP, ni User-Agent en clair.</summary>
public interface IPageViewRepository
{
    Task RecordAsync(string path, string ipHash, bool isBot, string? pays);
    Task<ApercuStats> GetStatsAsync(DateTime depuis, DateTime? jusqua, Granularite granularite, SourceVues source);
    /// <summary>Pays (code ISO, « ? » = inconnu) ayant des visites de lecteurs depuis la date donnée, avec leurs vues : sert à remplir la liste de choix.</summary>
    Task<IReadOnlyList<CompteVues>> GetPaysDisponiblesAsync(DateTime depuis);
    /// <summary>Visites de lecteurs (robots exclus) d'un pays sur [debut, fin[ ; <paramref name="pays"/> vide = tous les pays, « ? » = pays inconnu.</summary>
    Task<JourPays> GetJourPaysAsync(DateTime debut, DateTime fin, string? pays);
}

public sealed class PageViewRepository(DapperContext db) : IPageViewRepository
{
    public async Task RecordAsync(string path, string ipHash, bool isBot, string? pays)
    {
        using var conn = db.CreateConnection();
        await conn.ExecuteAsync(
            "INSERT INTO page_views (path, ip_hash, is_bot, country_code) VALUES (@path, @ipHash, @isBot, @pays)",
            new { path, ipHash, isBot, pays });
    }

    public async Task<IReadOnlyList<CompteVues>> GetPaysDisponiblesAsync(DateTime depuis)
    {
        using var conn = db.CreateConnection();
        return (await conn.QueryAsync<CompteVues>(
            """
            SELECT COALESCE(country_code, '?') AS cle, count(*)::int AS vues, count(DISTINCT ip_hash)::int AS visiteurs
              FROM page_views WHERE visited_at >= @depuis AND NOT is_bot
             GROUP BY 1 ORDER BY count(*) DESC
            """, new { depuis })).ToList();
    }

    public async Task<JourPays> GetJourPaysAsync(DateTime debut, DateTime fin, string? pays)
    {
        using var conn = db.CreateConnection();
        pays = string.IsNullOrWhiteSpace(pays) ? null : pays.Trim().ToUpperInvariant();
        var p = new { debut, fin, pays };
        // Filtre pays : NULL = tous ; « ? » = visites sans pays connu (country_code NULL) ; sinon le code ISO.
        const string filtre =
            """
            visited_at >= @debut AND visited_at < @fin AND NOT is_bot
            AND (@pays::text IS NULL OR (@pays = '?' AND country_code IS NULL) OR country_code = @pays)
            """;
        var total = await conn.QuerySingleAsync<(int Vues, int Visiteurs)>(
            $"SELECT count(*)::int, count(DISTINCT ip_hash)::int FROM page_views WHERE {filtre}", p);
        var top = (await conn.QueryAsync<CompteVues>(
            $"""
             SELECT path AS cle, count(*)::int AS vues, count(DISTINCT ip_hash)::int AS visiteurs
               FROM page_views WHERE {filtre}
              GROUP BY path ORDER BY count(*) DESC, path LIMIT 8
             """, p)).ToList();
        return new JourPays { Vues = total.Vues, Visiteurs = total.Visiteurs, TopPages = top };
    }

    public async Task<ApercuStats> GetStatsAsync(DateTime depuis, DateTime? jusqua, Granularite granularite, SourceVues source)
    {
        using var conn = db.CreateConnection();

        // NULL = pas de filtre sur la source. Passé tel quel à Postgres : « @isBot IS NULL OR is_bot = @isBot »
        // évite de dupliquer chaque requête par cas.
        bool? isBot = source switch { SourceVues.Humains => false, SourceVues.Bots => true, _ => null };
        var p = new { depuis, jusqua, isBot };

        const string filtre =
            """
            visited_at >= @depuis AND (@jusqua::timestamp IS NULL OR visited_at < @jusqua)
            AND (@isBot::boolean IS NULL OR is_bot = @isBot)
            """;

        var total = await conn.QuerySingleAsync<(int Vues, int Visiteurs)>(
            $"SELECT count(*)::int, count(DISTINCT ip_hash)::int FROM page_views WHERE {filtre}", p);

        var top = (await conn.QueryAsync<CompteVues>(
            $"""
             SELECT path AS cle, count(*)::int AS vues, count(DISTINCT ip_hash)::int AS visiteurs
               FROM page_views WHERE {filtre}
              GROUP BY path ORDER BY count(*) DESC LIMIT 50
             """, p)).ToList();

        // L'unité vient d'un enum interne, jamais d'une saisie : l'interpolation est sûre.
        var unite = granularite == Granularite.Mois ? "month" : "day";
        var serie = (await conn.QueryAsync<PointSerie>(
            $"""
             SELECT date_trunc('{unite}', visited_at) AS debut, count(*)::int AS vues, count(DISTINCT ip_hash)::int AS visiteurs
               FROM page_views WHERE {filtre}
              GROUP BY 1 ORDER BY 1
             """, p)).ToList();

        // Pays : « ? » regroupe les visites sans pays connu (base GeoLite2 absente ou adresse introuvable).
        var pays = (await conn.QueryAsync<CompteVues>(
            $"""
             SELECT COALESCE(country_code, '?') AS cle, count(*)::int AS vues, count(DISTINCT ip_hash)::int AS visiteurs
               FROM page_views WHERE {filtre}
              GROUP BY 1 ORDER BY count(*) DESC
             """, p)).ToList();

        return new ApercuStats { TotalVues = total.Vues, VisiteursUniques = total.Visiteurs, TopPages = top, Serie = serie, Pays = pays };
    }
}

// ----------------------------------------------------------------------------- administration

/// <summary>Une ligne du journal des imports (table import_runs).</summary>
public sealed class ExecutionImport
{
    public long Id { get; set; }
    public string Source { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public bool? Ok { get; set; }
    public int? Scrutins { get; set; }
    public int? Votes { get; set; }
    public string? Message { get; set; }

    public TimeSpan? Duree => FinishedAt is null ? null : FinishedAt - StartedAt;
}

/// <summary>Un scrutin mis « à la une » par l'éditeur (rang et titre court facultatifs).</summary>
public sealed class ScrutinUne
{
    public string Id { get; set; } = "";
    public string Chambre { get; set; } = "";
    public DateTime DateScrutin { get; set; }
    public string Titre { get; set; } = "";
    public int? FeaturedRank { get; set; }
    public string? FeaturedTitle { get; set; }
}

/// <summary>Volumes affichés sur le tableau de bord.</summary>
public sealed class ChiffresAdmin
{
    public int Parlementaires { get; set; }
    public int Scrutins { get; set; }
    public int Votes { get; set; }
    public int Unes { get; set; }
    public DateTime? DernierScrutinAn { get; set; }
    public DateTime? DernierScrutinSenat { get; set; }
}

/// <summary>Accès aux données de l'administration : chiffres clés, journal des imports, textes à la une.</summary>
public interface IAdminRepository
{
    Task<ChiffresAdmin> GetChiffresAsync();
    Task<IReadOnlyList<ExecutionImport>> GetImportsAsync(int max);
    Task<IReadOnlyList<ScrutinUne>> GetUnesAsync();
    Task DefinirUneAsync(string scrutinId, int? rang, string? titreCourt);
    Task RetirerUneAsync(string scrutinId);
}

public sealed class AdminRepository(DapperContext db) : IAdminRepository
{
    public async Task<ChiffresAdmin> GetChiffresAsync()
    {
        using var conn = db.CreateConnection();
        return await conn.QuerySingleAsync<ChiffresAdmin>(
            """
            SELECT (SELECT count(*) FROM parlementaires)::int AS parlementaires,
                   (SELECT count(*) FROM scrutins)::int AS scrutins,
                   -- Estimation du planificateur (instantanée) plutôt qu'un count(*) sur des millions de lignes ;
                   -- GREATEST(…, 0) : Postgres renvoie -1 tant que la table n'a pas été analysée.
                   GREATEST((SELECT reltuples::bigint FROM pg_class WHERE relname = 'votes'), 0)::int AS votes,
                   (SELECT count(*) FROM scrutins WHERE featured)::int AS unes,
                   (SELECT max(date_scrutin) FROM scrutins WHERE chambre = 'AN') AS dernier_scrutin_an,
                   (SELECT max(date_scrutin) FROM scrutins WHERE chambre = 'SENAT') AS dernier_scrutin_senat
            """);
    }

    public async Task<IReadOnlyList<ExecutionImport>> GetImportsAsync(int max)
    {
        using var conn = db.CreateConnection();
        return (await conn.QueryAsync<ExecutionImport>(
            "SELECT id, source, started_at, finished_at, ok, scrutins, votes, message FROM import_runs ORDER BY id DESC LIMIT @max",
            new { max })).ToList();
    }

    public async Task<IReadOnlyList<ScrutinUne>> GetUnesAsync()
    {
        using var conn = db.CreateConnection();
        return (await conn.QueryAsync<ScrutinUne>(
            """
            SELECT id, chambre, date_scrutin, titre, featured_rank, featured_title
              FROM scrutins WHERE featured
             ORDER BY chambre, featured_rank NULLS LAST, date_scrutin DESC
            """)).ToList();
    }

    public async Task DefinirUneAsync(string scrutinId, int? rang, string? titreCourt)
    {
        using var conn = db.CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE scrutins SET featured = true, featured_rank = @rang, featured_title = NULLIF(trim(@titreCourt), '') WHERE id = @scrutinId",
            new { scrutinId, rang, titreCourt });
    }

    public async Task RetirerUneAsync(string scrutinId)
    {
        using var conn = db.CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE scrutins SET featured = false, featured_rank = NULL, featured_title = NULL WHERE id = @scrutinId",
            new { scrutinId });
    }
}
