using Dapper;
using IlsOntVote.Web.Data;

namespace IlsOntVote.Web.Services;

/// <summary>
/// Efface chaque jour les visites de plus de <see cref="JoursConservation"/> jours (table <c>page_views</c>) : on ne garde pas des
/// données de fréquentation plus longtemps que nécessaire (durée annoncée dans les mentions légales : à changer ensemble).
/// Premier passage 1 minute après le démarrage, puis toutes les 24 h. Une erreur est journalisée et réessayée le lendemain.
/// </summary>
public sealed class PurgeVuesService(DapperContext db, ILogger<PurgeVuesService> log) : BackgroundService
{
    public const int JoursConservation = 400;

    protected override async Task ExecuteAsync(CancellationToken arret)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(1), arret); } catch (OperationCanceledException) { return; }
        while (!arret.IsCancellationRequested)
        {
            try
            {
                using var cx = db.CreateConnection();
                // `visited_at` est en UTC sans fuseau : comparaison avec l'heure UTC du serveur de base.
                var n = await cx.ExecuteAsync(
                    "DELETE FROM page_views WHERE visited_at < (now() AT TIME ZONE 'utc') - make_interval(days => @jours)",
                    new { jours = JoursConservation });
                if (n > 0) log.LogInformation("Purge des visites : {N} lignes de plus de {J} jours supprimées.", n, JoursConservation);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "Échec de la purge des anciennes visites (nouvel essai demain).");
            }
            try { await Task.Delay(TimeSpan.FromHours(24), arret); } catch (OperationCanceledException) { return; }
        }
    }
}
