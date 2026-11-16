using IlsOntVote.Importer;
using Microsoft.Extensions.Configuration;

// Usage :
//   dotnet run -- an [--legislature 17 | 15,16,17 | all] [--scrutins-dir D --amo-dir D]
//   dotnet run -- senat [--senateurs-dir D --dosleg-sql F]
//
// Sans options *-dir, les jeux sont téléchargés (data.assemblee-nationale.fr, data.senat.fr)
// dans ./data (ignoré par Git), puis extraits.
// AN « all » = législatures complètes disponibles : 15, 16, 17 (la 14e n'a pas
// d'annuaire complet en open data : des députés manqueraient).
// En exploitation, rejouer chaque jour : `an` (17 seule) et `senat`.
//
// Connexion : clés ConnectionStrings:PostgreSQL_Debug (DOTNET_ENVIRONMENT=Development)
// ou PostgreSQL_Release, dans appsettings(.Development).json (jamais commités)
// ou variables d'environnement (ConnectionStrings__PostgreSQL_Release).

const int LegislatureEnCours = 17;
int[] LegislaturesCompletes = [15, 16, 17];

var argv = args.ToList();
if (argv.Count == 0 || argv[0] is not ("an" or "senat" or "textes" or "ue" or "stats" or "dossiers" or "traductions" or "export"))
{
    Console.Error.WriteLine("Usage : IlsOntVote.Importer an [--legislature 17|15,16,17|all] [--scrutins-dir D --amo-dir D]");
    Console.Error.WriteLine("        IlsOntVote.Importer senat [--senateurs-dir D --dosleg-sql F]");
    Console.Error.WriteLine("        IlsOntVote.Importer textes [--dosleg-sql F]   (cycle de vie des textes + rattachement des scrutins)");
    return 2;
}

string? Option(string name)
{
    var i = argv.IndexOf(name);
    return i >= 0 && i + 1 < argv.Count ? argv[i + 1] : null;
}

var env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";
var config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile($"appsettings.{env}.json", optional: true)
    // Production : mêmes secrets que le site. Le service d'import donne le chemin du fichier du site (variable
    // ILSONVOTE_LOCAL_CONFIG) : une seule copie du mot de passe de la base à tenir à jour.
    .AddJsonFile(Environment.GetEnvironmentVariable("ILSONVOTE_LOCAL_CONFIG") ?? "appsettings.Local.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var key = env == "Development" ? "PostgreSQL_Debug" : "PostgreSQL_Release";
var connectionString = config.GetConnectionString(key);
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine($"Connection string '{key}' introuvable (appsettings.{env}.json ou variable d'environnement).");
    return 2;
}

var repo = new ImportRepository(connectionString);
return argv[0] switch
{
    "an" => await ImporterAnAsync(),
    "senat" => await ImporterSenatAsync(),
    "ue" => await ImporterUeAsync(),
    "stats" => await RafraichirStatsAsync(),
    "dossiers" => await ImporterDossiersAsync(),
    "traductions" => await TraduireTitresAsync(),
    "export" => await ExporterDonneesAsync(),
    _ => await ImporterTextesAsync(),
};

// --------------------------------------------------------------------------

