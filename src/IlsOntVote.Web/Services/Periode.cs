namespace IlsOntVote.Web.Services;

/// <summary>
/// Période « du … au … » saisie dans l'adresse (?du=2026-01-01&amp;au=2026-03-31). Une seule date = une borne ouverte ;
/// la même date des deux côtés = ce jour-là. Valeur illisible = ignorée (jamais d'erreur) ; bornes inversées = remises dans l'ordre.
/// Les dates sont lues au format ISO (yyyy-MM-dd) du champ HTML <c>type="date"</c>.
/// </summary>
public static class Periode
{
    /// <summary>Lit les deux dates ; renvoie null pour une borne absente ou invalide.</summary>
    public static (DateTime? Du, DateTime? Au) Lire(string? du, string? au)
    {
        var a = Parse(du);
        var b = Parse(au);
        if (a is not null && b is not null && a > b) (a, b) = (b, a);
        return (a, b);
    }

    private static DateTime? Parse(string? s) =>
        DateTime.TryParseExact(s, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var d) ? d.Date : null;
}
