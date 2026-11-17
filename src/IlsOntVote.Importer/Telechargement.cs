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

    /// <summary>
    /// Téléchargement par tranches de 1 Mo (requêtes <c>Range</c>), chaque tranche réessayée seule. Pourquoi : le serveur de l'Assemblée
    /// sert parfois un fichier en flux continu à ~2 Ko/s (le « Dossiers législatifs » de 11 Mo mettait plus d'une heure) ou coupe
    /// la connexion en route (« The response ended prematurely »), alors que les tranches arrivent à ~1 Mo/s. Renvoie false (sans rien
    /// écrire de définitif) si le serveur ne gère pas les tranches : l'appelant retombe sur le flux continu.
    /// </summary>
    private static async Task<bool> EssayerParTranchesAsync(HttpClient http, string url, string destination)
    {
        try
        {
            using var tete = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, url));
            if (!tete.IsSuccessStatusCode || tete.Content.Headers.ContentLength is not long total || total <= Tranche) return false;

            await using var fs = File.Create(destination);
            for (long debut = 0; debut < total; debut += Tranche)
            {
                var fin = Math.Min(debut + Tranche, total) - 1;
                byte[]? donnees = null;
                for (var essai = 1; essai <= EssaisMax && donnees is null; essai++)
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
                        Console.Error.WriteLine($"  tranche {debut / Tranche}: échec ({ex.Message}), essai {essai}/{EssaisMax}");
                    }
                    if (donnees is null) await Task.Delay(TimeSpan.FromSeconds(2 * essai));
                }
                if (donnees is null) throw new IOException($"Tranche {debut / Tranche} introuvable après {EssaisMax} essais : {url}");
                await fs.WriteAsync(donnees);
            }
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