async Task<int> ImporterAnAsync()
{
    var option = Option("--legislature") ?? LegislatureEnCours.ToString();
    int[] legislatures = option == "all"
        ? LegislaturesCompletes
        : option.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(int.Parse).ToArray();

    var scrutinsDirOpt = Option("--scrutins-dir");
    var amoDirOpt = Option("--amo-dir");
    if ((scrutinsDirOpt is not null || amoDirOpt is not null) && legislatures.Length != 1)
    {
        Console.Error.WriteLine("--scrutins-dir / --amo-dir ne s'utilisent qu'avec une seule législature.");
        return 2;
    }

    var source = new AssembleeNationaleSource();
    var codeRetour = 0;

    foreach (var legislature in legislatures)
    {
        Console.WriteLine($"=== Assemblée nationale — législature {legislature} ===");
        var runId = await repo.DemarrerAsync($"AN-L{legislature}");
        try
        {
            var (scrutinsDir, amoDir) = await ResoudreDossiersAnAsync(legislature, scrutinsDirOpt, amoDirOpt);

            Console.WriteLine("Lecture de l'annuaire…");
            var annuaire = source.LireAnnuaire(amoDir, legislature, await repo.LireSlugsAsync());
            Console.WriteLine($"  {annuaire.Parlementaires.Count} députés, {annuaire.Groupes.Count} groupes, {annuaire.Mandats.Count} mandats.");
            await repo.EcrireAnnuaireAsync(annuaire, Chambres.AN, [legislature]);

            Console.WriteLine("Import des scrutins…");
            var (nbScrutins, nbVotes, ignores) = await repo.EcrireScrutinsAsync(source.LireScrutins(scrutinsDir), annuaire);
            Console.WriteLine($"  {nbScrutins} scrutins, {nbVotes} votes ({ignores} ignorés : député absent de l'annuaire).");

            await repo.TerminerAsync(runId, true, nbScrutins, nbVotes,
                ignores > 0 ? $"{ignores} votes ignorés (député absent de l'annuaire)" : null);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            await repo.TerminerAsync(runId, false, 0, 0, ex.Message);
            codeRetour = 1;
        }
    }
    return codeRetour;
}

