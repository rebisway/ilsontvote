using Microsoft.AspNetCore.Html;

namespace IlsOntVote.Web.Services;

/// <summary>Les trois niveaux de fiabilité d'une donnée affichée (règle du projet : le doute se voit toujours).</summary>
public enum NiveauDoute
{
    /// <summary>Calculé ou déduit par le site (pas publié tel quel) : ≈</summary>
    Calcule,
    /// <summary>Source incomplète ou contradictoire : ⚠</summary>
    Incertain,
    /// <summary>Donnée absente de la source : ?</summary>
    Manquant,
}

/// <summary>Étiquette unique du doute : icône + libellé court + infobulle qui explique pourquoi (et la même légende partout).</summary>
public static class Doute
{
    public static IHtmlContent Html(NiveauDoute niveau, string court, string explication)
    {
        var (classe, icone) = niveau switch
        {
            NiveauDoute.Calcule => ("doute-calcule", "≈"),
            NiveauDoute.Incertain => ("doute-incertain", "⚠"),
            _ => ("doute-manquant", "?"),
        };
        var e = System.Net.WebUtility.HtmlEncode(explication);
        return new HtmlString($"<span class=\"doute {classe}\" title=\"{e}\" tabindex=\"0\">{icone} {System.Net.WebUtility.HtmlEncode(court)}</span>");
    }
}
