using Dapper;
using IlsOntVote.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IlsOntVote.Web.Pages.Admin;

/// <summary>Une tâche d'import du catalogue (table import_taches) avec sa dernière exécution.</summary>
public sealed class TacheImport
{
    public string Code { get; set; } = "";
    public string Libelle { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>Pays de la tâche (FR, GB, EU) ; NULL = tâche commune (traductions, statistiques, export).</summary>
    public string? Pays { get; set; }
    public int Ordre { get; set; }
    public bool Ponctuelle { get; set; }
    /// <summary>La tâche se lance en téléversant un fichier (source dont le téléchargement automatique est impossible).</summary>
    public bool AccepteFichier { get; set; }
    public long? DerniereId { get; set; }
    public DateTime? DernierDebut { get; set; }
    public DateTime? DerniereFin { get; set; }
    public bool? DernierOk { get; set; }
    /// <summary>Demandes manuelles pas encore prises en charge par le planificateur.</summary>
    public int EnAttente { get; set; }
}

/// <summary>Un créneau du planning (table import_planning) : des jours de la semaine (ISO, 1 = lundi) et une heure de Paris.</summary>
public sealed class CreneauImport
{
    public int Id { get; set; }
    public string? Pays { get; set; }
    public string Tache { get; set; } = "";
    public string Libelle { get; set; } = "";
    public short[] Jours { get; set; } = [];
    public TimeSpan Heure { get; set; }
    public bool Actif { get; set; }
}

/// <summary>Une exécution d'une tâche (table import_executions), sans sa sortie console (voir ImportExecution).</summary>
public sealed class ExecutionTache
{
    public long Id { get; set; }
    public string? Pays { get; set; }
    public string Tache { get; set; } = "";
    public string Libelle { get; set; } = "";
    public string Declencheur { get; set; } = "";
    public string? DemandePar { get; set; }
    public DateTime Debut { get; set; }
    public DateTime? Fin { get; set; }
    public int? CodeSortie { get; set; }
    public bool? Ok { get; set; }
    /// <summary>Enregistrements ajoutés (somme des hausses d'effectif des tables suivies) ; NULL = exécution non terminée ou antérieure au bilan.</summary>
    public long? Ajouts { get; set; }
    public int NbErreurs { get; set; }

    public TimeSpan? Duree => Fin is null ? null : Fin - Debut;
    public string Etat => ImportAffichage.Etat(Fin, Ok);
}

/// <summary>Un onglet de la page Imports : clé d'adresse (« » = tous, « FR », « GB », « commun »…), libellé, nombre de tâches, et vrai si une dernière exécution a échoué.</summary>
public sealed record OngletImport(string Cle, string Libelle, int Nombre, bool Echec);

/// <summary>Présentation commune aux pages d'imports : heure de Paris, durées, libellés de jours et d'états.</summary>
public static class ImportAffichage
{
    private static readonly TimeZoneInfo Paris = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");
    private static readonly string[] Jours = ["lundi", "mardi", "mercredi", "jeudi", "vendredi", "samedi", "dimanche"];
    public static readonly System.Globalization.CultureInfo Fr = new("fr-FR");