/// <summary>
/// Parlement européen : INCRÉMENTAL (seuls les votes absents de la base sont lus, un appel d'API par vote : le premier import
/// prend du temps, les suivants quelques secondes). <c>--limit N</c> borne le nombre de votes lus (test).
/// </summary>
async Task<int> ImporterUeAsync()
{
    Console.WriteLine("=== Parlement européen (HowTheyVote) ===");
    var runId = await repo.DemarrerAsync("UE");
    try
    {
        using var source = new ParlementEuropeenSource();

        // Rattrapage : titres français (Observatoire législatif) des votes principaux déjà importés en anglais.
        if (argv.Contains("--titres"))
        {
            var refs = await repo.LireReferencesUeAsync();
            int traduits = 0, lignes = 0;
            foreach (var reference in refs)
            {
                var fr = await source.TitreFrancaisAsync(reference);
                if (fr is null) continue;
                lignes += await repo.MettreTitreUeAsync(reference, fr, ParlementEuropeenSource.NoteTitreFr);
                traduits++;
            }
            Console.WriteLine($"  {traduits}/{refs.Count} procédures traduites, {lignes} scrutins mis à jour.");
            // Titres anglais d'origine (HowTheyVote) : ils servent l'interface /en ; une seule série de pages de liste, pas un appel par vote.
            var anglais = (await source.ListerVotesAsync()).Select(v => (v.Id, v.Titre)).Where(v => v.Titre.Length > 0).ToList();
            Console.WriteLine($"  {await repo.MettreTitresEnUeAsync(anglais)} titres anglais de votes européens mis à jour.");
            await repo.TerminerAsync(runId, true, lignes, 0, $"titres français : {traduits}/{refs.Count} procédures");
            return 0;
        }

        var limite = int.TryParse(Option("--limit"), out var l) ? l : int.MaxValue;
        var (deja, _) = await repo.LireScrutinsUeAsync();
        var liste = await source.ListerVotesAsync();
        var aLire = liste.Where(v => !deja.Contains($"EP-{v.Id}")).Take(limite).ToList();
        Console.WriteLine($"  {liste.Count} votes au total, {deja.Count} déjà en base, {aLire.Count} à lire.");

        var existants = await repo.LireSlugsAsync();
        var slugs = existants.Values.ToHashSet();
        var groupes = new Dictionary<string, GroupeRow>();
        var parlementaires = new Dictionary<string, ParlementaireRow>();
        int nbScrutins = 0; long nbVotes = 0;

        foreach (var lot in aLire.Chunk(25))
        {
            var scrutins = new List<ScrutinAvecVotes>();
            // Vus dans CE lot seulement : les mandats sont écrits après chaque lot (et fusionnés avec l'existant), de sorte
            // qu'un import interrompu laisse toujours un état cohérent (sinon la liste des eurodéputés restait vide).
            var vus = new Dictionary<string, (DateOnly Premier, DateOnly Dernier, string GroupeId)>();
            foreach (var (id, _, _) in lot)
            {
                var (sc, membres) = await source.LireVoteAsync(id);
                scrutins.Add(sc);
                foreach (var m in membres)
                {
                    groupes[m.GroupeId] = new GroupeRow(m.GroupeId, Chambres.UE, ParlementEuropeenSource.Legislature,
                        m.GroupeSigle, m.GroupeLibelle, CouleurUe(m.GroupeSigle), GroupeOrdre.Pour(m.GroupeSigle));
                    if (!parlementaires.ContainsKey(m.Id))
                    {
                        // Slug déjà attribué conservé (il fait l'adresse de la page) ; sinon nouveau slug unique.
                        var slug = existants.TryGetValue(m.Id, out var s0) ? s0 : Slugs.Unique(slugs, m.Prenom, m.Nom, m.Id);
                        parlementaires[m.Id] = new ParlementaireRow(m.Id, Chambres.UE, null, m.Prenom, m.Nom, slug,
                            ParlementEuropeenSource.Pays(m.PaysIso), m.PartiNational);
                    }
                    // Premier / dernier vote où l'élu figure, et son groupe à ce dernier vote.
                    vus[m.Id] = vus.TryGetValue(m.Id, out var v)
                        ? (m.Vu < v.Premier ? m.Vu : v.Premier, m.Vu >= v.Dernier ? m.Vu : v.Dernier, m.Vu >= v.Dernier ? m.GroupeId : v.GroupeId)
                        : (m.Vu, m.Vu, m.GroupeId);
                }
                await Task.Delay(500); // politesse envers une API bénévole qui limite le débit (429 au-delà de ~100 appels rapides)
            }
            await repo.EcrireMembresUeAsync(groupes.Values, parlementaires.Values);
            var annuaire = new Annuaire(groupes.Values.ToList(), parlementaires.Values.ToList(), [], []);
            var (_, ancienPlusRecent) = await repo.LireScrutinsUeAsync(); // plus récent vote connu AVANT ce lot
            var (n, v2, _) = await repo.EcrireScrutinsAsync(scrutins, annuaire);
            await repo.EcrireMandatsUeAsync(vus, ancienPlusRecent);
            nbScrutins += n; nbVotes += v2;
            Console.WriteLine($"  {nbScrutins}/{aLire.Count} votes lus…");
        }

        Console.WriteLine($"  {nbScrutins} scrutins, {nbVotes} votes, {parlementaires.Count} eurodéputés.");
        await repo.TerminerAsync(runId, true, nbScrutins, nbVotes, null);
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex);
        await repo.TerminerAsync(runId, false, 0, 0, ex.Message);
        return 1;
    }
}

/// <summary>Couleur initiale d'un groupe du PE (l'import ne l'écrase pas ensuite).</summary>
static string? CouleurUe(string sigle) => sigle.ToUpperInvariant() switch
{
    "THE LEFT" => "#a4123f", "GREENS/EFA" => "#5aa02c", "S&D" => "#e75480", "RENEW" => "#f2b705",
    "EPP" => "#3399cc", "ECR" => "#1f5f8b", "PFE" => "#6b3fa0", "ESN" => "#4a4a8a", "NI" => "#8d949a", _ => null,
};

