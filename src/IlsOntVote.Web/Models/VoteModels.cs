using System.Text.RegularExpressions;

namespace IlsOntVote.Web.Models;

/// <summary>Les chambres couvertes et leurs libellés. Le code « AN » / « SENAT » est celui stocké en base.</summary>
public static class Chambres
{
    public const string AN = "AN";
    public const string Senat = "SENAT";
    public const string UE = "UE";

    public static string Nom(string chambre, bool en = false) => en
        ? chambre switch { Senat => "Senate", UE => "European Parliament", _ => "National Assembly" }
        : chambre switch { Senat => "Sénat", UE => "Parlement européen", _ => "Assemblée nationale" };
    public static string Membre(string chambre, bool en = false) => en
        ? chambre switch { Senat => "senator", UE => "MEP", _ => "deputy" }
        : chambre switch { Senat => "sénateur", UE => "eurodéputé", _ => "député" };
    public static string Membres(string chambre, bool en = false) => en
        ? chambre switch { Senat => "senators", UE => "MEPs", _ => "deputies" }
        : chambre switch { Senat => "sénateurs", UE => "eurodéputés", _ => "députés" };
    /// <summary>Sigle court pour les étiquettes de tableau.</summary>
    public static string Court(string chambre, bool en = false) => en
        ? chambre switch { Senat => "Senate", UE => "EU", _ => "NA" }
        : chambre switch { Senat => "Sénat", UE => "UE", _ => "AN" };
    /// <summary>Classe CSS de l'étiquette de chambre (couleur propre à chaque assemblée).</summary>
    public static string ClasseTag(string chambre) => chambre switch { Senat => "ch-senat", UE => "ch-ue", _ => "ch-an" };
    /// <summary>Valeur du filtre d'URL (?chambre=) pour cette chambre.</summary>
    public static string Filtre(string chambre) => chambre switch { Senat => "senat", UE => "ue", _ => "an" };

    /// <summary>Préfixe d'URL des fiches : /depute/{slug}, /senateur/{slug} ou /eurodepute/{slug}.</summary>
    public static string Fiche(string chambre) => chambre switch { Senat => "senateur", UE => "eurodepute", _ => "depute" };
    /// <summary>Adresse de la liste des élus de la chambre.</summary>
    public static string Liste(string chambre) => chambre switch { Senat => "senateurs", UE => "eurodeputes", _ => "deputes" };
    /// <summary>Chambre d'après le début d'une adresse (/senateurs, /eurodepute/x…) ; AN par défaut.</summary>
    public static string DepuisChemin(string chemin) =>
        chemin.StartsWith("/senateur", StringComparison.OrdinalIgnoreCase) ? Senat :
        chemin.StartsWith("/eurodepute", StringComparison.OrdinalIgnoreCase) ? UE : AN;
}

/// <summary>Valeurs de <c>Position</c> d'un parlementaire pour un scrutin.</summary>
public static class Pos
{
    public const string Pour = "pour";
    public const string Contre = "contre";
    public const string Abstention = "abstention";
    public const string NonVotant = "non_votant";
    public const string Absent = "absent";

    public static string Libelle(string p, bool en = false) => en
        ? p switch { Pour => "For", Contre => "Against", Abstention => "Abstention", NonVotant => "Non-voting", _ => "Absent" }
        : p switch
    {
        Pour => "Pour",
        Contre => "Contre",
        Abstention => "Abstention",
        NonVotant => "Non-votant",
        _ => "Absent",
    };
}

/// <summary>Un scrutin public (un vote), tel qu'importé de l'Assemblée nationale ou du Sénat.</summary>
public sealed class Scrutin
{
    public string Id { get; set; } = "";
    public string Chambre { get; set; } = Chambres.AN;
    public int Legislature { get; set; }
    public int Numero { get; set; }
    public DateTime DateScrutin { get; set; }
    public string Titre { get; set; } = "";
    public string? Sort { get; set; }
    public string? TypeVote { get; set; }
    public bool SurEnsemble { get; set; }
    public int? NbVotants { get; set; }
    public int? NbPour { get; set; }
    public int? NbContre { get; set; }
    public int? NbAbstentions { get; set; }
    public int? NbNonVotants { get; set; }
    public string? FeaturedTitle { get; set; }
    public bool NominatifIncomplet { get; set; }
    public int NbVotesManquants { get; set; }
    public string? Note { get; set; }
    public string? TypeVoteLibelle { get; set; }
    public string? TypeMajorite { get; set; }
    public int? SuffragesExprimes { get; set; }
    public int? SuffragesRequis { get; set; }

