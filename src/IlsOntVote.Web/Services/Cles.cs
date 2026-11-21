using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace IlsOntVote.Web.Services;

/// <summary>
/// Clés de traduction DÉRIVÉES D'UNE DONNÉE (type de texte, décision, pays d'un eurodéputé…) : les vocabulaires fermés des sources sont traduits en base, une ligne par valeur et par langue.
/// La clé n'est pas dérivée d'une phrase de l'interface (règle des clés stables) mais du CODE de la donnée, jamais d'un libellé affiché.
/// </summary>
public static class Cles
{
    /// <summary>Minuscules, sans accents, tout ce qui n'est pas lettre ou chiffre devient « - » (« Rejeté (question préalable) » -> « rejete-question-prealable »).</summary>
    public static string Slug(string valeur)
    {
        var d = valeur.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in d) if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return Regex.Replace(sb.ToString().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
    }
}
