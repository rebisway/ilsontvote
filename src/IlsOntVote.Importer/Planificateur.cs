using System.Diagnostics;
using System.Reflection;
using System.Text;
using Dapper;
using Npgsql;

namespace IlsOntVote.Importer;

/// <summary>
/// Planificateur d'imports : exécute les tâches demandées depuis /Admin et celles que le planning rend dues.
/// Lancé CHAQUE MINUTE par ilsontvote-planificateur.timer (commande « planificateur ») : un passage court quand il n'y a rien à faire,
/// long quand un import tourne (le timer ne relance pas un service oneshot déjà actif ; un verrou Postgres garantit en plus qu'un seul
/// planificateur travaille à la fois, y compris si on le lance à la main).
/// Chaque tâche est exécutée dans un PROCESSUS ENFANT (« dotnet Importer.dll &lt;commande&gt; ») : une tâche qui plante ou fuit en mémoire
/// n'emporte pas le planificateur, et sa sortie console est capturée pour le journal de /Admin.
/// Sécurité : la base ne contient que des CODES de tâche ; les lignes de commande sont dans <see cref="Commandes"/> (liste blanche).
/// </summary>
public sealed class Planificateur(string connectionString)
{
    /// <summary>Clé du verrou consultatif Postgres (valeur arbitraire, propre à ce programme).</summary>
    private const long CleVerrou = 7420026;

    /// <summary>Un créneau non déclenché depuis plus longtemps que cela est abandonné (machine éteinte une journée : pas d'import « fantôme » le lendemain).</summary>
    private static readonly TimeSpan FenetreRattrapage = TimeSpan.FromHours(12);

    /// <summary>Durée maximale d'une tâche : au-delà, le processus est tué et l'exécution marquée en échec (source qui ne répond plus).</summary>
    private static readonly TimeSpan DureeMax = TimeSpan.FromHours(3);

    /// <summary>Taille maximale de la sortie conservée par exécution (les imports sont bavards) : on garde la FIN, là où sont les erreurs.</summary>
    private const int SortieMax = 400_000;

    /// <summary>Intervalle d'écriture de la sortie en base pendant l'exécution (pour suivre un import en direct dans /Admin).</summary>
    private static readonly TimeSpan IntervalleSortie = TimeSpan.FromSeconds(5);

    /// <summary>Fuseau des heures du planning : celui des lecteurs (les débuts de séance de l'Assemblée sont à l'heure de Paris).</summary>
    private static readonly TimeZoneInfo Paris = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");

    /// <summary>Code de tâche -> arguments de l'Importer. Toute autre valeur est refusée (jamais de commande construite depuis la base).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Commandes = new Dictionary<string, string[]>
    {
        ["an"] = ["an"],
        ["an_historique"] = ["an", "--legislature", "all"],
        ["senat"] = ["senat"],
        ["textes"] = ["textes"],           // + --dosleg-sql si le dump du Sénat est déjà là (voir Arguments)
        ["dossiers"] = ["dossiers"],
        ["dossiers_historique"] = ["dossiers", "--legislature", "all"],
        ["ue"] = ["ue"],
        ["communes"] = ["communes"],
        ["amendements"] = ["amendements"],
        ["amendements_historique"] = ["amendements", "--legislature", "all"],
        ["petitions"] = ["petitions"],
        ["petitions_uk"] = ["petitions", "--source", "uk"],
        ["petitions_archives"] = ["petitions", "--archives"],
        ["traductions"] = ["traductions"],
        ["stats"] = ["stats"],
        ["export"] = ["export"],
    };