/// <summary>
/// Étapes de lecture des textes à l'Assemblée, avec ou sans scrutin public (Dossiers législatifs de l'AN). Par défaut la législature en
/// cours ; <c>--legislature 15,16,17|all</c> pour l'historique. À lancer APRÈS « an » et « textes » (rattachement aux scrutins et aux textes).
/// </summary>
async Task<int> ImporterDossiersAsync()
{
    var option = Option("--legislature") ?? LegislatureEnCours.ToString();
    int[] legislatures = option == "all" ? LegislaturesCompletes
        : option.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(int.Parse).ToArray();
    var codeRetour = 0;
    foreach (var legislature in legislatures)
    {
        Console.WriteLine($"=== Dossiers législatifs — législature {legislature} ===");
        var runId = await repo.DemarrerAsync($"DOSSIERS-L{legislature}");
        try
        {
            var dossier = await Telechargement.TelechargerZipAsync(DossiersAnSource.Url(legislature),
                Path.Combine(Directory.GetCurrentDirectory(), "data", "an", $"dossiers-L{legislature}"));
            var etapes = DossiersAnSource.Lire(dossier).ToList();
            var (total, sans, rattachees) = await repo.EcrireEtapesAsync(etapes, legislature);
            Console.WriteLine($"  {total} décisions de séance, dont {sans} sans scrutin public ; {rattachees} rattachées à un texte.");
            await repo.TerminerAsync(runId, true, total, 0, $"{sans} sans scrutin public, {rattachees} rattachées à un texte");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            await repo.TerminerAsync(runId, false, 0, 0, ex.Message);
            codeRetour = 1;
        }
    }
    return codeRetour;
}

/// <summary>
/// Traduction automatique des titres avec OPUS-MT, via le script <c>tools/traduire_titres.py</c>, dans les DEUX sens :
///  - fr -> en : titres de lois et de votes français (interface /en) ;
///  - en -> fr : votes du Parlement européen sans titre français officiel (interface française).
/// Incrémental : une mémoire en base (traductions_titres / traductions_titres_fr) évite de retraduire ; chaque nuit, seuls les NOUVEAUX titres
/// passent par le modèle. ÉTAPE OPTIONNELLE : sans modèle ou sans Python, chaque sens est ignoré (code de sortie 0) : l'import nocturne ne doit
/// jamais échouer pour ça. Variables : ILSONVOTE_PYTHON (défaut « python3 »), ILSONVOTE_MODELE_TRADUCTION (fr-en, défaut ./models/opus-mt-fr-en),
/// ILSONVOTE_MODELE_TRADUCTION_EN_FR (défaut ./models/opus-mt-en-fr). <c>--limit N</c> pour tester.
/// </summary>
async Task<int> TraduireTitresAsync()
{
    Console.WriteLine("=== Traduction des titres (fr -> en, et en -> fr pour le Parlement européen) ===");
    var racine = Path.Combine(Directory.GetCurrentDirectory(), "models");
    var modeleFrEn = Environment.GetEnvironmentVariable("ILSONVOTE_MODELE_TRADUCTION") ?? Path.Combine(racine, "opus-mt-fr-en");
    var modeleEnFr = Environment.GetEnvironmentVariable("ILSONVOTE_MODELE_TRADUCTION_EN_FR") ?? Path.Combine(racine, "opus-mt-en-fr");
    var script = Path.Combine(AppContext.BaseDirectory, "tools", "traduire_titres.py");
    bool Disponible(string modele) => File.Exists(Path.Combine(modele, "model.bin")) && File.Exists(script);

    // Réapplique d'abord la mémoire en->fr : le ré-import du jour a remis les titres anglais de la source, même sans modèle installé.
    var reappliques = await repo.AppliquerTraductionsFrAsync();
    if (reappliques > 0) Console.WriteLine($"  {reappliques} titres européens retraduits depuis la mémoire.");

    var frEn = Disponible(modeleFrEn);
    var enFr = Disponible(modeleEnFr);
    if (!frEn) Console.WriteLine($"  fr -> en non configuré (modèle absent : {modeleFrEn}) : ignoré. Voir docs/TRADUCTION.md.");
    if (!enFr) Console.WriteLine($"  en -> fr non configuré (modèle absent : {modeleEnFr}) : ignoré. Voir docs/TRADUCTION.md.");
    if (!frEn && !enFr) return 0;

    var runId = await repo.DemarrerAsync("TRADUCTIONS");
    try
    {
        var limite = int.TryParse(Option("--limit"), out var l) ? l : 20000;
        var messages = new List<string>();
        var total = 0;

        if (frEn)
        {
            var titres = await repo.LireTitresATraduireAsync(limite);
            Console.WriteLine($"  fr -> en : {titres.Count} titres à traduire.");
            if (titres.Count > 0)
            {
                var resultats = await LancerScriptTraductionAsync(modeleFrEn, "fr-en", titres);
                var n = await repo.EcrireTraductionsAsync(resultats, "opus-mt-fr-en");
                Console.WriteLine($"  {resultats.Count} titres traduits (fr -> en), {n} textes et scrutins mis à jour.");
                messages.Add($"{resultats.Count} fr->en"); total += n;
            }
        }
        if (enFr)
        {
            var titres = await repo.LireTitresATraduireFrAsync(limite);
            Console.WriteLine($"  en -> fr : {titres.Count} titres européens à traduire.");
            if (titres.Count > 0)
            {
                var resultats = await LancerScriptTraductionAsync(modeleEnFr, "en-fr", titres);
                var n = await repo.EcrireTraductionsFrAsync(resultats, "opus-mt-en-fr");
                Console.WriteLine($"  {resultats.Count} titres traduits (en -> fr), {n} votes mis à jour.");
                messages.Add($"{resultats.Count} en->fr"); total += n;
            }
        }
        await repo.TerminerAsync(runId, true, total, 0, messages.Count == 0 ? "rien à traduire" : string.Join(", ", messages));
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex);
        await repo.TerminerAsync(runId, false, 0, 0, ex.Message);
        return 1;
    }
}

