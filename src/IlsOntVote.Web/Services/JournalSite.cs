using System.Threading.Channels;
using Dapper;
using IlsOntVote.Web.Data;

namespace IlsOntVote.Web.Services;

/// <summary>Un message du journal du site.</summary>
public sealed class LigneJournal
{
    public long Id { get; set; }
    /// <summary>Date en UTC.</summary>
    public DateTime Le { get; set; }
    public short Niveau { get; set; }
    /// <summary>Catégorie .NET (classe qui a émis le message).</summary>
    public string Source { get; set; } = "";
    public string Message { get; set; } = "";
}

/// <summary>
/// Copie une partie des messages de l'application vers la table <c>journal_site</c> (/Admin/Journal), sans donner au site accès au journal du serveur.
/// Gardés : avertissements et erreurs, plus les informations des appels MCP. Les messages passent par une file bornée : une base lente ou en panne
/// ne ralentit jamais une page, et les messages en trop sont perdus (le journal du serveur reste la référence).
/// </summary>
public sealed class JournalSite : ILoggerProvider
{
    /// <summary>Messages en attente d'écriture au maximum.</summary>
    private const int FileMax = 1000;
    /// <summary>Longueur maximale d'un message (pile d'appels comprise).</summary>
    public const int LongueurMax = 4000;
    /// <summary>Catégorie dont les messages d'information sont aussi gardés (« MCP : outil … »).</summary>
    private const string SourceMcp = "IlsOntVote.Web.Mcp.McpOutils";

    private readonly Channel<LigneJournal> _file = Channel.CreateBounded<LigneJournal>(
        new BoundedChannelOptions(FileMax) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    public ChannelReader<LigneJournal> Messages => _file.Reader;

    /// <summary>Règle pure : ce message est-il gardé ? L'écriture du journal elle-même est exclue, sinon son propre échec bouclerait.</summary>
    public static bool Garder(string source, LogLevel niveau) =>
        niveau != LogLevel.None && source != typeof(EcritureJournalService).FullName
        && (niveau >= LogLevel.Warning || (source == SourceMcp && niveau >= LogLevel.Information));

    public ILogger CreateLogger(string categoryName) => new Journaliseur(this, categoryName);

    public void Dispose() => _file.Writer.TryComplete();

    private sealed class Journaliseur(JournalSite journal, string source) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => Garder(source, logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!Garder(source, logLevel)) return;
            var message = formatter(state, exception);
            if (exception is not null) message += Environment.NewLine + exception;
            // PostgreSQL refuse le caractère nul dans un texte.
            message = message.Replace("\0", "");
            journal._file.Writer.TryWrite(new LigneJournal
            {
                Le = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
                Niveau = (short)logLevel,
                Source = source.Length > 200 ? source[..200] : source,
                Message = message.Length > LongueurMax ? message[..LongueurMax] : message,
            });
        }
    }
}

/// <summary>Écrit en arrière-plan les messages de <see cref="JournalSite"/> et efface les plus anciens.</summary>
public sealed class EcritureJournalService(JournalSite journal, DapperContext db, ILogger<EcritureJournalService> log) : BackgroundService
{
    public const int JoursConservation = 14;

    protected override async Task ExecuteAsync(CancellationToken arret)
    {
        var purge = DateTime.MinValue;
        try
        {
            while (await journal.Messages.WaitToReadAsync(arret))
            {
                var lot = new List<LigneJournal>();
                while (lot.Count < 100 && journal.Messages.TryRead(out var l)) lot.Add(l);
                try
                {
                    using var cx = db.CreateConnection();
                    await cx.ExecuteAsync("INSERT INTO journal_site (le, niveau, source, message) VALUES (@Le, @Niveau, @Source, @Message)", lot);
                    // Purge au plus une fois par heure, au fil des écritures.
                    if (DateTime.UtcNow - purge > TimeSpan.FromHours(1))
                    {
                        await cx.ExecuteAsync("DELETE FROM journal_site WHERE le < (now() AT TIME ZONE 'utc') - make_interval(days => @jours)", new { jours = JoursConservation });
                        purge = DateTime.UtcNow;
                    }
                }
                catch (Exception ex)
                {
                    // Base indisponible ou migration 088 absente : le lot est perdu ; signalé dans le journal du serveur seulement (cf. Garder).
                    log.LogWarning(ex, "Écriture du journal du site impossible ({N} messages perdus)", lot.Count);
                    await Task.Delay(TimeSpan.FromMinutes(1), arret);
                }
            }
        }
        catch (OperationCanceledException) { /* arrêt du site */ }
    }
}
