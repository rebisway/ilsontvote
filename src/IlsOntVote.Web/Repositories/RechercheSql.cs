using System.Globalization;
using System.Text;

namespace IlsOntVote.Web.Repositories;

/// <summary>
/// Motifs de recherche dans les titres, communs aux listes : chaque mot saisi doit apparaître (ILIKE, dans n'importe quel ordre).
/// Accents, tirets et apostrophes sont neutralisés des deux côtés : « etat d'urgence » trouve « état d'urgence », « lot et garonne » trouve
/// « Lot-et-Garonne » (côté base, la colonne passe par <see cref="Colonne"/>, soit la fonction SQL recherche_norm de la migration 021). Les jokers LIKE saisis (% _ \) sont échappés : on cherche le texte, jamais un motif.
/// </summary>
public static class RechercheSql
{
    private const int MaxMots = 6;

    /// <summary>Motifs <c>%mot%</c> prêts pour <c>ILIKE ALL (@mots)</c> ; tableau vide si la recherche est vide.</summary>
    public static string[] Mots(string? recherche) =>
        SansAccents(recherche ?? "").Replace('-', ' ').Replace('\'', ' ').Replace('’', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(MaxMots)
            .Select(m => "%" + m.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%")
            .ToArray();

    /// <summary>Expression SQL de la colonne normalisée comme le texte saisi (même règle que <see cref="Mots"/>).</summary>
    public static string Colonne(string colonne) => $"recherche_norm({colonne})";

    /// <summary>Texte sans signes diacritiques ; « œ » et « æ » développés (comme unaccent côté base).</summary>
    public static string SansAccents(string texte)
    {
        var decompose = texte.Replace("œ", "oe").Replace("Œ", "OE").Replace("æ", "ae").Replace("Æ", "AE").Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decompose.Length);
        foreach (var c in decompose)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
