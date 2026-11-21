using IlsOntVote.Web.Services;
namespace IlsOntVote.Web.Models;

/// <summary>Famille de statut : sert à choisir la couleur de l'étiquette (cf. site.css, `.statut-*`).</summary>
public enum FamilleStatut { Promulgue, PartiellementCensure, Censure, DeuxAssemblees, EnDiscussion, Adopte, Rejete, Abandonne }

/// <summary><c>Court</c> : étiquette de tableau. <c>Libelle</c> : formulation complète (infobulle, page du texte).</summary>
public sealed record Statut(FamilleStatut Famille, string Court, string Libelle)
{
    public string Classe => Famille switch
    {
        FamilleStatut.Promulgue => "statut-promulgue",
        FamilleStatut.PartiellementCensure => "statut-partiel",
        FamilleStatut.Censure => "statut-censure",
        FamilleStatut.DeuxAssemblees => "statut-attente",
        FamilleStatut.EnDiscussion => "statut-discussion",
        FamilleStatut.Adopte => "statut-promulgue",
        _ => "statut-inactif",
    };
}

/// <summary>État d'un texte devant une assemblée, pour une mini-étiquette « AN » ou « Sénat ».</summary>
public sealed record EtatChambre(string Etiquette, string Symbole, string Classe, string Infobulle);

/// <summary>Un texte de loi (dossier législatif) et son parcours.</summary>
/// <summary>Traduction anglaise du statut d'un texte (la clé est le libellé court français produit par <see cref="Texte.Statut"/>).</summary>
public static class StatutTraduction
{
    /// <summary>Le statut dans la langue courante (textes en base : « statut-court-… », « statut-libelle-… ») ; un statut inconnu garde son libellé français (jamais de texte inventé).</summary>
    public static Statut Traduit(this Statut st, Langue l)
    {
        if (l.Code == "fr") return st;
        var k = Cles.Slug(st.Court);
        return l.TexteOuNull("statut-court-" + k) is { } court ? st with { Court = court, Libelle = l.TexteOuNull("statut-libelle-" + k) ?? st.Libelle } : st;
    }
}

public class Texte
{
    /// <summary>Thèmes APPROXIMATIFS (mots-clés du titre, migration 048) : codes de la table <c>themes</c> ; vide = aucun thème repéré.</summary>
    public string[] Themes { get; set; } = [];
    public string Id { get; set; } = "";
    public string? TypeLibelle { get; set; }
    public string Titre { get; set; } = "";
    public string? EtatCode { get; set; }
    public string? EtatLibelle { get; set; }
    public string? NumeroLoi { get; set; }
    public DateTime? DatePromulgation { get; set; }
    public DateTime? DateJo { get; set; }
    public string? NumeroJo { get; set; }
    public string? UrlLegifrance { get; set; }
    public string? UrlDossierSenat { get; set; }
    public string? UrlDossierAn { get; set; }
    public DateTime? SaisineCcDate { get; set; }
    public string? SaisineCcPar { get; set; }
    public string? DecisionCcNumero { get; set; }
    public DateTime? DecisionCcDate { get; set; }
    public string? DecisionCcCode { get; set; }
    public string? DecisionCcLibelle { get; set; }
    public string? DecisionCcUrl { get; set; }

    // Calculés par la requête (agrégat des scrutins rattachés).
    public DateTime? DernierVote { get; set; }
    public int NbScrutins { get; set; }
    public bool AdopteAn { get; set; }
    public bool AdopteSenat { get; set; }
    public int NbScrutinsAn { get; set; }
    /// <summary>Décisions de lecture à l'Assemblée connues (avec ou sans scrutin public).</summary>
    public int NbEtapesAn { get; set; }
    /// <summary>Une décision de lecture à l'Assemblée a adopté le texte (peut être un vote à main levée).</summary>
    public bool AdopteAnEtape { get; set; }
    /// <summary>Le texte a été adopté (au moins en partie) sans vote, par l'article 49, alinéa 3.</summary>
    public bool Adopte493 { get; set; }
    public int NbScrutinsSenat { get; set; }
    /// <summary>
    /// Amendements déposés à l'Assemblée sur ce texte, effectif OFFICIEL (jeu « Amendements », 16e et 17e législatures). NULL = pas de donnée :
    /// texte sans dossier à l'Assemblée (« non applicable ») ou législature non couverte (« manquant », cf. <see cref="AmendementsManquants"/>).
    /// </summary>
    public int? NbAmendements { get; set; }
    public int? NbAmendementsAdoptes { get; set; }
    public int? NbAmendementsRejetes { get; set; }
    /// <summary>Scrutins publics (tous rattachés au texte) dont le titre porte sur un amendement : CALCULÉ par nous, pas un chiffre officiel.</summary>
    public int NbAmendementsScrutins { get; set; }
    /// <summary>Le texte a un dossier à l'Assemblée mais sans effectif d'amendements (législature antérieure à la 16e) : à signaler « ? », pas « 0 ».</summary>
    public bool AmendementsManquants => NbAmendements is null && NbEtapesAn > 0;
    /// <summary>Décisions de séance du Sénat connues (avec ou sans scrutin public).</summary>
    public int NbEtapesSenat { get; set; }
    public DateTime? DateAdoptionAn { get; set; }
    public DateTime? DateAdoptionSenat { get; set; }

