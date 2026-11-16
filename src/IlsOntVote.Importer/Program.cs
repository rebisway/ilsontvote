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
if (argv.Count == 0 || argv[0] is not ("an" or "senat" or "textes" or "ue"))
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
            foreach (var (id, _) in lot)
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

        var (rattaches, ambigus, sans) = await repo.RattacherScrutinsAsync();
        Console.WriteLine($"  Scrutins sur l'ensemble : {rattaches} rattachés, {ambigus} ambigus (laissés sans texte), {sans} sans texte.");

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
