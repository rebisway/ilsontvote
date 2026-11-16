namespace IlsOntVote.Importer;

/// <summary>
/// Rattachement d'un vote sur l'ensemble d'un texte à ce texte quand le TITRE seul ne suffit pas : plusieurs textes portent le même titre
/// (lectures successives, textes jumeaux), ou le titre du vote diffère de celui du dossier (le titre d'un texte change en cours de navette).
/// On s'appuie alors sur la DATE : un vote sur l'ensemble a lieu le jour d'une décision de séance de la même chambre sur ce texte
/// (table texte_etapes). Règles (jamais de choix au hasard : en cas de doute, le vote reste non rattaché) :
///  1. parmi les textes de même titre, un seul a une décision ce jour-là (±<see cref="JoursTolerance"/> jour) -> celui-là ;
///  2. aucun texte de même titre : parmi les textes ayant une décision ce jour-là, celui dont le titre ressemble le plus (mots communs),
///     à condition d'atteindre <see cref="SeuilSimilarite"/> et de dépasser le suivant d'au moins <see cref="MargeSimilarite"/>.
/// </summary>
public static class Rattachement
{
    /// <summary>Écart de jours toléré entre le vote et la décision de séance (un vote peut suivre la discussion d'un jour).</summary>
    public const int JoursTolerance = 1;
    /// <summary>Part minimale de mots communs (indice de Jaccard) entre le titre du vote et celui du texte, pour le repli par ressemblance.</summary>
    public const double SeuilSimilarite = 0.6;
    /// <summary>Avance minimale du meilleur texte sur le suivant pour qu'on le retienne.</summary>
    public const double MargeSimilarite = 0.15;

    /// <summary>Indice de Jaccard des mots de deux titres normalisés : 1 = mêmes mots, 0 = aucun en commun.</summary>
    public static double Similarite(string a, string b)
    {
        var ma = a.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var mb = b.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        if (ma.Count == 0 || mb.Count == 0) return 0;
        return (double)ma.Intersect(mb).Count() / ma.Union(mb).Count();
    }

    /// <summary>Même nature de texte : « organique » et « constitutionnelle » présents (ou absents) des deux côtés.</summary>
    public static bool MemeNature(string a, string b) =>
        a.Split(' ').Contains("organique") == b.Split(' ').Contains("organique")
        && a.Split(' ').Any(m => m.StartsWith("constitutionnel")) == b.Split(' ').Any(m => m.StartsWith("constitutionnel"));

    /// <summary>
    /// Texte retenu pour un vote, ou null. <paramref name="candidatsTitre"/> : textes de même titre ; <paramref name="avecDecisionCeJour"/> : textes (id,
    /// titre normalisé) ayant une décision de la même chambre à la date du vote ; <paramref name="cleVote"/> : clé normalisée du titre du vote.
    /// </summary>
    public static string? ChoisirParDate(string cleVote, IReadOnlyCollection<string> candidatsTitre, IReadOnlyCollection<(string Id, string Titre)> avecDecisionCeJour)
    {
        var jour = avecDecisionCeJour.Select(t => t.Id).ToHashSet();
        var memeTitre = candidatsTitre.Where(jour.Contains).Distinct().ToList();
        if (memeTitre.Count == 1) return memeTitre[0];
        if (candidatsTitre.Count > 0) return null;   // plusieurs textes de même titre et la date ne tranche pas : on n'invente rien

        // Une loi ordinaire, une loi organique et une loi constitutionnelle de même sujet peuvent être votées le même jour : la nature doit être la même.
        var classes = avecDecisionCeJour.Where(t => MemeNature(cleVote, t.Titre)).Select(t => (t.Id, S: Similarite(cleVote, t.Titre))).OrderByDescending(x => x.S).ToList();
        if (classes.Count == 0 || classes[0].S < SeuilSimilarite) return null;
        return classes.Count == 1 || classes[0].S - classes[1].S >= MargeSimilarite ? classes[0].Id : null;
    }
}