    /// <summary>Titre lisible : première lettre en majuscule.</summary>
    public string TitreAffiche => Titre.Length == 0 ? Titre : char.ToUpperInvariant(Titre[0]) + Titre[1..];
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


    /// <summary>
    /// Statut factuel déduit des données — jamais un jugement. « Adopté par les deux assemblées » sans
    /// promulgation est la formulation neutre de « voté mais pas (encore) promulgué ».
    /// </summary>
    public Statut Statut
    {
        get
        {
            if (DecisionCcCode == "03")
                return new(FamilleStatut.Censure, "Censuré", "Censuré par le Conseil constitutionnel");
            if (DatePromulgation is not null)
                return DecisionCcCode == "02"
                    ? new(FamilleStatut.PartiellementCensure, "Censure partielle", "Promulgué après censure partielle du Conseil constitutionnel")
                    : new(FamilleStatut.Promulgue, "Promulgué", "Promulgué");

            return EtatCode switch
            {
                "03" => new(FamilleStatut.Rejete, "Rejeté", "Rejeté"),
                "05" => new(FamilleStatut.Abandonne, "Caduc", "Caduc"),
                "06" => new(FamilleStatut.Abandonne, "Retiré", "Retiré"),
                "02" => new(FamilleStatut.Abandonne, "Fusionné", "Fusionné avec un autre texte"),
                "01" when AdopteAn && AdopteSenat => new(FamilleStatut.DeuxAssemblees, "Non promulgué", "Adopté par les deux assemblées — non promulgué"),
                "01" => new(FamilleStatut.EnDiscussion, "En discussion", "En discussion"),
                "04" => new(FamilleStatut.Adopte, "Adopté", "Adopté"),
                _ => new(FamilleStatut.EnDiscussion, "En discussion", EtatLibelle ?? "Statut inconnu"),
            };
        }
    }

    /// <summary>Étiquette « AN » (adopté, pas adopté, ou vote sans scrutin public) dans la langue courante (tous les textes en base).</summary>
    public EtatChambre EtatAnEn(Langue l) => EtatDe(l, "an", NbScrutinsAn, NbEtapesAn, AdopteAn, DateAdoptionAn, Adopte493);

    /// <summary>Étiquette « Sénat » (adopté, pas adopté, ou vote sans scrutin public) dans la langue courante.</summary>
    public EtatChambre EtatSenatEn(Langue l) => EtatDe(l, "senat", NbScrutinsSenat, NbEtapesSenat, AdopteSenat, DateAdoptionSenat, false);