/// <summary>
/// Passe une liste de titres au script Python (fichier JSON Lines en entrée, un autre en sortie) et renvoie les couples (source, traduction).
/// <paramref name="sens"/> : « fr-en » ou « en-fr ». L'identifiant échangé est l'indice dans la liste : le titre source sert de clé en base.
/// </summary>
async Task<IReadOnlyList<(string Source, string Cible)>> LancerScriptTraductionAsync(string modele, string sens, IReadOnlyList<string> titres)
{
    var script = Path.Combine(AppContext.BaseDirectory, "tools", "traduire_titres.py");
    var (langueSource, langueCible) = (sens[..2], sens[3..]);
    var dossier = Path.Combine(Path.GetTempPath(), "ilsontvote-trad-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dossier);
    var entree = Path.Combine(dossier, "entree.jsonl");
    var sortie = Path.Combine(dossier, "sortie.jsonl");
    try
    {
        // Première lettre en majuscule (comme à l'affichage) : le modèle traduit mieux une phrase qui commence par une majuscule.
        await File.WriteAllLinesAsync(entree, titres.Select((t, i) => System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["id"] = i.ToString(),
            [langueSource] = t.Length == 0 ? t : char.ToUpperInvariant(t[0]) + t[1..],
        })));

        var psi = new System.Diagnostics.ProcessStartInfo(Environment.GetEnvironmentVariable("ILSONVOTE_PYTHON") ?? "python3")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        psi.ArgumentList.Add(script); psi.ArgumentList.Add(modele); psi.ArgumentList.Add(entree); psi.ArgumentList.Add(sortie); psi.ArgumentList.Add(sens);
        using var proc = System.Diagnostics.Process.Start(psi)!;
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) Console.WriteLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) Console.Error.WriteLine(e.Data); };
        proc.BeginOutputReadLine(); proc.BeginErrorReadLine();
        await proc.WaitForExitAsync();
        if (proc.ExitCode != 0) throw new InvalidOperationException($"Le script de traduction ({sens}) a échoué (code {proc.ExitCode}).");

        return (await File.ReadAllLinesAsync(sortie))
            .Where(x => x.Length > 0)
            .Select(x => System.Text.Json.JsonDocument.Parse(x).RootElement)
            .Select(e => (Source: titres[int.Parse(e.GetProperty("id").GetString()!)], Cible: e.GetProperty(langueCible).GetString()!))
            .Where(t => t.Cible.Length > 0)
            .ToList();
    }
    finally { try { Directory.Delete(dossier, true); } catch (IOException) { } }
}

