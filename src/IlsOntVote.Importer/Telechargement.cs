using System.IO.Compression;

namespace IlsOntVote.Importer;

/// <summary>Téléchargement des jeux de données publics (AN, Sénat). Les zips arrivent de sources externes : ils sont extraits dans un dossier dédié, jamais exécutés.</summary>
public static class Telechargement
{
    /// <summary>
    /// Identité envoyée à toutes les sources : fixe et honnête (le projet et son site), jamais déguisée ni changeante. Une source gênée par notre trafic
    /// sait ainsi qui contacter ; certains serveurs refusent en outre les requêtes sans User-Agent.
    /// </summary>
    public const string UserAgent = "IlsOntVote/1.0 (+https://www.ilsontvote.org)";

    /// <summary>Client HTTP commun : User-Agent fixe et délai donné.</summary>
    public static HttpClient CreerClient(TimeSpan delai)
    {
        var http = new HttpClient { Timeout = delai };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return http;
    }

    /// <summary>
    /// Requête conditionnelle : compare l'ETag du serveur (requête HEAD, sans télécharger le fichier) à celui enregistré après le dernier traitement
    /// réussi (<c>destination + ".etag"</c>). Retourne (false, etag) si le fichier n'a pas changé. Sans ETag ou sans fichier local : (true, …), on télécharge.
    /// L'ETag n'est enregistré qu'APRÈS un traitement réussi (<see cref="EnregistrerEtag"/>) : un import en échec ne doit pas faire sauter le suivant.
    /// </summary>
    public static async Task<(bool Modifie, string? Etag)> VerifierEtagAsync(string url, string destination)
    {
        try
        {
            using var http = CreerClient(TimeSpan.FromMinutes(1));
            using var tete = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, url));
            var etag = tete.IsSuccessStatusCode ? tete.Headers.ETag?.Tag : null;
            if (etag is null || !File.Exists(destination)) return (true, etag);
            var connu = File.Exists(destination + ".etag") ? File.ReadAllText(destination + ".etag").Trim() : null;
            return (connu != etag, etag);
        }
        catch (HttpRequestException) { return (true, null); }   // HEAD refusé : on télécharge, comme avant
    }

    /// <summary>Enregistre l'ETag d'un fichier traité avec succès (cf. <see cref="VerifierEtagAsync"/>).</summary>
    public static void EnregistrerEtag(string destination, string? etag)
    {
        if (etag is not null) File.WriteAllText(destination + ".etag", etag);
    }

    /// <summary>Nombre d'essais par fichier (« The response ended prematurely » observé depuis la VM sur les gros zips de l'AN).</summary>
    private const int EssaisMax = 6;

    /// <summary>Télécharge une URL vers un fichier, avec plusieurs essais (les coupures réseau existent).</summary>
    public static async Task TelechargerFichierAsync(string url, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var http = CreerClient(TimeSpan.FromMinutes(10));

        // D'abord par tranches (plus rapide et plus fiable quand le serveur le permet), sinon flux continu ci-dessous.
        if (await EssayerParTranchesAsync(http, url, destination)) return;

        for (var essai = 1; ; essai++)
        {
            // Les coupures réseau en cours de transfert existent (observé sur le jeu de la 16e législature) : 3 essais.
            Console.WriteLine($"Téléchargement : {url}" + (essai > 1 ? $" (essai {essai})" : ""));
            try
            {
                using var rep = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                rep.EnsureSuccessStatusCode();
                await using var fs = File.Create(destination);
                await rep.Content.CopyToAsync(fs);
                return;
            }
            catch (Exception ex) when (essai < EssaisMax && ex is HttpRequestException or IOException or TaskCanceledException)
            {
                Console.Error.WriteLine($"  échec : {ex.Message}");
                await Task.Delay(TimeSpan.FromSeconds(10 * essai)); // attente croissante : le serveur de l'AN coupe parfois les gros transferts
            }
        }
    }

    private const int Tranche = 1 << 20; // 1 Mo

    /// <summary>Au-delà de cette taille, les tranches passent à <see cref="GrosseTranche"/> : 365 Mo en tranches de 1 Mo font 365 requêtes, assez pour que le serveur de l'Assemblée en coupe.</summary>
    private const long SeuilGrosFichier = 50L << 20;
    private const int GrosseTranche = 4 << 20; // 4 Mo

    /// <summary>
    /// Essais par tranche avant de renoncer aux tranches pour le flux continu. Raison : le fichier « Amendements » de la 16e législature (346 Mo) répond
    /// « 206 Partial Content » avec un CORPS VIDE à toute plage au-delà des premiers Mo (constaté avec curl, hors de notre code) alors que le flux
    /// continu fonctionne ; insister 10 fois par tranche ne sert à rien, on bascule vite.
    /// </summary>
    private const int EssaisTranche = 4;

    /// <summary>Attente avant le nouvel essai d'une tranche : 5 s, 10 s, 15 s… plafonnée à 60 s (observé : un blocage de quelques dizaines de secondes sur la VM).</summary>
    private static TimeSpan AttenteTranche(int essai) => TimeSpan.FromSeconds(Math.Min(60, 5 * essai));

    /// <summary>
    /// Téléchargement par tranches (requêtes <c>Range</c>), chaque tranche réessayée seule. Pourquoi : le serveur de l'Assemblée
    /// sert parfois un fichier en flux continu à ~2 Ko/s (le « Dossiers législatifs » de 11 Mo mettait plus d'une heure) ou coupe
    /// la connexion en route (« The response ended prematurely », « Error while copying content to a stream »), alors que les tranches
    /// arrivent à ~1 Mo/s. Les fichiers de plus de 50 Mo (amendements, ~365 Mo) utilisent des tranches de 4 Mo.
    /// REPRISE : le fichier est écrit dans <c>destination + ".part"</c> ; si le processus s'arrête en route (redémarrage, arrêt depuis /Admin), ce fichier
    /// est conservé avec l'ETag et la taille du fichier distant (<c>.part.meta</c>) et le prochain essai reprend après le dernier octet reçu, tant que
    /// le fichier distant n'a pas changé. Renvoie false (sans rien écrire de définitif) si le serveur ne gère pas les tranches : l'appelant retombe sur le flux continu.
    /// </summary>
    private static async Task<bool> EssayerParTranchesAsync(HttpClient http, string url, string destination)
    {
        var part = destination + ".part";
        var meta = destination + ".part.meta";
        try
        {
            using var tete = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, url));
            if (!tete.IsSuccessStatusCode || tete.Content.Headers.ContentLength is not long total || total <= Tranche) return false;
            var taille = total > SeuilGrosFichier ? GrosseTranche : Tranche;
            var signature = $"{total}|{tete.Headers.ETag?.Tag}";

            // Reprise seulement si le fichier distant est le même (taille + ETag) que celui du téléchargement interrompu.
            long debut = 0;
            if (File.Exists(part) && File.Exists(meta) && File.ReadAllText(meta).Trim() == signature)
            {
                debut = new FileInfo(part).Length;
                if (debut >= total) debut = 0;
                else Console.WriteLine($"  reprise du téléchargement à {debut / 1048576.0:0.0} Mo sur {total / 1048576.0:0.0} Mo.");
            }
            File.WriteAllText(meta, signature);

            await using (var fs = new FileStream(part, debut == 0 ? FileMode.Create : FileMode.Open, FileAccess.Write))
            {
                fs.Seek(debut, SeekOrigin.Begin);
                for (; debut < total; debut += taille)
                {
                    var fin = Math.Min(debut + taille, total) - 1;
                    byte[]? donnees = null;
                    for (var essai = 1; essai <= EssaisTranche && donnees is null; essai++)
                    {
                        try
                        {
                            using var req = new HttpRequestMessage(HttpMethod.Get, url);
                            req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(debut, fin);
                            using var rep = await http.SendAsync(req);
                            if (rep.StatusCode != System.Net.HttpStatusCode.PartialContent) return false; // tranches non gérées : flux continu
                            var octets = await rep.Content.ReadAsByteArrayAsync();
                            if (octets.Length == fin - debut + 1) donnees = octets;
                        }
                        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
                        {
                            Console.Error.WriteLine($"  tranche à {debut / 1048576.0:0.0} Mo : échec ({ex.Message}), essai {essai}/{EssaisTranche}");
                        }
                        if (donnees is null) await Task.Delay(AttenteTranche(essai));
                    }
                    // Échec persistant des tranches : on abandonne CETTE méthode (le .part est supprimé) et l'appelant télécharge en flux continu.
                    if (donnees is null)
                    {
                        Console.Error.WriteLine($"  tranche à {debut / 1048576.0:0.0} Mo toujours en échec après {EssaisTranche} essais : bascule en téléchargement continu.");
                        fs.Close();
                        File.Delete(part); File.Delete(meta);
                        return false;
                    }
                    await fs.WriteAsync(donnees);
                }
            }
            File.Move(part, destination, overwrite: true);
            File.Delete(meta);
            Console.WriteLine($"  (téléchargé par tranches : {total / 1048576.0:0.0} Mo)");
            return true;
        }
        catch (HttpRequestException) { return false; } // HEAD refusé, etc. : on laisse le flux continu essayer
    }

    /// <summary>Télécharge un zip dans un dossier vidé au préalable, l'extrait, et retourne le dossier.</summary>
    public static async Task<string> TelechargerZipAsync(string url, string dossier)
    {
        if (Directory.Exists(dossier)) Directory.Delete(dossier, recursive: true);
        Directory.CreateDirectory(dossier);
        var zip = Path.Combine(dossier, "source.zip");
        await TelechargerFichierAsync(url, zip);
        ZipFile.ExtractToDirectory(zip, dossier);
        File.Delete(zip);
        return dossier;
    }
}
