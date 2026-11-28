using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace IlsOntVote.Importer;

/// <summary>Contenu d'un fichier de pétitions canadiennes : les pétitions lues et le nombre de résultats que la source annonce.</summary>
public sealed record FichierPetitionsCanada(IReadOnlyList<PetitionRow> Petitions, int? Annonces)
{
    /// <summary>La recherche de la source plafonne ses exports (1 000 pétitions observées pour 1 712 résultats) : le fichier peut être partiel.</summary>
    public bool Partiel => Annonces > Petitions.Count;
}

/// <summary>
/// Pétitions à la Chambre des communes du Canada, à partir du fichier XML que l'administrateur télécharge dans son navigateur puis téléverse.
/// Pourquoi à la main : le téléchargement XML de la source exige un jeton reCAPTCHA ; ce contrôle anti-robot n'est pas contourné.
/// Les noms des pétitionnaires (personnes privées) figurent dans le fichier et ne sont volontairement PAS lus.
/// </summary>
public static class PetitionsCanadaSource
{
    public const string Plateforme = "CA";
    private const string RacineFr = "https://www.noscommunes.ca/petitions/fr/Petition/Details?Petition=";

    /// <summary>Balises du texte de la pétition qui terminent un paragraphe (les autres sont des enrichissements en ligne).</summary>
    private static readonly HashSet<string> Blocs = ["Title", "Para", "WhereAs", "CallUpon", "Grievances", "li", "p"];

    /// <summary>Lit le fichier. Une entrée sans numéro ou sans titre est ignorée (rien n'est inventé) ; une DTD est refusée (réglage par défaut de .NET).</summary>
    public static FichierPetitionsCanada Lire(Stream flux)
    {
        var racine = XDocument.Load(flux).Root;
        if (racine?.Name.LocalName != "Results" || racine.Element("Petitions") is not { } liste)
            throw new InvalidDataException("Fichier non reconnu : un export XML des pétitions de la Chambre des communes (<Results><Petitions>) est attendu.");
        var lignes = new Dictionary<string, PetitionRow>();
        foreach (var p in liste.Elements("Petition"))
        {
            var numero = Texte(p, "PetitionNumber");
            var titre = Texte(p, "Title");
            if (numero is null || titre is null) continue;
            var corps = p.Element("Prayer") is { } texte ? Paragraphes(texte) : null;
            lignes[numero] = new PetitionRow(
                $"CA-{numero}", Plateforme, titre, corps,
                // Pétition électronique : ouverture à la signature ; pétition papier (pas d'ouverture) : présentation, sinon certification.
                Date(p, "SignatureOpeningDateTime") ?? Date(p, "PresentedDateTime") ?? Date(p, "CertificationDateTime"),
                int.TryParse((string?)p.Attribute("SignatureCount"), out var n) ? n : null,
                Texte(p, "StatusName") ?? "?",
                Date(p, "SignatureClosingDateTime"),
                numero.StartsWith("e-", StringComparison.OrdinalIgnoreCase) ? "Pétition électronique" : "Pétition papier",
                RacineFr + Uri.EscapeDataString(numero), null, null, (string?)p.Attribute("ParliamentNumber"))
            {
                ReponseLe = Date(p, "GovernmentResponseDateTime"),
            };
        }
        return new FichierPetitionsCanada([.. lignes.Values], int.TryParse((string?)racine.Attribute("RecordsFound"), out var r) ? r : null);
    }

    /// <summary>
    /// Date en toutes lettres de la source, en français (« 27 novembre 2026 à 15 h 20 (HAE) ») ou en anglais (« November 27, 2026 at 3:20 p.m. ») :
    /// seule la date est gardée. Illisible = null.
    /// </summary>
    public static DateTime? LireDate(string? valeur)
    {
        if (string.IsNullOrWhiteSpace(valeur)) return null;
        // L'heure suit « à » ou « at » ; « 1er » (premier du mois) n'est pas compris par .NET.
        var jour = Regex.Split(valeur.Replace(' ', ' '), @"\s+(?:à|at)\s+")[0].Trim();
        jour = Regex.Replace(jour, @"^1er\b", "1");
        foreach (var culture in new[] { "fr-CA", "en-CA" })
            if (DateTime.TryParse(jour, CultureInfo.GetCultureInfo(culture), DateTimeStyles.None, out var d)) return d.Date;
        return null;
    }

    private static DateTime? Date(XElement p, string nom) => LireDate(Texte(p, nom));

    private static string? Texte(XElement p, string nom) => p.Element(nom)?.Value.Trim() is { Length: > 0 } v ? v : null;

    /// <summary>Texte de la pétition, un paragraphe par ligne.</summary>
    private static string? Paragraphes(XElement racine)
    {
        var sb = new StringBuilder();
        void Parcourir(XElement e)
        {
            foreach (var noeud in e.Nodes())
            {
                if (noeud is XText t) sb.Append(t.Value);
                else if (noeud is XElement fils)
                {
                    var bloc = Blocs.Contains(fils.Name.LocalName);
                    if (bloc) sb.Append('\n');
                    Parcourir(fils);
                    if (bloc) sb.Append('\n');
                }
            }
        }
        Parcourir(racine);
        var texte = string.Join('\n', sb.ToString().Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));
        return texte.Length == 0 ? null : texte;
    }
}
