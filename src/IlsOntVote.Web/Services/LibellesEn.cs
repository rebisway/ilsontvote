namespace IlsOntVote.Web.Services;

/// <summary>
/// Traduction anglaise des libellés ADMINISTRATIFS fournis par les sources (type de texte, décision du Conseil constitutionnel, issue
/// d'une lecture). Ce sont des vocabulaires fermés et courts. Un libellé inconnu est renvoyé tel quel (en français) : on ne devine jamais.
/// Les titres de lois, eux, ne sont pas traduits ici (voir la traduction automatique des titres, signalée comme telle).
/// </summary>
public static class LibellesEn
{
    private static string? Chercher(Dictionary<string, string> d, string? v) =>
        v is not null && d.TryGetValue(v.Trim().ToLowerInvariant(), out var t) ? t : null;

    private static readonly Dictionary<string, string> Types = new()
    {
        ["projet de loi"] = "Government bill",
        ["proposition de loi"] = "Private member's bill",
        ["projet de loi organique"] = "Organic government bill",
        ["proposition de loi organique"] = "Organic private member's bill",
        ["projet de loi constitutionnelle"] = "Constitutional government bill",
        ["proposition de loi constitutionnelle"] = "Constitutional private member's bill",
    };
    private static readonly Dictionary<string, string> Decisions = new()
    {
        ["conforme"] = "Compliant",
        ["partiellement conforme"] = "Partially compliant",
        ["non conforme"] = "Not compliant",
        ["incompétent"] = "Not competent",
        ["non-conformité"] = "Not compliant",
    };
    private static readonly Dictionary<string, string> Resultats = new()
    {
        ["adopté"] = "Adopted", ["adoptée"] = "Adopted",
        ["adopté sans modification"] = "Adopted without amendment", ["adoptée sans modification"] = "Adopted without amendment",
        ["modifié"] = "Amended", ["modifiée"] = "Amended",
        ["rejeté"] = "Rejected", ["rejetée"] = "Rejected",
        ["retiré"] = "Withdrawn", ["retirée"] = "Withdrawn",
        ["considérée comme définitive en application de l'article 151-7 du règlement"] = "Deemed final (Rules of Procedure, art. 151-7)",
    };

    public static string TypeTexte(string? fr, bool en) => en ? Chercher(Types, fr) ?? fr ?? "" : fr ?? "";
    public static string DecisionCc(string? fr, bool en) => en ? Chercher(Decisions, fr) ?? fr ?? "" : fr ?? "";
    public static string ResultatLecture(string? fr, bool en) => en ? Chercher(Resultats, fr) ?? fr ?? "" : fr ?? "";
}