    /// <summary>Tables dont l'effectif est relevé avant et après chaque exécution (nom -> libellé affiché). Liste en dur : jamais construite depuis la base.</summary>
    public static readonly IReadOnlyDictionary<string, string> TablesSuivies = new Dictionary<string, string>
    {
        ["parlementaires"] = "Parlementaires",
        ["mandats"] = "Mandats",
        ["groupes"] = "Groupes",
        ["groupe_appartenances"] = "Appartenances à un groupe",
        ["scrutins"] = "Scrutins",
        ["votes"] = "Votes individuels",
        ["textes"] = "Textes",
        ["texte_etapes"] = "Étapes des textes",
        ["amendements_dossiers"] = "Amendements : dossiers (effectifs)",
        ["petitions"] = "Pétitions",
        ["petitions_historique"] = "Pétitions : relevés (historique)",
        ["petitions_anomalies"] = "Pétitions : constats",
        ["traductions_titres"] = "Titres traduits (fr vers en)",
        ["traductions_titres_fr"] = "Titres traduits (en vers fr)",
    };

    /// <summary>Lignes de sortie signalant une erreur (en plus de tout ce qui sort sur la sortie d'erreur) : mots-clés courants des messages .NET et des importeurs.</summary>
    private static readonly System.Text.RegularExpressions.Regex LigneErreur =
        new(@"erreur|exception|échec|echec|failed|impossible|introuvable|timeout|refus", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Effectif de chaque table suivie (count exact : les chiffres du bilan doivent être justes, quelques secondes suffisent).</summary>
    private static async Task<Dictionary<string, long>> EffectifsAsync(NpgsqlConnection cx)
    {
        var r = new Dictionary<string, long>();
        foreach (var t in TablesSuivies.Keys)
            // Nom issu de la liste en dur ci-dessus (pas d'entrée utilisateur) ; table absente (migration non appliquée) : ignorée.
            r[t] = await cx.ExecuteScalarAsync<long?>($"SELECT CASE WHEN to_regclass('public.{t}') IS NULL THEN NULL ELSE (SELECT count(*) FROM {t}) END") ?? -1;
        return r;
    }

    private sealed record Travail(string Tache, string Declencheur, string? DemandePar, int? PlanningId, long? DemandeId);

    private sealed class LignePlanning
    {
        public int Id { get; set; }
        public string Tache { get; set; } = "";
        public short[] Jours { get; set; } = [];
        public TimeSpan Heure { get; set; }
        public DateTime DernierLancement { get; set; }
        public int Ordre { get; set; }
    }

    /// <summary>Un passage : exécute tout ce qui est dû, puis recontrôle (une tâche longue peut avoir rendu d'autres créneaux dus). Code 0 sauf erreur interne.</summary>
    public async Task<int> PasserAsync()
    {
        await using var verrou = new NpgsqlConnection(connectionString);
        await verrou.OpenAsync();
        // Le verrou tient tant que cette connexion reste ouverte : libéré automatiquement si le processus meurt.
        if (!await verrou.ExecuteScalarAsync<bool>("SELECT pg_try_advisory_lock(@cle)", new { cle = CleVerrou }))
        {
            Console.WriteLine("Un autre planificateur est déjà en cours : rien à faire.");
            return 0;
        }

        await Battement(verrou);
        // « arret » restant d'un clic sans import en cours : au début d'un passage rien ne tourne, il est périmé (il tuerait la prochaine tâche).
        await verrou.ExecuteAsync("DELETE FROM import_etat WHERE cle = 'arret'");
        // Avec le verrou en main, une exécution « en cours » est forcément orpheline (processus tué, machine redémarrée) : on la clôt.
        await verrou.ExecuteAsync(
            """
            UPDATE import_executions
               SET fin = now(), ok = NULL, sortie = sortie || E'\n[Interrompue : le planificateur s''est arrêté avant la fin.]'
             WHERE fin IS NULL
            """);

        var echecs = 0;
        for (var tour = 0; tour < 50; tour++)
        {
            // Pause demandée depuis /Admin/Imports : aucune nouvelle tâche (le passage reste « vivant » : battement écrit, état visible).
            if (await verrou.ExecuteScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM import_etat WHERE cle = 'pause')"))
            {
                Console.WriteLine("Planificateur en pause (demandé depuis /Admin/Imports) : rien à lancer.");
                break;
            }
            var travaux = await ChercherTravauxAsync(verrou);
            if (travaux.Count == 0) break;
            foreach (var t in travaux)
            {
                var ok = await ExecuterAsync(verrou, t);
                if (!ok) echecs++;
                await Battement(verrou);
            }
        }
        return echecs == 0 ? 0 : 1;
    }

    private static Task Battement(NpgsqlConnection cx) => cx.ExecuteAsync(
        "INSERT INTO import_etat (cle, valeur) VALUES ('planificateur', now()) ON CONFLICT (cle) DO UPDATE SET valeur = now()");

    /// <summary>
    /// Demandes manuelles d'abord (dans l'ordre de demande), puis créneaux du planning dus, dans l'ordre des dépendances du catalogue.
    /// Une tâche déjà prévue dans le lot n'est pas ajoutée deux fois (même tâche demandée et planifiée, ou deux créneaux voisins).
    /// </summary>
    private static async Task<List<Travail>> ChercherTravauxAsync(NpgsqlConnection cx)
    {
        var lot = new List<Travail>();
        var demandes = await cx.QueryAsync<(long Id, string Tache, string? Par)>(
            "SELECT id, tache, demandee_par FROM import_demandes WHERE prise_le IS NULL ORDER BY id");
        foreach (var d in demandes)
            if (!lot.Any(x => x.Tache == d.Tache)) lot.Add(new Travail(d.Tache, "admin", d.Par, null, d.Id));
            else await cx.ExecuteAsync("UPDATE import_demandes SET prise_le = now() WHERE id = @Id", new { d.Id }); // doublon : classé, pas relancé

        var maintenant = DateTime.UtcNow;
        var lignes = (await cx.QueryAsync<LignePlanning>(
            """
            -- Alias explicites : Dapper ne convertit PAS snake_case en PascalCase dans ce projet. Sans « AS DernierLancement », la propriété restait
            -- à 0001-01-01, tout créneau passé depuis moins de 12 h paraissait « jamais lancé » et repartait à chaque passage (une fois par minute).
            SELECT p.id AS Id, p.tache AS Tache, p.jours AS Jours, p.heure AS Heure, p.dernier_lancement AS DernierLancement, t.ordre AS Ordre
              FROM import_planning p JOIN import_taches t ON t.code = p.tache
             WHERE p.actif
             ORDER BY t.ordre, p.id
            """)).ToList();
        foreach (var l in lignes)
        {
            // Garde-fou : une date de dernier lancement illisible ne doit jamais rendre un créneau « dû » en boucle.
            if (l.DernierLancement.Year < 2000) { Console.Error.WriteLine($"Créneau {l.Id} ignoré : dernier lancement illisible."); continue; }
            if (CreneauDu(l.Jours, l.Heure, l.DernierLancement, maintenant) is not { } creneau) continue;
            // Marqué tout de suite : même si la tâche échoue ou dure des heures, le créneau n'est pas rejoué en boucle.
            await cx.ExecuteAsync("UPDATE import_planning SET dernier_lancement = @maintenant WHERE id = @Id", new { maintenant, l.Id });
            if (!lot.Any(x => x.Tache == l.Tache)) lot.Add(new Travail(l.Tache, "planning", null, l.Id, null));
        }
        // Planning après les demandes, mais dans l'ordre des dépendances au sein de chaque groupe.
        var ordre = lignes.GroupBy(l => l.Tache).ToDictionary(g => g.Key, g => g.First().Ordre);
        return lot.OrderBy(t => t.Declencheur == "admin" ? 0 : 1).ThenBy(t => ordre.GetValueOrDefault(t.Tache, int.MaxValue)).ToList();
    }

    /// <summary>
    /// Dernier créneau (UTC) prévu par une ligne de planning, aujourd'hui ou hier à l'heure de Paris, s'il est passé, non encore déclenché et
    /// pas trop ancien ; sinon null. Hier est examiné pour un créneau de fin de soirée qui franchit minuit pendant un import long.
    /// Calcul pur (aucun accès base) : testé dans PlanificateurTests.
    /// </summary>
    public static DateTime? CreneauDu(short[] jours, TimeSpan heure, DateTime dernierLancementUtc, DateTime maintenantUtc)
    {
        var localMaintenant = TimeZoneInfo.ConvertTimeFromUtc(maintenantUtc, Paris);
        DateTime? meilleur = null;
        for (var delta = 0; delta >= -1; delta--)
        {
            var jour = localMaintenant.Date.AddDays(delta);
            // ISO : lundi = 1 … dimanche = 7 (DayOfWeek met dimanche à 0).
            var iso = jour.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)jour.DayOfWeek;
            if (!jours.Contains((short)iso)) continue;
            var local = DateTime.SpecifyKind(jour + heure, DateTimeKind.Unspecified);
            // Heure inexistante (passage à l'heure d'été) : on décale d'une heure plutôt que de lever une exception.
            if (Paris.IsInvalidTime(local)) local = local.AddHours(1);
            var utc = TimeZoneInfo.ConvertTimeToUtc(local, Paris);
            if (utc <= maintenantUtc && (meilleur is null || utc > meilleur)) meilleur = utc;
        }
        if (meilleur is null) return null;
        if (meilleur <= dernierLancementUtc) return null;                 // déjà déclenché
        if (maintenantUtc - meilleur > FenetreRattrapage) return null;    // trop ancien : abandonné
        return meilleur;
    }