    /// <summary>Instant (UTC) -> heure de Paris, quel que soit le fuseau du serveur : le planning est exprimé en heure de Paris.</summary>
    public static string Date(DateTime? utc) => utc is null ? "—"
        : TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc), Paris).ToString("ddd d MMM HH:mm", Fr);

    public static string Duree(TimeSpan? d) => d is null ? "—"
        : d.Value.TotalHours >= 1 ? $"{(int)d.Value.TotalHours} h {d.Value.Minutes:00} min"
        : d.Value.TotalMinutes >= 1 ? $"{(int)d.Value.TotalMinutes} min {d.Value.Seconds:00} s"
        : $"{d.Value.Seconds} s";

    /// <summary>Jours ISO (1 = lundi) en texte : « tous les jours », « lundi, mercredi », « du lundi au vendredi ».</summary>
    public static string JoursTexte(IEnumerable<short> jours)
    {
        var j = jours.Distinct().OrderBy(x => x).ToList();
        if (j.Count == 7) return "tous les jours";
        if (j.SequenceEqual(new short[] { 1, 2, 3, 4, 5 })) return "du lundi au vendredi";
        return string.Join(", ", j.Where(x => x is >= 1 and <= 7).Select(x => Jours[x - 1]));
    }

    /// <summary>
    /// Prochaine occurrence (UTC) d'un créneau après <paramref name="maintenantUtc"/>, heure de Paris, sur les 8 prochains jours ; null si aucun jour.
    /// Même convention de jours que le planificateur (ISO, lundi = 1).
    /// </summary>
    public static DateTime? ProchaineOccurrence(IEnumerable<short> jours, TimeSpan heure, DateTime maintenantUtc)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(maintenantUtc, Paris);
        for (var d = 0; d <= 8; d++)
        {
            var jour = local.Date.AddDays(d);
            var iso = jour.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)jour.DayOfWeek;
            if (!jours.Contains((short)iso)) continue;
            var h = DateTime.SpecifyKind(jour + heure, DateTimeKind.Unspecified);
            if (Paris.IsInvalidTime(h)) h = h.AddHours(1);
            var utc = TimeZoneInfo.ConvertTimeToUtc(h, Paris);
            if (utc > maintenantUtc) return utc;
        }
        return null;
    }

    public static string Heure(TimeSpan h) => $"{h.Hours:00}:{h.Minutes:00}";

    /// <summary>« en cours » (pas de fin), « ok », « echec » ou « interrompue » (fin sans résultat : le planificateur s'est arrêté en route).</summary>
    public static string Etat(DateTime? fin, bool? ok) => fin is null ? "en cours" : ok is true ? "ok" : ok is false ? "echec" : "interrompue";
}

/// <summary>
/// Imports : catalogue des tâches (ce que fait chacune), bouton « Lancer maintenant », planning (plusieurs créneaux par tâche) et dernières
/// exécutions. Le site n'exécute rien lui-même : il écrit une demande (import_demandes) que le planificateur de l'Importer prend à son
/// prochain passage (≤ 1 minute). Cf. migration 026.
/// </summary>
public class ImportsModel(DapperContext db) : PageModel
{
    public IReadOnlyList<TacheImport> Taches { get; private set; } = [];
    public IReadOnlyList<CreneauImport> Planning { get; private set; } = [];
    public IReadOnlyList<ExecutionTache> Dernieres { get; private set; } = [];
    /// <summary>Dernier passage du planificateur (import_etat) ; NULL = jamais : il n'est pas installé ou pas démarré.</summary>
    public DateTime? DernierPassage { get; private set; }
    /// <summary>
    /// Onglet de pays choisi : « » = tous, un code pays (FR, GB, EU) ou « commun ». Mémorisé dans un cookie (les actions de la page rechargent sans paramètre) ;
    /// sans effet sur l'exécution : l'ordre des dépendances reste celui du catalogue, un onglet ne filtre que l'affichage.
    /// </summary>
    [BindProperty(SupportsGet = true)] public string? Pays { get; set; }
    public IReadOnlyList<OngletImport> Onglets { get; private set; } = [];
    private const string CookiePays = "admin_imports_pays";

    /// <summary>Exécutions sans fin et demandes non prises : ce que les boutons de réinitialisation concernent.</summary>
    /// <summary>Pause demandée : le planificateur ne lance plus rien (l'import en cours va à son terme, sauf « Arrêter »).</summary>
    public bool EnPause { get; private set; }
    public int EnCours { get; private set; }
    /// <summary>Import actuellement en cours (libellé et début), s'il y en a un.</summary>
    public (string Libelle, DateTime Debut)? TacheEnCours { get; private set; }
    /// <summary>Prochain créneau actif du planning (tous imports confondus), avec le libellé de l'import.</summary>
    public (string Libelle, DateTime Quand)? ProchainCreneau { get; private set; }
    public int DemandesEnAttente { get; private set; }
    [TempData] public string? Message { get; set; }

