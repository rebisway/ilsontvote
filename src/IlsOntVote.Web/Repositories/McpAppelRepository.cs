using Dapper;
using IlsOntVote.Web.Data;

namespace IlsOntVote.Web.Repositories;

/// <summary>Appels et clients distincts d'un outil MCP sur une période.</summary>
public sealed class UsageOutil
{
    public string Outil { get; set; } = "";
    public int Appels { get; set; }
    public int Clients { get; set; }
}

/// <summary>Usage du serveur MCP sur une période : total et détail par outil.</summary>
public sealed record UsageMcp(int Appels, int Clients, IReadOnlyList<UsageOutil> Outils);

/// <summary>Enregistrement et lecture des appels MCP (table <c>mcp_appels</c>, migration 077). Ni paramètres ni IP : même empreinte que les pages vues.</summary>
public interface IMcpAppelRepository
{
    Task EnregistrerAsync(string outil, string ipHash);
    /// <summary>Usage sur [depuis, jusqua[ (UTC) ; null si la table n'existe pas encore.</summary>
    Task<UsageMcp?> LireAsync(DateTime depuis, DateTime? jusqua);
}

public sealed class McpAppelRepository(DapperContext db) : IMcpAppelRepository
{
    public async Task EnregistrerAsync(string outil, string ipHash)
    {
        using var cx = db.CreateConnection();
        await cx.ExecuteAsync("INSERT INTO mcp_appels (outil, ip_hash) VALUES (@outil, @ipHash)", new { outil, ipHash });
    }

    public async Task<UsageMcp?> LireAsync(DateTime depuis, DateTime? jusqua)
    {
        using var cx = db.CreateConnection();
        if (!await cx.ExecuteScalarAsync<bool>("SELECT to_regclass('public.mcp_appels') IS NOT NULL")) return null;
        // Borne haute ajoutée seulement si elle existe ; les dates de la base sont en UTC sans fuseau.
        var filtre = "appele_le >= @depuis" + (jusqua is null ? "" : " AND appele_le < @jusqua");
        var p = new { depuis, jusqua };
        var (appels, clients) = await cx.QuerySingleAsync<(int, int)>($"SELECT count(*)::int, count(DISTINCT ip_hash)::int FROM mcp_appels WHERE {filtre}", p);
        var outils = (await cx.QueryAsync<UsageOutil>(
            $"SELECT outil, count(*)::int AS appels, count(DISTINCT ip_hash)::int AS clients FROM mcp_appels WHERE {filtre} GROUP BY outil ORDER BY count(*) DESC, outil", p)).ToList();
        return new UsageMcp(appels, clients, outils);
    }
}
