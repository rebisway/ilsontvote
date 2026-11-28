using Dapper;
using IlsOntVote.Web.Data;

namespace IlsOntVote.Web.Mcp;

/// <summary>Dérogation enregistrée à la limite de débit. <c>Limite</c> null = aucune limite ; <c>FinLe</c> en UTC, null = jamais réglée.</summary>
public sealed class ReglageMcp
{
    public int? Limite { get; set; }
    public DateTime? FinLe { get; set; }
}

/// <summary>
/// Limite de débit du serveur MCP : <see cref="Normale"/>, sauf dérogation temporaire réglée dans /Admin/Mcp (table <c>mcp_reglage</c>).
/// Copie en mémoire relue toutes les 30 secondes : le limiteur est consulté à chaque requête. Toute erreur de lecture ramène à la limite normale.
/// </summary>
public sealed class LimiteMcp(DapperContext db, ILogger<LimiteMcp> journal)
{
    /// <summary>Requêtes par minute et par adresse IP : large pour un assistant, trop bas pour aspirer la base (les fichiers de données ouvertes servent à cela).</summary>
    public const int Normale = 60;
    /// <summary>Durée maximale d'une dérogation : au-delà, une limite levée « le temps d'un examen » serait oubliée.</summary>
    public const int JoursMax = 90;
    private static readonly TimeSpan Fraicheur = TimeSpan.FromSeconds(30);

    private ReglageMcp _reglage = new();
    private DateTime _charge = DateTime.MinValue;

    /// <summary>Réglage enregistré (même expiré).</summary>
    public ReglageMcp Lire()
    {
        if (DateTime.UtcNow - _charge < Fraicheur) return _reglage;
        try
        {
            using var cx = db.CreateConnection();
            _reglage = cx.QuerySingleOrDefault<ReglageMcp>("SELECT limite, fin_le FROM mcp_reglage WHERE id = 1") ?? new();
        }
        catch (Exception ex)
        {
            // Table absente (migration 086) ou base indisponible : on revient à la limite normale, jamais à « sans limite ».
            journal.LogWarning(ex, "Lecture de la limite MCP impossible : limite normale appliquée");
            _reglage = new();
        }
        _charge = DateTime.UtcNow;
        return _reglage;
    }

    /// <summary>Limite applicable maintenant ; null = aucune limite.</summary>
    public int? Courante() => Appliquer(Lire(), DateTime.UtcNow);

    /// <summary>Règle pure : la dérogation ne vaut que jusqu'à son échéance.</summary>
    public static int? Appliquer(ReglageMcp r, DateTime maintenantUtc) =>
        r.FinLe is { } fin && maintenantUtc < fin ? r.Limite : Normale;

    /// <summary>Enregistre une dérogation de <paramref name="jours"/> jours (limite null = aucune) ; effet immédiat.</summary>
    public async Task DerogerAsync(int? limite, int jours)
    {
        await EcrireAsync(limite, DateTime.SpecifyKind(DateTime.UtcNow.AddDays(Math.Clamp(jours, 1, JoursMax)), DateTimeKind.Unspecified));
    }

    /// <summary>Revient à la limite normale ; effet immédiat.</summary>
    public Task RetablirAsync() => EcrireAsync(null, null);

    private async Task EcrireAsync(int? limite, DateTime? finLe)
    {
        using var cx = db.CreateConnection();
        await cx.ExecuteAsync("UPDATE mcp_reglage SET limite = @limite, fin_le = @finLe WHERE id = 1", new { limite, finLe });
        _charge = DateTime.MinValue;
    }
}
