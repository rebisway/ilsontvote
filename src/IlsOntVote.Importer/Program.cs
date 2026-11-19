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

// Sortie en UTF-8 : le planificateur capture la console de chaque tâche pour le journal de /Admin (sinon accents illisibles sous Windows).
Console.OutputEncoding = System.Text.Encoding.UTF8;

var argv = args.ToList();
if (argv.Count == 0 || argv[0] is not ("an" or "senat" or "textes" or "ue" or "stats" or "dossiers" or "traductions" or "export" or "petitions" or "planificateur" or "amendements" or "communes" or "lords" or "bills"))
{
    Console.Error.WriteLine("Usage : IlsOntVote.Importer an [--legislature 17|15,16,17|all] [--scrutins-dir D --amo-dir D]");
    Console.Error.WriteLine("        IlsOntVote.Importer senat [--senateurs-dir D --dosleg-sql F]");
    Console.Error.WriteLine("        IlsOntVote.Importer planificateur   (exécute les demandes de /Admin et le planning ; lancé chaque minute par systemd)");
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
    "petitions" => await ImporterPetitionsAsync(),
    "planificateur" => await new Planificateur(connectionString).PasserAsync(),
    "amendements" => await ImporterAmendementsAsync(),
    "communes" => await ImporterCommunesAsync(),
    "lords" => await ImporterLordsAsync(),
    "bills" => await ImporterBillsRoyaumeUniAsync(),
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
/// Chambre des Communes (Royaume-Uni), législature en cours (59e, depuis le 9 juillet 2024) ou, avec <c>--legislature N</c> (57, 58), une législature TERMINÉE
/// (historique : mandats figés, une seule écriture des mandats à la fin), via l'API publique des divisions. Incrémental : une division
/// déjà en base n'est relue que si elle a moins de 7 jours (l'API corrige listes et décomptes après publication). <c>--limit N</c> pour tester.
/// PROTOTYPE : la chambre est déclarée inactive dans <c>chambres</c> (migration 030) et le site n'a pas encore de pages pour elle ; ne pas planifier en production.
/// </summary>
async Task<int> ImporterCommunesAsync()
{
    Console.WriteLine("=== Chambre des Communes (Royaume-Uni) ===");
    var runId = await repo.DemarrerAsync("COMMONS");
    try
    {
        using var source = new CommonsSource();
        var limite = int.TryParse(Option("--limit"), out var l) ? l : int.MaxValue;
        var numLeg = int.TryParse(Option("--legislature"), out var nl) ? nl : CommonsSource.Legislature;
        var leg = CommonsSource.Legislatures.FirstOrDefault(x => x.Numero == numLeg)
                  ?? throw new ArgumentException($"Législature {numLeg} inconnue (connues : {string.Join(", ", CommonsSource.Legislatures.Select(x => x.Numero))}).");
        var terminee = leg.Fin is not null;
        var liste = await source.ListerAsync(leg.Debut, leg.Fin);
        var (deja, _) = await repo.LireScrutinsAsync(Chambres.Commons);
        var seuil = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-7);
        // Du plus ancien au plus récent : la reconstruction des mandats suit l'ordre du temps.
        var aLire = liste.Where(d => !deja.Contains(CommonsSource.IdScrutin(d.Id)) || d.Date >= seuil)
            .OrderBy(d => d.Date).ThenBy(d => d.Id).Take(limite).ToList();
        Console.WriteLine($"  {liste.Count} divisions depuis {leg.Debut:yyyy-MM-dd} (législature {leg.Numero}), {deja.Count} déjà en base, {aLire.Count} à lire.");

        var existants = await repo.LireSlugsAsync();
        var slugs = existants.Values.ToHashSet();
        var groupes = new Dictionary<string, GroupeRow>();
        var parlementaires = new Dictionary<string, ParlementaireRow>();
        int nbScrutins = 0; long nbVotes = 0;
        // Législature terminée : les « vus » s'accumulent sur TOUS les lots et les mandats s'écrivent une fois à la fin (début = premier lot, fin = dernier).
        var vusTotal = new Dictionary<string, (DateOnly Premier, DateOnly Dernier, string GroupeId)>();

        foreach (var lot in aLire.Chunk(25))
        {
            var scrutins = new List<ScrutinAvecVotes>();
            var vus = terminee ? vusTotal : new Dictionary<string, (DateOnly Premier, DateOnly Dernier, string GroupeId)>();
            foreach (var (id, _) in lot)
            {
                var division = await source.LireAsync(id);
                scrutins.Add(CommonsSource.VersScrutin(division));
                // Tous les membres en exercice figurent dans l'une des trois listes : voté pour, contre, ou « sans vote enregistré ».
                foreach (var m in division.Pour.Concat(division.Contre).Concat(division.SansVote))
                {
                    var gid = CommonsSource.IdGroupe(m.PartiSigle);
                    groupes[gid] = new GroupeRow(gid, Chambres.Commons, leg.Numero, m.PartiSigle, m.PartiLibelle, m.Couleur, CommonsSource.OrdreParti(m.PartiSigle));
                    var pid = CommonsSource.IdMembre(m.MemberId);
                    if (!parlementaires.ContainsKey(pid))
                    {
                        var slug = existants.TryGetValue(pid, out var s0) ? s0 : Slugs.Unique(slugs, m.Prenom, m.Nom, pid);
                        parlementaires[pid] = new ParlementaireRow(pid, Chambres.Commons, null, m.Prenom, m.Nom, slug, null, m.Circonscription);
                    }
                    vus[pid] = vus.TryGetValue(pid, out var v)
                        ? (division.Date < v.Premier ? division.Date : v.Premier, division.Date >= v.Dernier ? division.Date : v.Dernier, division.Date >= v.Dernier ? gid : v.GroupeId)
                        : (division.Date, division.Date, gid);
                }
                await Task.Delay(250);   // politesse envers une API publique partagée
            }
            await repo.EcrireMembresUeAsync(groupes.Values, parlementaires.Values);   // upsert générique (le nom « Ue » est historique)
            if (!terminee) await repo.InitialiserGouvernementAsync(Chambres.Commons, CommonsSource.GroupeGouvernement);
            var annuaire = new Annuaire(groupes.Values.ToList(), parlementaires.Values.ToList(), [], []);
            var (_, ancienPlusRecent) = await repo.LireScrutinsAsync(Chambres.Commons);
            var (n, v2, _) = await repo.EcrireScrutinsAsync(scrutins, annuaire);
            if (!terminee) await repo.EcrireMandatsDeduitsAsync(Chambres.Commons, leg.Numero, vus, ancienPlusRecent);
            nbScrutins += n; nbVotes += v2;
            Console.WriteLine($"  {nbScrutins}/{aLire.Count} divisions lues…");
        }
        if (terminee && vusTotal.Count > 0) await repo.EcrireMandatsDeduitsAsync(Chambres.Commons, leg.Numero, vusTotal, null, terminee: true);

        Console.WriteLine($"  {nbScrutins} divisions, {nbVotes} votes, {parlementaires.Count} députés.");
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

/// <summary>
/// Chambre des Lords (Royaume-Uni) : législature en cours, ou <c>--legislature N</c> (57, 58) pour l'historique. Une exécution : divisions (avec leurs votants), pairs de la
/// période (annuaire daté : mandats donc absents déduits), puis écriture. Incrémental : une division déjà en base n'est relue que si elle a moins de 7 jours.
/// </summary>
async Task<int> ImporterLordsAsync()
{
    Console.WriteLine("=== Chambre des Lords (Royaume-Uni) ===");
    var runId = await repo.DemarrerAsync("LORDS");
    try
    {
        using var source = new LordsSource();
        var limite = int.TryParse(Option("--limit"), out var l) ? l : int.MaxValue;
        var numLeg = int.TryParse(Option("--legislature"), out var nl) ? nl : CommonsSource.Legislature;
        var leg = CommonsSource.Legislatures.FirstOrDefault(x => x.Numero == numLeg)
                  ?? throw new ArgumentException($"Législature {numLeg} inconnue (connues : {string.Join(", ", CommonsSource.Legislatures.Select(x => x.Numero))}).");
        var (deja, plusRecent) = await repo.LireScrutinsAsync(Chambres.Lords);
        var seuil = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-7);
        // Passage courant : on ne relit que les dernières semaines (la source rend toutes les divisions d'une période en une seule série de pages) ; premier import ou historique : toute la période.
        var depuis = leg.Fin is null && plusRecent is { } pr && pr >= leg.Debut ? (pr.AddDays(-8) > leg.Debut ? pr.AddDays(-8) : leg.Debut) : leg.Debut;
        var divisions = (await source.ListerDivisionsAsync(depuis, leg.Fin))
            .Where(d => !deja.Contains(LordsSource.IdScrutin(d.Id)) || d.Date >= seuil).OrderBy(d => d.Date).ThenBy(d => d.Id).Take(limite).ToList();
        Console.WriteLine($"  {divisions.Count} divisions à écrire depuis {depuis:yyyy-MM-dd} (législature {leg.Numero}).");

        var fin = leg.Fin ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var pairs = (await source.ListerPairsAsync(leg.Debut, fin)).GroupBy(p => p.Id).Select(g => g.First()).ToDictionary(p => p.Id);
        Console.WriteLine($"  {pairs.Count} pairs en exercice sur la période.");

        // Un votant absent de l'annuaire (donnée manquante côté membres) reçoit un mandat réduit à ses votes observés : jamais de vote sans siège.
        foreach (var grp in divisions.SelectMany(d => d.Content.Concat(d.NonContent).Select(v => (v, d.Date))).GroupBy(x => x.v.MemberId))
            if (!pairs.ContainsKey(grp.Key))
            {
                var v = grp.First().v;
                pairs[grp.Key] = new PairLords(grp.Key, v.Nom, v.Sigle, v.Parti, v.Couleur, grp.Min(x => x.Date), grp.Max(x => x.Date));
            }

        var existants = await repo.LireSlugsAsync();
        var slugs = existants.Values.ToHashSet();
        var groupes = new Dictionary<string, GroupeRow>();
        var parlementaires = new List<ParlementaireRow>();
        foreach (var p in pairs.Values)
        {
            var pid = LordsSource.IdMembre(p.Id);
            // Titre inclus dans le nom (« Baroness Adams of Craigielea ») : un pair est désigné par son titre ; prénom vide, adresse « baroness-adams-of-craigielea ».
            var slug = existants.TryGetValue(pid, out var s0) ? s0 : Slugs.Unique(slugs, "", p.Nom, pid);
            parlementaires.Add(new ParlementaireRow(pid, Chambres.Lords, null, "", p.Nom, slug, null, null));
            if (p.Sigle is { } sg)
            {
                var gid = LordsSource.IdGroupe(sg);
                groupes[gid] = new GroupeRow(gid, Chambres.Lords, null, sg, p.Parti ?? sg, p.Couleur, CommonsSource.OrdreParti(sg));
            }
        }
        foreach (var d in divisions)
            foreach (var v in d.Content.Concat(d.NonContent))
            {
                var gid = LordsSource.IdGroupe(v.Sigle);
                if (!groupes.ContainsKey(gid)) groupes[gid] = new GroupeRow(gid, Chambres.Lords, null, v.Sigle, v.Parti, v.Couleur, CommonsSource.OrdreParti(v.Sigle));
            }

        await repo.EcrireMembresUeAsync(groupes.Values, parlementaires);   // upsert générique (le nom « Ue » est historique)
        if (leg.Fin is null) await repo.InitialiserGouvernementAsync(Chambres.Lords, LordsSource.GroupeGouvernement);
        await repo.EcrirePairsAsync(pairs.Values.Select(p => (LordsSource.IdMembre(p.Id), p.Debut, p.Fin, p.Sigle is { } sg ? LordsSource.IdGroupe(sg) : null)).ToList());

        var annuaire = new Annuaire(groupes.Values.ToList(), parlementaires, [], []);
        int nbScrutins = 0; long nbVotes = 0;
        foreach (var lot in divisions.Chunk(50))
        {
            var (n, v2, _) = await repo.EcrireScrutinsAsync(lot.Select(LordsSource.VersScrutin).ToList(), annuaire);
            nbScrutins += n; nbVotes += v2;
            Console.WriteLine($"  {nbScrutins}/{divisions.Count} divisions écrites…");
        }
        Console.WriteLine($"  {nbScrutins} divisions, {nbVotes} votes, {pairs.Count} pairs.");
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

/// <summary>
/// Projets de loi du Parlement britannique (<c>bills</c>) : liste des sessions courantes, puis étapes des seuls projets nouveaux ou modifiés depuis le dernier passage, puis rattachement
/// des divisions des Communes à leur projet (par le titre). À lancer APRÈS « communes ». <c>--force</c> relit les étapes de tous les projets.
/// </summary>
async Task<int> ImporterBillsRoyaumeUniAsync()
{
    Console.WriteLine("=== Projets de loi du Parlement britannique ===");
    var runId = await repo.DemarrerAsync("BILLS-UK");
    try
    {
        // --rattacher : seulement le rattachement des divisions (calcul en base, sans appel réseau) : après un changement de la règle de titres.
        if (argv.Contains("--rattacher"))
        {
            var (r, t) = await repo.RattacherDivisionsCommonsAsync();
            Console.WriteLine($"  {r}/{t} divisions rattachées à un projet (d'après le titre).");
            await repo.TerminerAsync(runId, true, 0, r, $"{r}/{t} divisions rattachées (rattachement seul)");
            return 0;
        }
        using var source = new BillsRoyaumeUniSource();
        var liste = await source.ListerAsync();
        var connus = await repo.LireBillsMajAsync();
        var force = argv.Contains("--force");
        var aLire = liste.Where(b =>
        {
            var id = b.GetProperty("billId").GetInt64();
            DateTime? maj = b.TryGetProperty("lastUpdate", out var m) && m.ValueKind == System.Text.Json.JsonValueKind.String
                && DateTime.TryParse(m.GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var d) ? d : null;
            return force || !connus.TryGetValue(id, out var stocke) || stocke is null || maj is null || maj > stocke;
        }).ToList();
        Console.WriteLine($"  {liste.Count} projets dans les sessions courantes, {connus.Count} déjà en base, {aLire.Count} à lire.");
        var n = 0;
        foreach (var resume in aLire)
        {
            await repo.EcrireBillAsync(await source.LireAsync(resume));
            if (++n % 50 == 0) Console.WriteLine($"  {n}/{aLire.Count} projets lus…");
            await Task.Delay(150);
        }
        var (rattachees, total) = await repo.RattacherDivisionsCommonsAsync();
        Console.WriteLine($"  {n} projets écrits ; {rattachees}/{total} divisions rattachées à un projet (d'après le titre).");
        await repo.TerminerAsync(runId, true, n, rattachees, $"{rattachees}/{total} divisions rattachées");
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
/// Pétitions du Parlement britannique (<c>petitions --source uk</c>) : un relevé par jour (ouvertes + clôturées, 330 pages de l'API), historique des soutiens et détection des baisses
/// par le même code que les pétitions françaises (<c>EcrireReleveAsync</c>, plateforme « UK »). Pas d'archives : l'historique commence au premier import. Rien n'est écrit si rien n'a
/// changé depuis le dernier relevé (empreinte) ; <c>--force</c> écrit quand même.
/// </summary>
async Task<int> ImporterPetitionsRoyaumeUniAsync()
{
    Console.WriteLine("=== Pétitions du Parlement britannique ===");
    var runId = await repo.DemarrerAsync("PETITIONS-UK");
    try
    {
        using var source = new PetitionsRoyaumeUniSource();
        var lignes = await source.LireToutesAsync(Console.WriteLine);
        var sha = PetitionsRoyaumeUniSource.Empreinte(lignes);
        if (!argv.Contains("--force") && sha == await repo.DernierShaAsync(PetitionsRoyaumeUniSource.Plateforme))
        {
            Console.WriteLine($"  {lignes.Count} pétitions lues, inchangées depuis le dernier relevé : rien à écrire.");
            await repo.TerminerAsync(runId, true, 0, 0, "inchangé depuis le dernier relevé");
            return 0;
        }
        var (n, anomalies) = await repo.EcrireReleveAsync(PetitionsRoyaumeUniSource.Plateforme, lignes, DateTime.UtcNow.Date, majFiche: true, sha);
        Console.WriteLine($"  {lignes.Count} pétitions lues, {n} écrites ; {anomalies} anomalie(s) sur ce relevé.");
        await repo.TerminerAsync(runId, true, n, anomalies, null);
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
/// Amendements déposés à l'Assemblée, par dossier législatif (jeu officiel « Amendements », ~365 Mo par législature). Par défaut la législature
/// en cours ; <c>--legislature 16,17</c> ou <c>all</c> pour les deux. Le fichier n'est retéléchargé que si son ETag a changé (planning plusieurs
/// fois par jour sans coût) ; <c>--force</c> ignore l'ETag. Après traitement le zip est remplacé par un fichier vide : seul l'ETag compte, le disque
/// ne garde pas 365 Mo de plus.
/// </summary>
async Task<int> ImporterAmendementsAsync()
{
    var option = Option("--legislature") ?? LegislatureEnCours.ToString();
    int[] legislatures = option == "all" ? AmendementsSource.LegislaturesDisponibles
        : option.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(int.Parse).ToArray();
    var force = argv.Contains("--force");
    var codeRetour = 0;
    foreach (var legislature in legislatures)
    {
        Console.WriteLine($"=== Amendements — législature {legislature} ===");
        var runId = await repo.DemarrerAsync($"AMENDEMENTS-L{legislature}");
        try
        {
            if (!AmendementsSource.LegislaturesDisponibles.Contains(legislature))
                throw new InvalidOperationException($"Législature {legislature} absente du jeu « Amendements » (publié pour la 16e et la 17e).");
            var url = AmendementsSource.Url(legislature);
            var zip = Path.Combine(Directory.GetCurrentDirectory(), "data", "an", $"amendements-L{legislature}", "Amendements.json.zip");
            var (modifie, etag) = force ? (true, null) : await Telechargement.VerifierEtagAsync(url, zip);
            if (!modifie)
            {
                Console.WriteLine("  fichier inchangé (ETag) : rien à télécharger.");
                await repo.TerminerAsync(runId, true, 0, 0, "fichier source inchangé (ETag)");
                continue;
            }
            Console.WriteLine("  téléchargement (~365 Mo)…");
            await Telechargement.TelechargerFichierAsync(url, zip);
            var dossiers = AmendementsSource.Lire(zip, out var sansDossier, out var illisibles);
            var deposes = dossiers.Sum(d => d.Deposes);
            await repo.EcrireAmendementsAsync(dossiers, legislature);
            var message = $"{deposes:N0} amendements, {dossiers.Count} dossiers" +
                          (sansDossier > 0 ? $" ; {sansDossier} fichiers ignorés (rangés par la source dans « incorrect_data », sans dossier)" : "") +
                          (illisibles > 0 ? $" ; {illisibles} fichiers illisibles ignorés" : "");
            Console.WriteLine("  " + message + ".");
            // Le zip est remplacé par un fichier vide (voir le résumé de la méthode) puis l'ETag est enregistré : seulement après un succès.
            File.WriteAllBytes(zip, []);
            Telechargement.EnregistrerEtag(zip, etag ?? (await Telechargement.VerifierEtagAsync(url, zip)).Etag);
            await repo.TerminerAsync(runId, true, 0, 0, message);
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
/// Pétitions (Sénat et Assemblée nationale) : télécharge le CSV de data.gouv.fr de chaque plateforme (mis à jour chaque lundi à la source), l'écrit avec
/// son historique et détecte les anomalies (baisse de soutiens, disparition). Un fichier identique au précédent (même empreinte) n'ajoute pas de relevé.
/// <c>--force</c> écrit même si le fichier est identique au précédent (remise à jour des fiches). <c>--source senat|an</c> limite à une plateforme. <c>--archives</c> rattrape d'abord les fichiers hebdomadaires archivés par data.gouv.fr
/// (Sénat depuis mars 2026, Assemblée depuis juillet 2025), dans l'ordre chronologique ; chaque archive est supprimée après traitement (10 Mo l'une pour l'AN).
/// </summary>
async Task<int> ImporterPetitionsAsync()
{
    if (Option("--source") == "uk") return await ImporterPetitionsRoyaumeUniAsync();
    Console.WriteLine("=== Pétitions ===");
    var runId = await repo.DemarrerAsync("PETITIONS");
    try
    {
        var demande = Option("--source");
        var sources = new[] { PetitionsSource.Senat, PetitionsSource.Assemblee }
            .Where(x => demande is null || (demande == "an" && x.Chambre == "AN") || (demande == "senat" && x.Chambre == "SENAT")).ToList();
        int total = 0, anomaliesTotal = 0;

        foreach (var source in sources)
        {
            Console.WriteLine($"--- {source.Nom} ---");
            var dossier = Path.Combine(Directory.GetCurrentDirectory(), "data", "petitions", source.Chambre.ToLowerInvariant());

            if (argv.Contains("--archives"))
            {
                using var http = Telechargement.CreerClient(TimeSpan.FromMinutes(2));
                var index = await http.GetStringAsync(source.UrlArchives);
                // Noms de la forme AAAA-MM-JJ_petitions.csv ; l'ordre chronologique est indispensable (chaque relevé est comparé au précédent).
                var fichiers = System.Text.RegularExpressions.Regex.Matches(index, @"(\d{4}-\d{2}-\d{2})_petitions\.csv")
                    .Select(m => m.Groups[1].Value).Distinct().Order().ToList();
                Console.WriteLine($"  {fichiers.Count} archives hebdomadaires.");
                foreach (var jour in fichiers)
                {
                    var date = DateTime.Parse(jour, System.Globalization.CultureInfo.InvariantCulture);
                    if (await repo.ReleveExisteAsync(source.Chambre, date)) continue;   // déjà traité
                    var chemin = Path.Combine(dossier, "archives", jour + "_petitions.csv");
                    await Telechargement.TelechargerFichierAsync(source.UrlArchives + jour + "_petitions.csv", chemin);
                    var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(chemin))).ToLowerInvariant();
                    // Une archive plus récente que tout relevé déjà traité met la fiche à jour (traitement chronologique : la dernière archive donne l'état
                    // courant) ; une archive plus ancienne que l'existant ne doit jamais l'écraser. Sans cela, le fichier du jour, identique à la
                    // dernière archive, est jugé « inchangé » et les fiches resteraient à la valeur de leur première apparition.
                    var dernier = await repo.DernierReleveAsync(source.Chambre);
                    var (_, an) = await repo.EcrireReleveAsync(source.Chambre, PetitionsSource.Lire(chemin, source), date, majFiche: dernier is null || date >= dernier, sha);
                    File.Delete(chemin);
                    anomaliesTotal += an;
                    Console.WriteLine($"  archive {jour} : {an} anomalie(s).");
                }
            }

            var fichier = Path.Combine(dossier, "petitions.csv");
            // Requête conditionnelle : si l'ETag du serveur est celui du dernier traitement réussi, on ne télécharge rien (les sources changent le lundi).
            var (modifie, etag) = argv.Contains("--force") ? (true, null) : await Telechargement.VerifierEtagAsync(source.Url, fichier);
            if (!modifie) { Console.WriteLine("  fichier inchangé (ETag) : rien à télécharger."); continue; }
            await Telechargement.TelechargerFichierAsync(source.Url, fichier);
            var shaCourant = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(fichier))).ToLowerInvariant();
            if (!argv.Contains("--force") && shaCourant == await repo.DernierShaAsync(source.Chambre))
            {
                Console.WriteLine("  fichier inchangé depuis le dernier relevé : rien à écrire.");
                Telechargement.EnregistrerEtag(fichier, etag);
                continue;
            }
            var lignes = PetitionsSource.Lire(fichier, source);
            var (n, anomalies) = await repo.EcrireReleveAsync(source.Chambre, lignes, DateTime.UtcNow.Date, majFiche: true, shaCourant);
            Telechargement.EnregistrerEtag(fichier, etag);   // après un traitement réussi seulement
            total += n; anomaliesTotal += anomalies;
            Console.WriteLine($"  {lignes.Count} pétitions lues, {n} écrites ; {anomalies} anomalie(s) sur ce relevé.");
        }
        Console.WriteLine($"  Total : {total} pétitions écrites, {anomaliesTotal} anomalie(s) détectée(s).");
        await repo.TerminerAsync(runId, true, total, anomaliesTotal, null);
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
