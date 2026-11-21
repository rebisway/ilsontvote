namespace IlsOntVote.Web.Services;

/// <summary>
/// Traduction des libellés ADMINISTRATIFS fournis par les sources (type de texte, décision du Conseil constitutionnel, issue d'une lecture). Ce sont des vocabulaires fermés et courts, traduits
/// EN BASE (clés « vocab-type-… », « vocab-decision-… », « vocab-resultat-… » dérivées de la valeur française). Un libellé inconnu est renvoyé tel quel (en français) : on ne devine jamais.
/// Les titres de lois, eux, ne sont pas traduits ici (voir la traduction automatique des titres, signalée comme telle).
/// </summary>
public static class LibellesEn
{
    private static string Traduire(string prefixe, string? fr, Langue l)
    {
        if (string.IsNullOrWhiteSpace(fr) || l.Code == "fr") return fr ?? "";
        return l.TexteOuNull(prefixe + Cles.Slug(fr)) ?? fr;
    }

    public static string TypeTexte(string? fr, Langue l) => Traduire("vocab-type-", fr, l);
    public static string DecisionCc(string? fr, Langue l) => Traduire("vocab-decision-", fr, l);
    public static string ResultatLecture(string? fr, Langue l) => Traduire("vocab-resultat-", fr, l);
}
