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
