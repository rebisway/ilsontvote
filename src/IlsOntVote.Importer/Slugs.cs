using System.Globalization;
using System.Text;

namespace IlsOntVote.Importer;

/// <summary>Slugs d'URL des parlementaires (« jean-dupont »). Un slug, une fois attribué, ne change jamais.</summary>
public static class Slugs
{
    /// <summary>
    /// Slug unique pour un élu : « prenom-nom », suffixé de son identifiant en cas d'homonyme. Les slugs existants
    /// sont passés dans <paramref name="deja"/> pour garantir l'unicité entre deux imports (voir l'appelant : un slug
    /// déjà attribué à un élu n'est jamais recalculé, car il fait son adresse web).
    /// </summary>
    public static string Unique(HashSet<string> deja, string prenom, string nom, string id)
    {
        var slug = Slugify($"{prenom} {nom}");
        if (slug.Length == 0) slug = id.ToLowerInvariant();
        if (!deja.Add(slug))
        {
            slug = $"{slug}-{id.ToLowerInvariant()}";
            deja.Add(slug);
        }
        return slug;
    }

    /// <summary>Minuscules, accents retirés (décomposition Unicode), tout ce qui n'est pas lettre ou chiffre devient un tiret.</summary>
    public static string Slugify(string text)
    {
        var normalise = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        var dernierTiret = true;
        foreach (var c in normalise)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) { sb.Append(char.ToLowerInvariant(c)); dernierTiret = false; }
            else if (!dernierTiret) { sb.Append('-'); dernierTiret = true; }
        }
        return sb.ToString().Trim('-');
    }
}
