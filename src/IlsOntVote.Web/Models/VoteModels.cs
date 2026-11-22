using IlsOntVote.Web.Services;
using System.Text.RegularExpressions;

namespace IlsOntVote.Web.Models;

/// <summary>Les chambres couvertes et leurs libellés. Le code « AN » / « SENAT » est celui stocké en base.</summary>
public static class Chambres
{
    public const string AN = "AN";
    public const string Senat = "SENAT";
    public const string UE = "UE";
    public const string Commons = "COMMONS";
    public const string Lords = "LORDS";
    public const string UsHouse = "USHOUSE";
    public const string UsSenate = "USSENATE";
    public const string ItCamera = "ITCAMERA";
    public const string ItSenate = "ITSENATE";
    public const string IeDail = "IEDAIL";
    public const string IeSeanad = "IESEANAD";
    public const string ChNr = "CHNR";
    public const string ChSr = "CHSR";
    public const string CaCommons = "CACOMMONS";
    public const string CaSenate = "CASENATE";

    public static string Nom(string chambre, bool en = false) => en
        ? chambre switch { Senat => "Senate", UE => "European Parliament", Commons => "House of Commons", Lords => "House of Lords", UsHouse => "House of Representatives", UsSenate => "United States Senate", ItCamera => "Chamber of Deputies", ItSenate => "Senate of the Republic", IeDail => "Dáil Éireann", IeSeanad => "Seanad Éireann", ChNr => "National Council", ChSr => "Council of States", CaCommons => "House of Commons of Canada", CaSenate => "Senate of Canada", _ => "National Assembly" }
        : chambre switch { Senat => "Sénat", UE => "Parlement européen", Commons => "Chambre des Communes", Lords => "Chambre des Lords", UsHouse => "Chambre des représentants", UsSenate => "Sénat des États-Unis", ItCamera => "Chambre des députés", ItSenate => "Sénat de la République", IeDail => "Dáil Éireann", IeSeanad => "Seanad Éireann", ChNr => "Conseil national", ChSr => "Conseil des États", CaCommons => "Chambre des communes du Canada", CaSenate => "Sénat du Canada", _ => "Assemblée nationale" };
    public static string Membre(string chambre, bool en = false) => en
        ? chambre switch { Senat => "senator", UE => "MEP", Commons => "MP", Lords => "peer", UsHouse => "Representative", UsSenate => "Senator", ItSenate => "senator", IeDail => "TD", IeSeanad => "senator", ChNr => "National Councillor", ChSr => "Councillor of States", CaCommons => "MP", CaSenate => "senator", _ => "deputy" }
        : chambre switch { Senat => "sénateur", UE => "eurodéputé", Commons => "député britannique", Lords => "pair", UsHouse => "représentant", UsSenate => "sénateur américain", ItCamera => "député italien", ItSenate => "sénateur italien", IeDail => "député irlandais", IeSeanad => "sénateur irlandais", ChNr => "conseiller national", ChSr => "conseiller aux États", CaCommons => "député canadien", CaSenate => "sénateur canadien", _ => "député" };
    public static string Membres(string chambre, bool en = false) => en
        ? chambre switch { Senat => "senators", UE => "MEPs", Commons => "MPs", Lords => "peers", UsHouse => "Representatives", UsSenate => "Senators", ItSenate => "senators", IeDail => "TDs", IeSeanad => "senators", ChNr => "National Councillors", ChSr => "Councillors of States", CaCommons => "MPs", CaSenate => "senators", _ => "deputies" }
        : chambre switch { Senat => "sénateurs", UE => "eurodéputés", Commons => "députés britanniques", Lords => "pairs", UsHouse => "représentants", UsSenate => "sénateurs américains", ItCamera => "députés italiens", ItSenate => "sénateurs italiens", IeDail => "députés irlandais", IeSeanad => "sénateurs irlandais", ChNr => "conseillers nationaux", ChSr => "conseillers aux États", CaCommons => "députés canadiens", CaSenate => "sénateurs canadiens", _ => "députés" };
    /// <summary>Sigle court pour les étiquettes de tableau.</summary>
    public static string Court(string chambre, bool en = false) => en
        ? chambre switch { Senat => "Senate", UE => "EU", Commons => "Commons", Lords => "Lords", UsHouse => "House", UsSenate => "Senate", ItCamera => "Chamber", ItSenate => "Senate", IeDail => "Dáil", IeSeanad => "Seanad", ChNr => "NC", ChSr => "CoS", CaCommons => "Commons", CaSenate => "Senate", _ => "NA" }
        : chambre switch { Senat => "Sénat", UE => "UE", Commons => "Communes", Lords => "Lords", UsHouse => "Chambre", UsSenate => "Sénat US", ItCamera => "Chambre", ItSenate => "Sénat", IeDail => "Dáil", IeSeanad => "Seanad", ChNr => "CN", ChSr => "CE", CaCommons => "Communes", CaSenate => "Sénat", _ => "AN" };
    /// <summary>Classe CSS de l'étiquette de chambre (couleur propre à chaque assemblée).</summary>
    public static string ClasseTag(string chambre) => chambre switch { Senat => "ch-senat", UE => "ch-ue", Commons => "ch-commons", Lords => "ch-lords", UsHouse => "ch-ushouse", UsSenate => "ch-ussenate", ItCamera => "ch-itcamera", ItSenate => "ch-itsenate", IeDail => "ch-iedail", IeSeanad => "ch-ieseanad", ChNr => "ch-chnr", ChSr => "ch-chsr", CaCommons => "ch-cacommons", CaSenate => "ch-casenate", _ => "ch-an" };
    /// <summary>Valeur du filtre d'URL (?chambre=) pour cette chambre.</summary>
    public static string Filtre(string chambre) => chambre switch { Senat => "senat", UE => "ue", Commons => "commons", Lords => "lords", UsHouse => "house", UsSenate => "senate", ItCamera => "camera", ItSenate => "senato", IeDail => "dail", IeSeanad => "seanad", ChNr => "cn", ChSr => "ce", CaCommons => "communes", CaSenate => "canada-senate", _ => "an" };

