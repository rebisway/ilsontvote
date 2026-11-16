using System.IO.Compression;

namespace IlsOntVote.Importer;

/// <summary>Téléchargement des jeux de données publics (AN, Sénat). Les zips arrivent de sources externes : ils sont extraits dans un dossier dédié, jamais exécutés.</summary>
public static class Telechargement
{
    /// <summary>Nombre d'essais par fichier (« The response ended prematurely » observé depuis la VM sur les gros zips de l'AN).</summary>
    private const int EssaisMax = 6;

    /// <summary>Télécharge une URL vers un fichier, avec plusieurs essais (les coupures réseau existent).</summary>
    public static async Task TelechargerFichierAsync(string url, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

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
