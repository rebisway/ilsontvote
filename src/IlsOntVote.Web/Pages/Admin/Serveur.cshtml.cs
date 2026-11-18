using System.Diagnostics;
using System.Runtime.InteropServices;
using Dapper;
using IlsOntVote.Web.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IlsOntVote.Web.Pages.Admin;

/// <summary>Taille d'une table (données + index + TOAST) de la base.</summary>
public sealed class TailleTable
{
    public string Nom { get; set; } = "";
    public long Octets { get; set; }
    public long Lignes { get; set; }
}

/// <summary>Volume (disque) relevé pour un dossier utile au site : où il est, sa taille, ce qui reste.</summary>
public sealed record Volume(string Role, string Chemin, string Racine, long Total, long Libre)
{
    public double PartLibre => Total == 0 ? 0 : (double)Libre / Total;
    /// <summary>Alerte : moins de 15 % ou moins de 5 Go libres (un import de l'Assemblée télécharge plusieurs centaines de Mo et en extrait davantage).</summary>
    public bool Alerte => PartLibre < 0.15 || Libre < 5L * 1024 * 1024 * 1024;
}

/// <summary>
/// État du serveur et de la base : taille de la base et des plus grosses tables, espace disque libre des dossiers du site, mémoire, charge, activité
/// de la connexion. Lecture seule. Les valeurs viennent du système au moment de l'affichage : aucune n'est stockée (pas d'historique).
/// </summary>
public class ServeurModel(DapperContext db, IConfiguration config) : PageModel
{
    public long TailleBase { get; private set; }
    public string Version { get; private set; } = "";
    public int Connexions { get; private set; }
    public IReadOnlyList<TailleTable> Tables { get; private set; } = [];
    public IReadOnlyList<Volume> Volumes { get; private set; } = [];
    public string Systeme { get; private set; } = "";
    public int Processeurs { get; private set; }
    public TimeSpan UptimeMachine { get; private set; }
    public TimeSpan UptimeSite { get; private set; }
    public long MemoireSite { get; private set; }
    public long? MemoireTotale { get; private set; }
    public long? MemoireDisponible { get; private set; }
    public string? Charge { get; private set; }
    public string? ErreurBase { get; private set; }

    public async Task OnGetAsync()
    {
        try
        {
            using var cx = db.CreateConnection();
            TailleBase = await cx.ExecuteScalarAsync<long>("SELECT pg_database_size(current_database())");
            Version = await cx.ExecuteScalarAsync<string>("SHOW server_version") ?? "";
            Connexions = await cx.ExecuteScalarAsync<int>("SELECT count(*) FROM pg_stat_activity WHERE datname = current_database()");
            // Tables ordinaires du schéma public (les index font partie de leur table : pg_total_relation_size = données + index + TOAST).
            // Lignes : estimation du planificateur (instantanée) plutôt qu'un count(*) sur des millions de lignes ; GREATEST(…, 0) : -1 avant le premier ANALYZE.
            Tables = (await cx.QueryAsync<TailleTable>(
                """
                SELECT c.relname AS nom, pg_total_relation_size(c.oid) AS octets, GREATEST(c.reltuples, 0)::bigint AS lignes
                  FROM pg_class c
                 WHERE c.relkind = 'r' AND c.relnamespace = 'public'::regnamespace
                 ORDER BY pg_total_relation_size(c.oid) DESC
                 LIMIT 15
                """)).ToList();
        }
        catch (Exception ex) { ErreurBase = ex.Message; }

        // Dossiers utiles : le site, les jeux téléchargés par les imports, les exports ouverts, les fichiers temporaires.
        var chemins = new List<(string Role, string? Chemin)>
        {
            ("Site (programme)", AppContext.BaseDirectory),
            ("Imports (jeux téléchargés)", Environment.GetEnvironmentVariable("ILSONVOTE_IMPORT_DIR") ?? "/var/lib/ilsontvote-import"),
            ("Données ouvertes (exports)", config["Export:Dir"] ?? Environment.GetEnvironmentVariable("ILSONVOTE_EXPORT_DIR") ?? "/var/lib/ilsontvote-export"),
            ("Fichiers temporaires", Path.GetTempPath()),
        };
        var volumes = new List<Volume>();
        foreach (var (role, chemin) in chemins)
        {
            if (string.IsNullOrEmpty(chemin) || !Directory.Exists(chemin)) continue;
            try
            {
                var d = new DriveInfo(Path.GetFullPath(chemin));
                if (d.IsReady && d.TotalSize > 0) volumes.Add(new Volume(role, chemin, d.Name, d.TotalSize, d.AvailableFreeSpace));
            }
            catch (Exception) { /* dossier illisible par le site : volume non affiché plutôt qu'une valeur inventée */ }
        }
        Volumes = volumes;

        Systeme = RuntimeInformation.OSDescription;
        Processeurs = Environment.ProcessorCount;
        UptimeMachine = TimeSpan.FromMilliseconds(Environment.TickCount64);
        UptimeSite = DateTime.Now - Process.GetCurrentProcess().StartTime;
        MemoireSite = Process.GetCurrentProcess().WorkingSet64;
        LireProc();
    }

    /// <summary>Mémoire et charge machine : /proc (Linux). Absent ailleurs (poste de développement Windows) : champs laissés vides, affichés « — ».</summary>
    private void LireProc()
    {
        try
        {
            if (System.IO.File.Exists("/proc/meminfo"))
            {
                foreach (var l in System.IO.File.ReadLines("/proc/meminfo"))
                {
                    // Lignes « MemTotal:  16314140 kB » (valeurs en kio).
                    var p = l.Split(':', 2);
                    if (p.Length != 2) continue;
                    var v = p[1].Replace("kB", "").Trim();
                    if (!long.TryParse(v, out var ko)) continue;
                    if (p[0] == "MemTotal") MemoireTotale = ko * 1024;
                    else if (p[0] == "MemAvailable") MemoireDisponible = ko * 1024;
                }
            }
            if (System.IO.File.Exists("/proc/loadavg"))
            {
                var t = System.IO.File.ReadAllText("/proc/loadavg").Split(' ');
                if (t.Length >= 3) Charge = $"{t[0]} · {t[1]} · {t[2]}";
            }
        }
        catch (IOException) { /* lecture impossible : valeurs absentes */ }
    }

    /// <summary>Taille lisible (Ko, Mo, Go, To) en base 1024, comme l'affichent les outils système.</summary>
    public static string Taille(long octets)
    {
        string[] u = ["o", "Ko", "Mo", "Go", "To"];
        double v = octets; var i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return (i == 0 ? v.ToString("0") : v.ToString(v >= 100 ? "0" : "0.0", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"))) + " " + u[i];
    }

    public static string Duree(TimeSpan d) => d.TotalDays >= 1 ? $"{(int)d.TotalDays} j {d.Hours} h" : $"{d.Hours} h {d.Minutes:00} min";
}