    /// <summary>Vrai si la chambre a des fiches d'élus et une liste d'élus (toutes, depuis les pages /members et /member/{slug} des Communes).</summary>
    public static bool AUneFiche(string chambre) => true;

    /// <summary>Préfixe d'URL des fiches : /depute/{slug}, /senateur/{slug} ou /eurodepute/{slug}.</summary>
    public static string Fiche(string chambre) => chambre switch { Senat => "senateur", UE => "eurodepute", Commons => "member", Lords => "lord", UsHouse => "representative", UsSenate => "senator", ItCamera => "deputy", ItSenate => "senatore", IeDail => "td", IeSeanad => "seanadoir", ChNr => "conseiller-national", ChSr => "conseiller-etats", CaCommons => "mp", CaSenate => "canadian-senator", _ => "depute" };
    /// <summary>Adresse de la liste des élus de la chambre.</summary>
    public static string Liste(string chambre) => chambre switch { Senat => "senateurs", UE => "eurodeputes", Commons => "members", Lords => "lords", UsHouse => "representatives", UsSenate => "senators", ItCamera => "deputies", ItSenate => "senatori", IeDail => "tds", IeSeanad => "seanad", ChNr => "conseillers-nationaux", ChSr => "conseillers-etats", CaCommons => "mps", CaSenate => "canadian-senators", _ => "deputes" };
    /// <summary>Chambre d'après le début d'une adresse (/senateurs, /eurodepute/x…) ; AN par défaut.</summary>
    public static string DepuisChemin(string chemin) =>
        chemin.StartsWith("/senateur", StringComparison.OrdinalIgnoreCase) ? Senat :
        chemin.StartsWith("/eurodepute", StringComparison.OrdinalIgnoreCase) ? UE :
        chemin.StartsWith("/member", StringComparison.OrdinalIgnoreCase) ? Commons :
        chemin.StartsWith("/lord", StringComparison.OrdinalIgnoreCase) ? Lords :
        chemin.StartsWith("/representative", StringComparison.OrdinalIgnoreCase) ? UsHouse :
        chemin.StartsWith("/deputies", StringComparison.OrdinalIgnoreCase) || chemin.StartsWith("/deputy/", StringComparison.OrdinalIgnoreCase) ? ItCamera :
        chemin.StartsWith("/senatori", StringComparison.OrdinalIgnoreCase) || chemin.StartsWith("/senatore/", StringComparison.OrdinalIgnoreCase) ? ItSenate :
        chemin.StartsWith("/conseiller-national", StringComparison.OrdinalIgnoreCase) || chemin.StartsWith("/conseillers-nationaux", StringComparison.OrdinalIgnoreCase) ? ChNr :
        chemin.StartsWith("/conseiller-etats", StringComparison.OrdinalIgnoreCase) || chemin.StartsWith("/conseillers-etats", StringComparison.OrdinalIgnoreCase) ? ChSr :
        chemin.StartsWith("/mp", StringComparison.OrdinalIgnoreCase) ? CaCommons :
        chemin.StartsWith("/canadian-senator", StringComparison.OrdinalIgnoreCase) ? CaSenate :
        chemin.StartsWith("/td", StringComparison.OrdinalIgnoreCase) ? IeDail :
        chemin.StartsWith("/seanad", StringComparison.OrdinalIgnoreCase) ? IeSeanad :
        chemin.StartsWith("/senators", StringComparison.OrdinalIgnoreCase) || chemin.StartsWith("/senator/", StringComparison.OrdinalIgnoreCase) ? UsSenate : AN;

    /// <summary>
    /// Chambre désignée par le paramètre d'adresse <c>?chambre=</c> parmi celles du site courant (« commons », « lords », « house », « senate »…), ou null (toutes). Les clés viennent de
    /// <see cref="Filtre"/> : sur le site d'un pays, le paramètre ne peut jamais désigner la chambre d'un autre pays.
    /// </summary>
    public static string? DepuisFiltre(string? cle, IEnumerable<string> visibles) => string.IsNullOrEmpty(cle) ? null : visibles.FirstOrDefault(c => Filtre(c) == cle);

