namespace IlsOntVote.Web.Services;

/// <summary>
/// Affichage des titres très longs. Constaté : un vote « en bloc » du Sénat américain (confirmation de centaines de nominations) a un titre de plusieurs dizaines de milliers de
/// caractères qui occupait des écrans entiers dans les tableaux. Les listes et tableaux montrent un titre COURT (l'intégral reste dans l'infobulle), la page du vote donne l'intégral
/// sous un « Titre complet » repliable ; les exports gardent toujours le titre entier.
/// </summary>
public static class Titres
{
    /// <summary>Longueur au-delà de laquelle un titre est abrégé dans un tableau ou une carte.</summary>
    public const int MaxListe = 400;

    /// <summary>Titre abrégé à une limite de mot, terminé par « … » ; inchangé s'il est assez court.</summary>
    public static string Court(string? titre, int max = MaxListe)
    {
        if (string.IsNullOrEmpty(titre) || titre.Length <= max) return titre ?? "";
        var coupe = titre.LastIndexOf(' ', max);
        return titre[..(coupe > max / 2 ? coupe : max)].TrimEnd(' ', ',', ';', ':') + "…";
    }

    /// <summary>
    /// Infobulle d'un titre : l'intégral quand il a été abrégé (plafonné à 1 500 caractères : un navigateur n'affiche pas davantage), puis la note de traduction éventuelle.
    /// Null s'il n'y a rien à ajouter.
    /// </summary>
    public static string? Info(string titre, string? note)
    {
        var trop = titre.Length > MaxListe;
        if (!trop) return note;
        var integral = titre.Length > 1500 ? titre[..1500] + "…" : titre;
        return note is null ? integral : note + "\n\n" + integral;
    }
}