/// <summary>
/// Données ouvertes : écrit les fichiers CSV compressés (scrutins, votes, élus, textes…) et le manifeste dans le dossier d'export
/// (ILSONVOTE_EXPORT_DIR, défaut ./data/export) que le site sert sur /donnees. À lancer après les imports (import-all.sh).
/// </summary>
async Task<int> ExporterDonneesAsync()
{
    Console.WriteLine("=== Export des données ouvertes ===");
    var dossier = Environment.GetEnvironmentVariable("ILSONVOTE_EXPORT_DIR") ?? Path.Combine(Directory.GetCurrentDirectory(), "data", "export");
    var runId = await repo.DemarrerAsync("EXPORT");
    try
    {
        var manifeste = await ExportDonnees.ExporterAsync(connectionString, dossier, Console.WriteLine);
        await repo.TerminerAsync(runId, true, (int)Math.Min(int.MaxValue, manifeste.Sum(m => m.Lignes)), 0, $"{manifeste.Count} fichiers");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex);
        await repo.TerminerAsync(runId, false, 0, 0, ex.Message);
        return 1;
    }
}

/// <summary>Recalcule les statistiques de participation (à lancer après les imports ; plusieurs minutes).</summary>
async Task<int> RafraichirStatsAsync()
{
    Console.WriteLine("=== Statistiques de participation ===");
    var runId = await repo.DemarrerAsync("STATS");
    try
    {
        await repo.RafraichirStatsAsync();
        Console.WriteLine("  Vues stats_participation_groupe et stats_participation_elu recalculées.");
        await repo.TerminerAsync(runId, true, 0, 0, null);
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex);
        await repo.TerminerAsync(runId, false, 0, 0, ex.Message);
        return 1;
    }
}

async Task<int> ImporterSenatAsync()
{
    Console.WriteLine("=== Sénat ===");
    var runId = await repo.DemarrerAsync("SENAT");
    try
    {
        var senateursDir = Option("--senateurs-dir");
        var doslegSql = Option("--dosleg-sql");

        if (senateursDir is null)
        {
            senateursDir = Path.Combine(Directory.GetCurrentDirectory(), "data", "senat", "senateurs");
            foreach (var f in SenatSource.FichiersSenateurs)
                await Telechargement.TelechargerFichierAsync($"https://data.senat.fr/data/senateurs/{f}.json",
                    Path.Combine(senateursDir, $"{f}.json"));
        }
        if (doslegSql is null)
        {
            var dossier = await Telechargement.TelechargerZipAsync("https://data.senat.fr/data/dosleg/dosleg.zip",
                Path.Combine(Directory.GetCurrentDirectory(), "data", "senat", "dosleg"));
            doslegSql = Path.Combine(dossier, "dosleg.sql");
        }

        var source = new SenatSource();
        Console.WriteLine("Lecture de l'annuaire…");
        var annuaire = source.LireAnnuaire(senateursDir, await repo.LireSlugsAsync());
        Console.WriteLine($"  {annuaire.Parlementaires.Count} sénateurs, {annuaire.Groupes.Count} groupes, {annuaire.Mandats.Count} mandats.");
        await repo.EcrireAnnuaireAsync(annuaire, Chambres.Senat, null);

        Console.WriteLine("Import des scrutins (lecture du dump, quelques minutes)…");
        var (nbScrutins, nbVotes, ignores) = await repo.EcrireScrutinsAsync(source.LireScrutins(doslegSql, annuaire), annuaire);
        Console.WriteLine($"  {nbScrutins} scrutins, {nbVotes} votes ({ignores} ignorés : sénateur absent de l'annuaire).");

        await repo.TerminerAsync(runId, true, nbScrutins, nbVotes,
            ignores > 0 ? $"{ignores} votes ignorés (sénateur absent de l'annuaire)" : null);
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex);
        await repo.TerminerAsync(runId, false, 0, 0, ex.Message);
        return 1;
    }
}