    private static EtatChambre EtatDe(Langue l, string chambre, int nbScrutins, int nbEtapes, bool adopte, DateTime? dateAdoption, bool adopte493)
    {
        var etiquette = l.T("etiquette-" + chambre);
        var devant = l.T("devant-" + chambre);
        // Date d'adoption (« le 12 mars 2026 ») précédée du mot de la langue ; vide si inconnue.
        var date = dateAdoption is null ? "" : l.Tf("etat-date", dateAdoption.Value.ToString("d MMMM yyyy", l.Culture));

        // Aucun scrutin public sur l'ensemble, mais une ou plusieurs lectures décidées sans scrutin public : on le dit au lieu d'un « ? ».
        if (nbScrutins == 0 && nbEtapes > 0)
        {
            if (adopte493 && adopte)
                return new(etiquette, "✓", "ch-ok ch-sans-scrutin", l.Tf("etat-an-article-49-3", date));
            return new(etiquette, adopte ? "✓" : "✗", (adopte ? "ch-ok" : "ch-ko") + " ch-sans-scrutin",
                l.Tf("etat-main-levee", adopte ? l.T("etat-adopte") : l.T("etat-pas-adopte"), devant, date));
        }

        var votes = nbScrutins > 1 ? l.Tf("etat-votes-pl", nbScrutins) : l.T("etat-votes-sg");
        if (nbScrutins == 0) return new(etiquette, "?", "ch-inconnu", l.Tf("etat-aucun-scrutin", devant));
        return adopte
            ? new(etiquette, "✓", "ch-ok", l.Tf("etat-adopte-par", devant, date, votes))
            : new(etiquette, "✗", "ch-ko", l.Tf("etat-pas-adopte-par", devant, votes));
    }
}

/// <summary>
/// Décision prise en séance publique à l'Assemblée sur un texte (une lecture), avec ou sans scrutin public (table texte_etapes).
/// Classe à propriétés (et non record) : Dapper lie un constructeur positionnel par nom ET type exact.
/// </summary>
public sealed class EtapeTexte
{
    /// <summary>« AN » ou « SENAT ».</summary>
    public string Chambre { get; set; } = "AN";
    public string CodeActe { get; set; } = "";
    public DateTime DateActe { get; set; }
    /// <summary>Libellé de la source : « adopté », « rejetée », « adoptée sans modification »…</summary>
    public string? Resultat { get; set; }
    /// <summary>Scrutin public lié (détail des votes disponible) ; null = aucun scrutin lié.</summary>
    public string? ScrutinId { get; set; }
    /// <summary>Vrai = décision prise sans scrutin public (main levée ou adoption sans vote nominatif) : détail des votes non disponible.</summary>
    public bool SansScrutinPublic { get; set; }
    /// <summary>Décision « considérée comme adoptée » par l'article 49, alinéa 3 : AUCUN vote sur le texte (différent d'une main levée).</summary>
    public bool Article493 { get; set; }
    /// <summary>Motion de censure liée au texte (toujours votée par scrutin public).</summary>
    public bool EstMotion => CodeActe.Contains("-MOTION-VOTE", StringComparison.Ordinal);

    /// <summary>Nom de la lecture dans la langue courante (textes en base : « lecture-… »).</summary>
    public string LectureEn(Langue l) => Chambre == "SENAT" ? LectureSenat(l) : LectureAn(l);

    /// <summary>Lecture au Sénat d'après le code SEN&lt;type&gt;-DEC (type de lecture de DOSLEG : 1 première, 2 deuxième… 4 CMP, 5 nouvelle, 6 définitive).</summary>
    private string LectureSenat(Langue l) => l.T(CodeActe.Split('-')[0] switch
    {
        "SEN1" => "lecture-1",
        "SEN2" => "lecture-2",
        "SEN3" => "lecture-3",
        "SEN4" => "lecture-cmp",
        "SEN5" => "lecture-nouvelle",
        "SEN6" => "lecture-definitive",
        "SEN7" => "lecture-4",
        _ => "lecture",
    });

    /// <summary>Lecture à l'Assemblée d'après le code de l'acte (AN1 = première lecture, AN2 = deuxième, etc.) ; une motion de censure a son propre libellé.</summary>
    private string LectureAn(Langue l) => l.T(EstMotion ? "lecture-motion-censure" : CodeActe.Split('-')[0] switch
    {
        "AN1" => "lecture-1",
        "AN2" => "lecture-2",
        "ANLUNI" => "lecture-unique",
        "ANNLEC" => "lecture-nouvelle",
        "ANLDEF" => "lecture-definitive",
        "CMP" => "lecture-cmp",
        _ => "lecture",
    });
}

public sealed class ScrutinDuTexte
{
    public string Id { get; set; } = "";
    public string Chambre { get; set; } = "";
    public DateTime DateScrutin { get; set; }
    public string Titre { get; set; } = "";
    public string? Sort { get; set; }
    public int? NbPour { get; set; }
    public int? NbContre { get; set; }
    public int? NbAbstentions { get; set; }

    public bool Adopte => Sort is not null && Sort.StartsWith("adopt", StringComparison.OrdinalIgnoreCase);
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
