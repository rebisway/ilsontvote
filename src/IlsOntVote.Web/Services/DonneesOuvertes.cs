using System.Text.Json;

namespace IlsOntVote.Web.Services;

/// <summary>Un fichier de données ouvertes tel que décrit par le manifeste écrit par l'importeur (commande « export »).</summary>
public sealed class FichierDonnees
{
    public string Fichier { get; set; } = "";
    public string Description { get; set; } = "";
    public long Lignes { get; set; }
    public long Octets { get; set; }
    public string Sha256 { get; set; } = "";
    public DateTime GenereLe { get; set; }
}

/// <summary>
/// Données ouvertes : fichiers CSV compressés produits chaque nuit par l'importeur dans un dossier (« Export:Dir » ou ILSONVOTE_EXPORT_DIR).
/// Le site ne génère rien : il liste le manifeste et sert les fichiers, avec une liste blanche de noms (jamais un chemin venant de l'adresse).
/// </summary>
public sealed class DonneesOuvertes(string dossier)
{
    private static readonly System.Text.RegularExpressions.Regex NomValide = new(@"^[a-z_]+\.csv\.gz$", System.Text.RegularExpressions.RegexOptions.Compiled);

    public string Dossier { get; } = dossier;

    /// <summary>Fichiers disponibles d'après le manifeste ; liste vide si l'export n'a pas encore tourné.</summary>
    public IReadOnlyList<FichierDonnees> Lire()
    {
        var chemin = Path.Combine(Dossier, "manifest.json");
        if (!File.Exists(chemin)) return [];
        try
        {
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<List<FichierDonnees>>(File.ReadAllText(chemin), opts) ?? [];
        }
        catch (JsonException) { return []; }
    }

    /// <summary>Chemin d'un fichier à servir, ou null : le nom doit être de la forme « nom.csv.gz » ET figurer au manifeste.</summary>
    public string? Chemin(string nom)
    {
        if (!NomValide.IsMatch(nom) || !Lire().Any(f => f.Fichier == nom)) return null;
        var chemin = Path.Combine(Dossier, nom);
        return File.Exists(chemin) ? chemin : null;
    }
}