async Task<int> ImporterTextesAsync()
{
    Console.WriteLine("=== Textes de loi (DOSLEG) ===");
    var runId = await repo.DemarrerAsync("TEXTES");
    try
    {
        var doslegSql = Option("--dosleg-sql");
        if (doslegSql is null)
        {
            var dossier = await Telechargement.TelechargerZipAsync("https://data.senat.fr/data/dosleg/dosleg.zip",
                Path.Combine(Directory.GetCurrentDirectory(), "data", "senat", "dosleg"));
            doslegSql = Path.Combine(dossier, "dosleg.sql");
        }

        var textes = new TexteSource().LireTextes(doslegSql);
        Console.WriteLine($"  {textes.Count} textes lus.");
        await repo.EcrireTextesAsync(textes);

        // Décisions du Sénat en séance (DOSLEG), avec ou sans scrutin public : écrites AVANT le rattachement (la date d'une décision aide à
        // départager les textes de même titre), liées aux scrutins APRÈS.
        var etapesSenat = new TexteSource().LireDecisionsSenat(doslegSql);
        await repo.EcrireEtapesSenatAsync(etapesSenat);

        var (rattaches, ambigus, sans) = await repo.RattacherScrutinsAsync();
        Console.WriteLine($"  Scrutins sur l'ensemble : {rattaches} rattachés, {ambigus} ambigus (laissés sans texte), {sans} sans texte.");

        var (ecrites, liees, sansScrutin) = await repo.RelierEtapesSenatAsync();
        Console.WriteLine($"  Décisions du Sénat : {ecrites} retenues sur {etapesSenat.Count} lues ({liees} avec scrutin public, {sansScrutin} sans scrutin public).");


        await repo.TerminerAsync(runId, true, rattaches, textes.Count, $"{ambigus} ambigus, {sans} sans texte");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex);
        await repo.TerminerAsync(runId, false, 0, 0, ex.Message);
        return 1;
    }
}

static async Task<(string Scrutins, string Amo)> ResoudreDossiersAnAsync(int legislature, string? scrutinsDir, string? amoDir)
{
    if (scrutinsDir is not null && amoDir is not null)
    {
        // Les zips s'extraient dans un sous-dossier « json » : accepter les deux formes.
        return (Path.Combine(scrutinsDir, Directory.Exists(Path.Combine(scrutinsDir, "json")) ? "json" : ""),
                Path.Combine(amoDir, Directory.Exists(Path.Combine(amoDir, "json")) ? "json" : ""));
    }

    var data = Path.Combine(Directory.GetCurrentDirectory(), "data", "an", $"L{legislature}");
    Directory.CreateDirectory(data);
    var racine = $"https://data.assemblee-nationale.fr/static/openData/repository/{legislature}";

    var s = scrutinsDir ?? Path.Combine(
        await Telechargement.TelechargerZipAsync($"{racine}/loi/scrutins/{FichierScrutins(legislature)}", Path.Combine(data, "scrutins")), "json");
    var a = amoDir ?? Path.Combine(
        await Telechargement.TelechargerZipAsync(
            $"{racine}/amo/tous_acteurs_mandats_organes_xi_legislature/AMO30_tous_acteurs_tous_mandats_tous_organes_historique.json.zip",
            Path.Combine(data, "amo")), "json");
    return (s, a);
}

// Les fichiers de scrutins des anciennes législatures portent un suffixe en chiffres romains.
static string FichierScrutins(int legislature) =>
    legislature >= 16 ? "Scrutins.json.zip" : $"Scrutins_{Romain(legislature)}.json.zip";

static string Romain(int n) => n switch
{
    14 => "XIV", 15 => "XV", 16 => "XVI", 17 => "XVII",
    _ => throw new ArgumentOutOfRangeException(nameof(n), $"Législature {n} non prise en charge."),
};