    public string TitreAffiche => string.IsNullOrWhiteSpace(FeaturedTitle) ? Capitalise(Titre) : FeaturedTitle!;
    // Interface anglaise : titre anglais (HowTheyVote pour l'UE) ou TRADUCTION AUTOMATIQUE du titre français (titre_en_auto).
    public string? TitreEn { get; set; }
    public bool TitreEnAuto { get; set; }
    /// <summary>Vrai quand le titre (français) est une TRADUCTION AUTOMATIQUE de l'original anglais (votes du Parlement européen sans titre officiel français).</summary>
    public bool TitreFrAuto { get; set; }
    /// <summary>Titre dans la langue demandée ; sans titre anglais, le titre français (rien n'est inventé).</summary>
    public string TitreLangue(bool en) => en && !string.IsNullOrWhiteSpace(TitreEn) ? Majuscule(TitreEn.Trim()) : TitreAffiche;
    /// <summary>Vrai quand le titre affiché est une traduction automatique (à signaler au lecteur).</summary>
    public bool TitreTraduitAuto(bool en) => !string.IsNullOrWhiteSpace(TitreEn) && (en ? TitreEnAuto : TitreFrAuto);
    /// <summary>Infobulle d'un titre traduit automatiquement : le titre officiel français ; null sinon (attribut omis).</summary>
    public string? InfobulleTitre(bool en) => !TitreTraduitAuto(en) ? null : en ? "Official title (French): " + TitreAffiche : "Titre original (anglais) : " + TitreEn;
    private static string Majuscule(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    public bool Adopte => Sort is not null && Sort.StartsWith("adopt", StringComparison.OrdinalIgnoreCase);

    // « l'ensemble du projet de loi X » -> « Projet de loi X » : le vote sur l'ensemble est le cas courant.
    private static readonly Regex PrefixeEnsemble =
        new(@"^l['’]ensemble (du|de la|des|de l['’]) ?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string Capitalise(string s)
    {
        s = PrefixeEnsemble.Replace(s.Trim().TrimEnd('.'), "");
        return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
    }
}

/// <summary>Un siège : un parlementaire et sa position pour un scrutin donné.</summary>
/// <summary>Un siège de l'hémicycle : un élu en fonction à la date du scrutin, avec sa position (vote, absent, non-votant).</summary>
public sealed class Siege
{
    public string ParlementaireId { get; set; } = "";
    public string Chambre { get; set; } = Chambres.AN;
    public string Prenom { get; set; } = "";
    public string Nom { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? GroupeId { get; set; }
    public string GroupeSigle { get; set; } = "";
    public string? GroupeCouleur { get; set; }
    public int GroupeOrdre { get; set; }
    public string Position { get; set; } = Pos.Absent;
    public bool ParDelegation { get; set; }
    public bool Rectifie { get; set; }
    public string? PositionOrigine { get; set; }
    public bool OrigineInconnue { get; set; }

    public string NomComplet => $"{Prenom} {Nom}";
    public string Lien => $"/{Chambres.Fiche(Chambre)}/{Slug}";

    // Coordonnées calculées par HemicycleLayout.
    public double X { get; set; }
    public double Y { get; set; }
}

/// <summary>Décompte des votes d'un groupe politique pour un scrutin.</summary>
public sealed record GroupeDecompte(
    string Sigle, string? Couleur, int Ordre, int Pour, int Contre, int Abstention, int NonVotant, int Absent)
{
    public int Total => Pour + Contre + Abstention + NonVotant + Absent;
}

/// <summary>Un parlementaire en exercice (ou une fiche), avec son groupe courant.</summary>
/// <summary>Un élu (député ou sénateur) et son groupe courant. `EnExercice` : un mandat couvre aujourd'hui, dans la législature la plus récente pour l'AN.</summary>
public sealed class Parlementaire
{
    public string Id { get; set; } = "";
    public string Chambre { get; set; } = Chambres.AN;
    public string? Civilite { get; set; }
    public string Prenom { get; set; } = "";
    public string Nom { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? Departement { get; set; }
    public string? Circonscription { get; set; }
    public string? GroupeSigle { get; set; }
    public string? GroupeLibelle { get; set; }
    public string? GroupeCouleur { get; set; }
    public int GroupeOrdre { get; set; } = 130;
    public bool EnExercice { get; set; }

    public string NomComplet => $"{Prenom} {Nom}";
    public string Lien => $"/{Chambres.Fiche(Chambre)}/{Slug}";
}

/// <summary>Bilan de participation d'un parlementaire sur les scrutins publics de son mandat.</summary>
/// <summary>Bilan de participation d'un élu. La participation compte pour, contre et abstention ; non-votant et absent n'y entrent pas.</summary>
public sealed class BilanParlementaire
{
    public int Total { get; set; }
    public int Pour { get; set; }
    public int Contre { get; set; }
    public int Abstention { get; set; }
    public int NonVotant { get; set; }
    public int Absent { get; set; }
    /// <summary>Votes exprimés par délégation (compris dans pour / contre / abstention, pas en plus).</summary>
    public int Delegation { get; set; }
    /// <summary>Votes rectifiés ou mises au point (compris dans les positions finales ci-dessus).</summary>
    public int MiseAuPoint { get; set; }

    public int Participations => Pour + Contre + Abstention;
    public double TauxParticipation => Total == 0 ? 0 : 100.0 * Participations / Total;
}

/// <summary>
/// Période de mandat d'un élu (début, fin ; fin vide = en cours). Début = date de prise de fonction (pour l'AN : celle du siège effectif,
/// pas la date de l'élection, qui précède pour un suppléant).
/// </summary>
public sealed class PeriodeMandat
{
    public DateTime DateDebut { get; set; }
    public DateTime? DateFin { get; set; }
}

/// <summary>
/// Participation d'un élu sur un mois : scrutins à sa charge (mandat en cours, hors « non votant » déclaré), votes exprimés et dont par délégation.
/// Mêmes définitions que la page des taux (/elus).
/// </summary>
public sealed class PointMensuel
{
    public DateTime Mois { get; set; }
    public int Scrutins { get; set; }
    public int Votants { get; set; }
    public int Delegations { get; set; }
    /// <summary>Part des scrutins où un vote a été exprimé (par l'élu ou par délégation), en pourcentage.</summary>
    public double ParticipationPct => Scrutins == 0 ? 0 : 100.0 * Votants / Scrutins;
    /// <summary>Part des scrutins où l'élu a voté lui-même (sans délégation).</summary>
    public double PresencePct => Scrutins == 0 ? 0 : 100.0 * (Votants - Delegations) / Scrutins;
}

/// <summary>Une ligne de l'historique de vote d'un parlementaire.</summary>
/// <summary>Une ligne de l'historique de vote d'un élu (absence incluse : un scrutin où il siégeait sans voter).</summary>
public sealed class LigneVote
{
    public string ScrutinId { get; set; } = "";
    public DateTime DateScrutin { get; set; }
    public string Titre { get; set; } = "";
    public string? Sort { get; set; }
    public string Position { get; set; } = Pos.Absent;
    public bool ParDelegation { get; set; }
    public bool Rectifie { get; set; }
    public string? PositionOrigine { get; set; }
    public bool OrigineInconnue { get; set; }

    public string TitreAffiche => new Scrutin { Titre = Titre }.TitreAffiche;
    // Interface anglaise : titre anglais (HowTheyVote pour l'UE) ou TRADUCTION AUTOMATIQUE du titre français (titre_en_auto).
    public string? TitreEn { get; set; }
    public bool TitreEnAuto { get; set; }
    /// <summary>Vrai quand le titre (français) est une TRADUCTION AUTOMATIQUE de l'original anglais (votes du Parlement européen sans titre officiel français).</summary>
    public bool TitreFrAuto { get; set; }
    /// <summary>Titre dans la langue demandée ; sans titre anglais, le titre français (rien n'est inventé).</summary>
    public string TitreLangue(bool en) => en && !string.IsNullOrWhiteSpace(TitreEn) ? Majuscule(TitreEn.Trim()) : TitreAffiche;
    /// <summary>Vrai quand le titre affiché est une traduction automatique (à signaler au lecteur).</summary>
    public bool TitreTraduitAuto(bool en) => !string.IsNullOrWhiteSpace(TitreEn) && (en ? TitreEnAuto : TitreFrAuto);
    /// <summary>Infobulle d'un titre traduit automatiquement : le titre officiel français ; null sinon (attribut omis).</summary>
    public string? InfobulleTitre(bool en) => !TitreTraduitAuto(en) ? null : en ? "Official title (French): " + TitreAffiche : "Titre original (anglais) : " + TitreEn;
    private static string Majuscule(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

}

/// <summary>Une page de résultats (lignes + total) ; `ParPage` vaut le total lors d'un export non paginé.</summary>
public sealed record Resultats<T>(IReadOnlyList<T> Lignes, int Total, int Page, int ParPage)
{
    public int NbPages => Math.Max(1, (int)Math.Ceiling(Total / (double)ParPage));
}