    /// <summary>Arguments de l'Importer pour une tâche. « textes » réutilise le dump DOSLEG déjà téléchargé par « senat » (130 Mo décompressés) au lieu de le reprendre.</summary>
    private static string[]? Arguments(string tache)
    {
        if (!Commandes.TryGetValue(tache, out var args)) return null;
        if (tache == "textes")
        {
            var dosleg = Path.Combine(Directory.GetCurrentDirectory(), "data", "senat", "dosleg", "dosleg.sql");
            if (File.Exists(dosleg)) return [.. args, "--dosleg-sql", dosleg];
        }
        return args;
    }

    /// <summary>Exécute une tâche dans un processus enfant, journalise début, sortie (en direct) et résultat. Renvoie vrai si le code de sortie est 0.</summary>
    private static async Task<bool> ExecuterAsync(NpgsqlConnection cx, Travail t)
    {
        var id = await cx.ExecuteScalarAsync<long>(
            "INSERT INTO import_executions (tache, declencheur, demande_par, planning_id) VALUES (@Tache, @Declencheur, @DemandePar, @PlanningId) RETURNING id", t);
        if (t.DemandeId is { } d) await cx.ExecuteAsync("UPDATE import_demandes SET prise_le = now() WHERE id = @d", new { d });

        var args = Arguments(t.Tache);
        if (args is null)
        {
            await cx.ExecuteAsync("UPDATE import_executions SET fin = now(), ok = false, code_sortie = NULL, sortie = @s WHERE id = @id",
                new { id, s = $"Tâche inconnue : « {t.Tache} » (absente de la liste des commandes de l'Importer)." });
            return false;
        }

        var sortie = new StringBuilder();
        var erreurs = new List<string>();
        var verrouSortie = new object();
        // erreur = true pour la sortie d'erreur du processus ; sinon, la ligne est aussi relevée si elle ressemble à un message d'erreur.
        void Ajouter(string? ligne, bool erreur = false)
        {
            if (ligne is null) return;
            lock (verrouSortie)
            {
                sortie.AppendLine(ligne);
                if (sortie.Length > SortieMax) sortie.Remove(0, sortie.Length - SortieMax);
                if ((erreur || LigneErreur.IsMatch(ligne)) && !string.IsNullOrWhiteSpace(ligne) && erreurs.Count < 2000) erreurs.Add(ligne);
            }
        }
        string Instantane() { lock (verrouSortie) return sortie.ToString(); }

        var dll = Assembly.GetEntryAssembly()!.Location;
        var psi = new ProcessStartInfo(Environment.ProcessPath ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // Hôte « dotnet » (production : dotnet X.dll) : la dll est le premier argument. Exécutable natif (dotnet run sous Windows, apphost) : il se lance seul.
        if (Path.GetFileNameWithoutExtension(psi.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) psi.ArgumentList.Add(dll);
        foreach (var a in args) psi.ArgumentList.Add(a);
        // Les lignes import_runs écrites par l'enfant se rattachent à cette exécution (lues par /Admin/ImportExecution).
        psi.Environment["ILSONVOTE_EXECUTION_ID"] = id.ToString();

        var avant = await EffectifsAsync(cx);
        Ajouter($"[{DateTime.Now:HH:mm:ss}] Début : {t.Tache} ({string.Join(' ', args)})");
        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => Ajouter(e.Data);
        process.ErrorDataReceived += (_, e) => Ajouter(e.Data, erreur: true);

        int? code = null;
        string? erreur = null;
        var arretee = false;
        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var limite = new CancellationTokenSource(DureeMax);
            var attente = process.WaitForExitAsync(limite.Token);
            while (!attente.IsCompleted)
            {
                await Task.WhenAny(attente, Task.Delay(IntervalleSortie));
                await cx.ExecuteAsync("UPDATE import_executions SET sortie = @s WHERE id = @id", new { id, s = Instantane() });
                await Battement(cx);
                // « Arrêter l'import en cours » (/Admin/Imports) : le processus enfant est tué avec ses descendants ; l'exécution est marquée interrompue.
                if (await cx.ExecuteScalarAsync<bool>("DELETE FROM import_etat WHERE cle = 'arret' RETURNING true"))
                {
                    arretee = true;
                    try { process.Kill(entireProcessTree: true); } catch { /* déjà terminé */ }
                    erreur = "Arrêtée depuis /Admin/Imports.";
                    break;
                }
            }
            if (!arretee) await attente;
            process.WaitForExit();   // vide les derniers flux asynchrones avant de lire la sortie
            code = arretee ? null : process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* déjà terminé */ }
            erreur = $"Interrompue : durée maximale dépassée ({DureeMax.TotalHours:0} h).";
        }
        catch (Exception ex)
        {
            erreur = "Impossible de lancer ou de suivre la tâche : " + ex.Message;
        }

        if (erreur is not null) Ajouter(erreur);
        var ok = code == 0 && erreur is null;
        Ajouter($"[{DateTime.Now:HH:mm:ss}] Fin : {(arretee ? "interrompue" : ok ? "succès" : "échec")}{(code is { } c ? $" (code {c})" : "")}");
        // Bilan : effectif de chaque table avant / après. Les hausses = enregistrements ajoutés ; une baisse (remplacement d'une législature,
        // purge) apparaît aussi : le bilan dit ce qui a changé, sans interpréter.
        var apres = await EffectifsAsync(cx);
        var bilan = TablesSuivies.Keys.Where(k => avant[k] >= 0).ToDictionary(k => k, k => new { avant = avant[k], apres = apres[k] });
        var ajouts = bilan.Values.Sum(v => Math.Max(0, v.apres - v.avant));
        string erreursTexte; int nbErreurs;
        lock (verrouSortie) { erreursTexte = string.Join(Environment.NewLine, erreurs); nbErreurs = erreurs.Count; }
        await cx.ExecuteAsync(
            """
            UPDATE import_executions
               SET fin = now(), ok = @ok, code_sortie = @code, sortie = @s,
                   bilan = @bilan::jsonb, ajouts = @ajouts, nb_erreurs = @nbErreurs, erreurs = @erreursTexte
             WHERE id = @id
            """,
            new { id, ok = arretee ? (bool?)null : ok, code, s = Instantane(), bilan = System.Text.Json.JsonSerializer.Serialize(bilan), ajouts, nbErreurs, erreursTexte });
        return ok;
    }
}