    /// <summary>Chambres dont la liste montre TOUTES les divisions par défaut (pas de « vote sur l'ensemble » exploitable : Lords, et Oireachtas où presque aucun projet de loi n'est mis aux voix à la dernière étape).</summary>
    public static bool SansVoteSurEnsemble(string? chambre) => chambre is Lords or IeDail or IeSeanad;

    /// <summary>Vrai pour une chambre du Congrès des États-Unis.</summary>
    public static bool EstUs(string chambre) => chambre is UsHouse or UsSenate;
    /// <summary>Vrai pour une chambre dont les scrutins sont des « votes nominaux » à source officielle liée (Congrès des États-Unis, Parlement d'Italie, Conseils suisses, Sénat du Canada).</summary>
    public static bool EstNominal(string chambre) => chambre is UsHouse or UsSenate or ItCamera or ItSenate or ChNr or ChSr or CaSenate;
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
    /// <summary>Thèmes APPROXIMATIFS (mots-clés du titre, migration 048) : codes de la table <c>themes</c> ; vide = aucun thème repéré.</summary>
    public string[] Themes { get; set; } = [];
    /// <summary>Question de confiance (Italie : le gouvernement engage sa responsabilité sur le texte ; drapeau officiel de la source). Équivalent italien le plus proche du 49.3, mais le vote a bien lieu.</summary>
    public bool Confiance { get; set; }
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
    /// <summary>Référence du dossier : pour les États-Unis, l'adresse de l'enregistrement OFFICIEL du vote (greffe de la Chambre, Sénat) ; pour l'UE, la référence de procédure (non affichée).</summary>
    public string? DossierRef { get; set; }
    /// <summary>Adresse officielle du vote (États-Unis) ou null.</summary>
    public string? UrlOfficielle => DossierRef is { } d && d.StartsWith("https://") ? d : null;

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
    public string? InfobulleTitre(Langue l) => !TitreTraduitAuto(l.Anglais) ? null : l.Anglais ? l.T("infobulle-titre-officiel") + TitreAffiche : l.T("infobulle-titre-original") + TitreEn;
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
    /// <summary>Groupe qui siège sur les bancs du gouvernement (chambres en bancs face à face) ; faux ailleurs.</summary>
    public bool GroupeGouvernement { get; set; }
    public string Position { get; set; } = Pos.Absent;
    public bool ParDelegation { get; set; }
    public bool Rectifie { get; set; }
    public string? PositionOrigine { get; set; }
    public bool OrigineInconnue { get; set; }

    public string NomComplet => $"{Prenom} {Nom}";
    public string? Lien => Chambres.AUneFiche(Chambre) ? $"/{Chambres.Fiche(Chambre)}/{Slug}" : null;

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
/// <summary>Traduction des statuts d'annuaire à restriction (congé, suspension, déchéance) : seuls ceux que l'import retient.</summary>
public static class StatutTexte
{
    /// <summary>Libellé du statut d'annuaire dans la langue courante (textes en base) ; un statut inconnu reste tel que publié.</summary>
    public static string? Libelle(string? statut, Langue l) =>
        statut is null ? null
        : statut.Contains("leave", StringComparison.OrdinalIgnoreCase) ? l.T("statut-annuaire-conge")
        : statut.Contains("suspend", StringComparison.OrdinalIgnoreCase) ? l.T("statut-annuaire-suspendu")
        : statut.Contains("disqualif", StringComparison.OrdinalIgnoreCase) ? l.T("statut-annuaire-decheance")
        : statut;
}

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
    /// <summary>Statut du jour avec restriction (« Leave of Absence », « Suspended »… : Lords) ; null sinon. Voir la migration 041.</summary>
    public string? Statut { get; set; }

    /// <summary>Libellé du statut dans la langue de la page (la source est en anglais).</summary>
    public string? StatutLibelle(Langue l) => StatutTexte.Libelle(Statut, l);

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
    public string? InfobulleTitre(Langue l) => !TitreTraduitAuto(l.Anglais) ? null : l.Anglais ? l.T("infobulle-titre-officiel") + TitreAffiche : l.T("infobulle-titre-original") + TitreEn;
    private static string Majuscule(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

}

/// <summary>Une page de résultats (lignes + total) ; `ParPage` vaut le total lors d'un export non paginé.</summary>
public sealed record Resultats<T>(IReadOnlyList<T> Lignes, int Total, int Page, int ParPage)
{
    public int NbPages => Math.Max(1, (int)Math.Ceiling(Total / (double)ParPage));
}

/// <summary>Une fiche voisine (précédente ou suivante) pour la navigation : clé d'adresse (identifiant ou slug) et libellé de l'infobulle.</summary>
public sealed record Voisin(string Cle, string Titre);

/// <summary>Voisins d'une fiche et début de l'adresse (« /texte/ », « /petition/ »…) ; null aux extrémités de la liste.</summary>
public sealed record VoisinsVue(string Base, Voisin? Precedent, Voisin? Suivant);