    public async Task OnGetAsync()
    {
        using var cx = db.CreateConnection();
        Taches = (await cx.QueryAsync<TacheImport>(
            """
            SELECT t.code, t.libelle, t.description, t.pays, t.ordre, t.ponctuelle, t.accepte_fichier,
                   e.id AS derniere_id, e.debut AS dernier_debut, e.fin AS derniere_fin, e.ok AS dernier_ok,
                   (SELECT count(*) FROM import_demandes d WHERE d.tache = t.code AND d.prise_le IS NULL)::int AS en_attente
              FROM import_taches t
              -- Dernière exécution de chaque tâche (LATERAL : une seule ligne par tâche, la plus récente).
              LEFT JOIN LATERAL (SELECT id, debut, fin, ok FROM import_executions WHERE tache = t.code ORDER BY id DESC LIMIT 1) e ON true
             ORDER BY t.ordre
            """)).ToList();
        Planning = (await cx.QueryAsync<CreneauImport>(
            "SELECT p.id, p.tache, t.libelle, t.pays, p.jours, p.heure, p.actif FROM import_planning p JOIN import_taches t ON t.code = p.tache ORDER BY t.ordre, p.heure, p.id")).ToList();
        // Onglet : paramètre d'adresse (même vide = « tous », choix explicite), sinon cookie.
        var paysOrdre = (await cx.QueryAsync<(string Code, string Nom, int Ordre)>("SELECT code, nom_fr, ordre FROM pays")).ToList();
        var noms = paysOrdre.ToDictionary(x => x.Code, x => x.Nom);
        if (Request.Query.ContainsKey("pays")) Response.Cookies.Append(CookiePays, Pays ?? "", new CookieOptions { MaxAge = TimeSpan.FromDays(365), HttpOnly = true, SameSite = SameSiteMode.Lax });
        else Pays = Request.Cookies.TryGetValue(CookiePays, out var memo) ? memo : "";
        Dernieres = await ImportsRequetes.ExecutionsAsync(cx, 120, null);   // assez large pour que chaque onglet garde ses 10 dernières : le filtre s'applique plus bas
        EnPause = await cx.ExecuteScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM import_etat WHERE cle = 'pause')");
        TacheEnCours = await cx.QueryFirstOrDefaultAsync<(string Libelle, DateTime Debut)?>(
            "SELECT COALESCE(t.libelle, e.tache), e.debut FROM import_executions e LEFT JOIN import_taches t ON t.code = e.tache WHERE e.fin IS NULL ORDER BY e.id DESC LIMIT 1");
        ProchainCreneau = Planning.Where(c => c.Actif)
            .Select(c => (c.Libelle, Quand: ImportAffichage.ProchaineOccurrence(c.Jours, c.Heure, DateTime.UtcNow)))
            .Where(x => x.Quand is not null).OrderBy(x => x.Quand).Select(x => ((string, DateTime)?)(x.Libelle, x.Quand!.Value)).FirstOrDefault();
        EnCours = await cx.ExecuteScalarAsync<int>("SELECT count(*) FROM import_executions WHERE fin IS NULL");
        DemandesEnAttente = await cx.ExecuteScalarAsync<int>("SELECT count(*) FROM import_demandes WHERE prise_le IS NULL");
        DernierPassage = await cx.ExecuteScalarAsync<DateTime?>("SELECT valeur FROM import_etat WHERE cle = 'planificateur'");

