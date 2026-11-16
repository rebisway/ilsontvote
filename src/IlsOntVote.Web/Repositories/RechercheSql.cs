namespace IlsOntVote.Web.Repositories;

/// <summary>
/// Motifs de recherche dans les titres, communs aux listes : chaque mot saisi doit apparaître (ILIKE, dans n'importe quel ordre).
/// Les tirets et apostrophes comptent comme des espaces, des deux côtés : « lot et garonne » trouve « Lot-et-Garonne » (côté base, la colonne
/// passe par <see cref="Colonne"/>). Les jokers LIKE saisis (% _ \) sont échappés : on cherche le texte, jamais un motif.
/// </summary>
public static class RechercheSql
{
    private const int MaxMots = 6;

    /// <summary>Motifs <c>%mot%</c> prêts pour <c>ILIKE ALL (@mots)</c> ; tableau vide si la recherche est vide.</summary>
    public static string[] Mots(string? recherche) =>
        (recherche ?? "").Replace('-', ' ').Replace('\'', ' ').Replace('’', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(MaxMots)
            .Select(m => "%" + m.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%")
            .ToArray();

    /// <summary>Expression SQL de la colonne avec tirets et apostrophes remplacés par des espaces (même normalisation que <see cref="Mots"/>).</summary>
    public static string Colonne(string colonne) => $"translate({colonne}, '-''’', '   ')";
}