        // Onglets : « Tous », puis un par pays ayant des tâches (ordre du catalogue), puis « Commun ». Pastille d'échec : dernière exécution de la tâche en échec.
        string Cle(string? pays) => pays ?? "commun";
        // Seules les tâches PLANIFIÉES (au moins un créneau actif) alertent : les tâches manuelles/ponctuelles (anciens historiques qui
        // échouent toujours) gardent leur statut « échec » dans leur ligne mais ne gonflent pas les pastilles. Un succès ultérieur l'efface.
        var planifiees = Planning.Where(c => c.Actif).Select(c => c.Tache).ToHashSet();
        bool Echec(TacheImport t) => t.DerniereFin is not null && t.DernierOk == false && planifiees.Contains(t.Code);
        var onglets = new List<OngletImport> { new("", "Tous", Taches.Count, Taches.Any(Echec)) };
        // Ordre des pays de la table pays (France, Royaume-Uni, Union européenne), « Commun » en dernier.
        foreach (var g in Taches.GroupBy(t => Cle(t.Pays)).OrderBy(g => g.Key == "commun" ? int.MaxValue : paysOrdre.FirstOrDefault(x => x.Code == g.Key).Ordre))
            onglets.Add(new OngletImport(g.Key, g.Key == "commun" ? "Commun" : noms.GetValueOrDefault(g.Key, g.Key), g.Count(), g.Any(Echec)));
        Onglets = onglets;
        if (!Onglets.Any(o => o.Cle == (Pays ?? ""))) Pays = "";   // cookie d'un pays qui n'a plus de tâche : retour à « Tous »

        // Filtre d'affichage (le prochain créneau et l'état du planificateur ci-dessus restent calculés sur TOUT le planning).
        if (Pays is { Length: > 0 } choix)
        {
            Taches = Taches.Where(t => Cle(t.Pays) == choix).ToList();
            Planning = Planning.Where(c => Cle(c.Pays) == choix).ToList();
            Dernieres = Dernieres.Where(e => Cle(e.Pays) == choix).ToList();
        }
        Dernieres = Dernieres.Take(10).ToList();
    }

    /// <summary>« Lancer maintenant » : une demande par tâche à la fois (déjà en attente ou en cours = refus explicite, pas de doublon silencieux).</summary>
    public async Task<IActionResult> OnPostLancerAsync(string? tache)
    {
        using var cx = db.CreateConnection();
        var libelle = await cx.ExecuteScalarAsync<string?>("SELECT libelle FROM import_taches WHERE code = @tache", new { tache });
        if (libelle is null) { Message = "Tâche inconnue."; return RedirectToPage(); }
        if (await cx.ExecuteScalarAsync<bool>("SELECT accepte_fichier FROM import_taches WHERE code = @tache", new { tache }))
        { Message = $"« {libelle} » se lance en téléversant un fichier."; return RedirectToPage(); }
        if (await cx.ExecuteScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM import_demandes WHERE tache = @tache AND prise_le IS NULL)", new { tache }))
        { Message = $"« {libelle} » est déjà demandée : elle partira au prochain passage du planificateur."; return RedirectToPage(); }
        if (await cx.ExecuteScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM import_executions WHERE tache = @tache AND fin IS NULL)", new { tache }))
        { Message = $"« {libelle} » est déjà en cours."; return RedirectToPage(); }
        await cx.ExecuteAsync("INSERT INTO import_demandes (tache, demandee_par) VALUES (@tache, @par)", new { tache, par = User.Identity?.Name });
        Message = $"« {libelle} » demandée : elle démarre au prochain passage du planificateur (moins d'une minute).";
        return RedirectToPage();
    }

    /// <summary>Taille maximale d'un fichier téléversé (l'export des pétitions du Canada pèse environ 7 Mo). La même limite figure dans la configuration nginx.</summary>
    private const int FichierMax = 30 * 1024 * 1024;

    /// <summary>
    /// Téléversement d'un fichier d'import : stocké en base avec une demande, que le planificateur exécute. Réservé aux tâches marquées
    /// <c>accepte_fichier</c> ; le contenu n'est validé que par l'import lui-même (son échec apparaît dans le journal).
    /// </summary>
    public async Task<IActionResult> OnPostTeleverserAsync(string? tache, IFormFile? fichier)
    {
        using var cx = db.CreateConnection();
        var libelle = await cx.ExecuteScalarAsync<string?>("SELECT libelle FROM import_taches WHERE code = @tache AND accepte_fichier", new { tache });
        if (libelle is null) { Message = "Cette tâche n'accepte pas de fichier."; return RedirectToPage(); }
        if (fichier is null || fichier.Length == 0) { Message = "Choisir un fichier."; return RedirectToPage(); }
        if (fichier.Length > FichierMax) { Message = "Fichier trop gros (30 Mo au plus)."; return RedirectToPage(); }
        if (await cx.ExecuteScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM import_demandes WHERE tache = @tache AND prise_le IS NULL)", new { tache }))
        { Message = $"« {libelle} » attend déjà un passage du planificateur : réessayer dans une minute."; return RedirectToPage(); }
        using var tampon = new MemoryStream();
        await fichier.CopyToAsync(tampon);
        // Fichier et demande en une seule instruction : jamais l'un sans l'autre.
        await cx.ExecuteAsync(
            """
            WITH f AS (INSERT INTO import_fichiers (nom, taille, contenu, ajoute_par) VALUES (@nom, @taille, @contenu, @par) RETURNING id)
            INSERT INTO import_demandes (tache, demandee_par, fichier_id) SELECT @tache, @par, id FROM f
            """,
            new { tache, nom = Path.GetFileName(fichier.FileName), taille = (int)fichier.Length, contenu = tampon.ToArray(), par = User.Identity?.Name });
        Message = $"Fichier reçu pour « {libelle} » : l'import démarre au prochain passage du planificateur (moins d'une minute).";
        return RedirectToPage();
    }

    /// <summary>Ajoute un créneau : jours cochés (ISO 1 à 7) + heure de Paris. Créé « déjà passé » (dernier_lancement = maintenant) : pas de rattrapage rétroactif.</summary>
    public async Task<IActionResult> OnPostAjouterCreneauAsync(string? tache, short[]? jours, string? heure)
    {
        var j = (jours ?? []).Where(x => x is >= 1 and <= 7).Distinct().OrderBy(x => x).ToArray();
        if (j.Length == 0 || !TimeOnly.TryParse(heure, out var h)) { Message = "Choisir au moins un jour et une heure valide."; return RedirectToPage(); }
        using var cx = db.CreateConnection();
        if (!await cx.ExecuteScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM import_taches WHERE code = @tache AND NOT accepte_fichier)", new { tache })) { Message = "Tâche inconnue ou à fichier (non planifiable)."; return RedirectToPage(); }
        await cx.ExecuteAsync("INSERT INTO import_planning (tache, jours, heure, cree_par) VALUES (@tache, @j, @heure, @par)",
            new { tache, j, heure = h.ToTimeSpan(), par = User.Identity?.Name });
        Message = $"Créneau ajouté : {ImportAffichage.JoursTexte(j)} à {ImportAffichage.Heure(h.ToTimeSpan())} (heure de Paris).";
        return RedirectToPage();
    }

    /// <summary>
    /// Réinitialisation : clôt les exécutions « en cours » (processus tué avec le service, machine redémarrée) pour débloquer le bouton « Lancer ».
    /// Elles sont marquées « interrompues », pas « réussies » : rien n'est inventé. Si le planificateur travaille réellement, il réécrit sa propre fin.
    /// </summary>
    public async Task<IActionResult> OnPostReinitialiserExecutionsAsync()
    {
        using var cx = db.CreateConnection();
        var n = await cx.ExecuteAsync(
            "UPDATE import_executions SET fin = now(), ok = NULL, sortie = sortie || E'\n[Interrompue manuellement depuis /Admin/Imports.]' WHERE fin IS NULL");
        Message = n == 0 ? "Aucune exécution en cours." : $"{n} exécution(s) marquée(s) interrompue(s).";
        return RedirectToPage();
    }

    /// <summary>Met le planificateur en pause (ou le reprend). Le site n'a pas la main sur systemd : l'ordre passe par la base, lu à chaque passage (≤ 1 minute).</summary>
    public async Task<IActionResult> OnPostBasculerPauseAsync()
    {
        using var cx = db.CreateConnection();
        var pause = await cx.ExecuteScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM import_etat WHERE cle = 'pause')");
        if (pause) await cx.ExecuteAsync("DELETE FROM import_etat WHERE cle = 'pause'");
        else await cx.ExecuteAsync("INSERT INTO import_etat (cle, valeur) VALUES ('pause', now()) ON CONFLICT (cle) DO NOTHING");
        Message = pause ? "Planificateur repris : les demandes et le planning sont de nouveau exécutés." : "Planificateur en pause : plus aucune tâche ne démarre. Les créneaux manqués pendant la pause sont rattrapés jusqu'à 12 h.";
        return RedirectToPage();
    }

    /// <summary>Demande l'arrêt de l'import en cours : le planificateur tue le processus au plus tard 5 secondes plus tard et marque l'exécution interrompue.</summary>
    public async Task<IActionResult> OnPostArreterEnCoursAsync()
    {
        using var cx = db.CreateConnection();
        if (!await cx.ExecuteScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM import_executions WHERE fin IS NULL)")) { Message = "Aucun import en cours."; return RedirectToPage(); }
        await cx.ExecuteAsync("INSERT INTO import_etat (cle, valeur) VALUES ('arret', now()) ON CONFLICT (cle) DO UPDATE SET valeur = now()");
        Message = "Arrêt demandé : l'import en cours est interrompu dans quelques secondes. Pour empêcher le suivant, mettre aussi le planificateur en pause.";
        return RedirectToPage();
    }

    /// <summary>Annule les demandes manuelles pas encore prises par le planificateur (elles sont classées comme prises, pas supprimées : traçabilité).</summary>
    public async Task<IActionResult> OnPostAnnulerDemandesAsync()
    {
        using var cx = db.CreateConnection();
        var n = await cx.ExecuteAsync("UPDATE import_demandes SET prise_le = now() WHERE prise_le IS NULL");
        Message = n == 0 ? "Aucune demande en attente." : $"{n} demande(s) annulée(s).";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSupprimerCreneauAsync(int id)
    {
        using var cx = db.CreateConnection();
        await cx.ExecuteAsync("DELETE FROM import_planning WHERE id = @id", new { id });
        Message = "Créneau supprimé.";
        return RedirectToPage();
    }

    /// <summary>Active ou suspend un créneau sans le supprimer. À la réactivation, aucun rattrapage des créneaux passés pendant la suspension.</summary>
    public async Task<IActionResult> OnPostBasculerCreneauAsync(int id)
    {
        using var cx = db.CreateConnection();
        await cx.ExecuteAsync("UPDATE import_planning SET actif = NOT actif, dernier_lancement = now() WHERE id = @id", new { id });
        return RedirectToPage();
    }
}

/// <summary>Requêtes partagées par les pages d'imports.</summary>
public static class ImportsRequetes
{
    /// <summary>Les dernières exécutions (sans leur sortie console, volumineuse).</summary>
    public static async Task<IReadOnlyList<ExecutionTache>> ExecutionsAsync(System.Data.IDbConnection cx, int max, string? pays) =>
        (await cx.QueryAsync<ExecutionTache>(
            """
            SELECT e.id, e.tache, COALESCE(t.libelle, e.tache) AS libelle, t.pays, e.declencheur, e.demande_par, e.debut, e.fin, e.code_sortie, e.ok, e.ajouts, e.nb_erreurs
              FROM import_executions e LEFT JOIN import_taches t ON t.code = e.tache
             ORDER BY e.id DESC LIMIT @max
            """, new { max })).ToList();
}
