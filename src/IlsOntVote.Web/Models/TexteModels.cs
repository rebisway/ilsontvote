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
    private static readonly Dictionary<string, (string Court, string Libelle)> En = new()
    {
        ["Censuré"] = ("Struck down", "Struck down by the Constitutional Council"),
        ["Censure partielle"] = ("Partly struck down", "Promulgated after partial censure by the Constitutional Council"),
        ["Promulgué"] = ("Promulgated", "Promulgated"),
        ["Rejeté"] = ("Rejected", "Rejected"),
        ["Caduc"] = ("Lapsed", "Lapsed"),
        ["Retiré"] = ("Withdrawn", "Withdrawn"),
        ["Fusionné"] = ("Merged", "Merged with another text"),
        ["Non promulgué"] = ("Not promulgated", "Adopted by both chambers — not promulgated"),
        ["En discussion"] = ("In progress", "In progress"),
        ["Adopté"] = ("Adopted", "Adopted"),
    };

    /// <summary>Le statut dans la langue demandée ; un statut inconnu garde son libellé français (jamais de texte inventé).</summary>
    public static Statut Traduit(this Statut st, bool en) =>
        en && En.TryGetValue(st.Court, out var t) ? st with { Court = t.Court, Libelle = t.Libelle } : st;
}

public class Texte
{
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
    public int NbScrutinsSenat { get; set; }
    public DateTime? DateAdoptionAn { get; set; }
    public DateTime? DateAdoptionSenat { get; set; }

    /// <summary>Titre lisible : première lettre en majuscule.</summary>
    public string TitreAffiche => Titre.Length == 0 ? Titre : char.ToUpperInvariant(Titre[0]) + Titre[1..];
    // Interface anglaise : titre anglais (HowTheyVote pour l'UE) ou TRADUCTION AUTOMATIQUE du titre français (titre_en_auto).
    public string? TitreEn { get; set; }
    public bool TitreEnAuto { get; set; }
    /// <summary>Titre dans la langue demandée ; sans titre anglais, le titre français (rien n'est inventé).</summary>
    public string TitreLangue(bool en) => en && !string.IsNullOrWhiteSpace(TitreEn) ? Majuscule(TitreEn.Trim()) : TitreAffiche;
    /// <summary>Vrai quand le titre affiché est une traduction automatique (à signaler au lecteur).</summary>
    public bool TitreTraduitAuto(bool en) => en && TitreEnAuto && !string.IsNullOrWhiteSpace(TitreEn);
    /// <summary>Infobulle d'un titre traduit automatiquement : le titre officiel français ; null sinon (attribut omis).</summary>
    public string? InfobulleTitre(bool en) => TitreTraduitAuto(en) ? "Official title (French): " + TitreAffiche : null;
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

    public EtatChambre EtatAn => EtatAnEn(false);
    public EtatChambre EtatSenat => EtatSenatEn(false);

    /// <summary>Étiquette « AN » (adopté, pas adopté, ou vote sans scrutin public) dans la langue demandée.</summary>
    public EtatChambre EtatAnEn(bool en)
    {
        // Aucun scrutin public sur l'ensemble, mais une ou plusieurs lectures décidées sans scrutin public : on le dit au lieu d'un « ? ».
        if (NbScrutinsAn == 0 && NbEtapesAn > 0)
        {
            var culture = new System.Globalization.CultureInfo(en ? "en-GB" : "fr-FR");
            var date = DateAdoptionAn is null ? "" : (en ? " on " : " le ") + DateAdoptionAn.Value.ToString("d MMMM yyyy", culture);
            var infobulle = en
                ? $"{(AdopteAn ? "Adopted" : "Not adopted")} by the National Assembly{date} in a vote without roll call (show of hands): the detail of who voted what is not available."
                : $"{(AdopteAn ? "Adopté" : "Pas adopté")} par l'Assemblée nationale{date} lors d'un vote sans scrutin public (main levée) : le détail « qui a voté quoi » n'est pas disponible.";
            return new(en ? "NA" : "AN", AdopteAn ? "✓" : "✗", (AdopteAn ? "ch-ok" : "ch-ko") + " ch-sans-scrutin", infobulle);
        }
        return Etat(en ? "NA" : "AN", en ? "the National Assembly" : "l'Assemblée nationale", NbScrutinsAn, AdopteAn, DateAdoptionAn, en);
    }

    public EtatChambre EtatSenatEn(bool en) =>
        Etat(en ? "Senate" : "Sénat", en ? "the Senate" : "le Sénat", NbScrutinsSenat, AdopteSenat, DateAdoptionSenat, en);

    private static EtatChambre Etat(string etiquette, string devant, int nb, bool adopte, DateTime? dateAdoption, bool en)
    {
        var culture = new System.Globalization.CultureInfo(en ? "en-GB" : "fr-FR");
        var votes = en
            ? (nb > 1 ? $"{nb} votes on the whole text" : "1 vote on the whole text")
            : (nb > 1 ? $"{nb} scrutins sur l'ensemble" : "1 scrutin sur l'ensemble");

        if (nb == 0)
            return new(etiquette, "?", "ch-inconnu", en
                ? $"No roll-call vote on the whole text in {devant} in our data: show-of-hands vote, or text not yet examined. The detail of who voted what is not available."
                : $"Aucun scrutin public sur l'ensemble devant {devant} dans nos données : vote à main levée, ou texte pas encore examiné. Le détail « qui a voté quoi » n'est pas disponible.");

        var date = dateAdoption is null ? "" : (en ? " on " : " le ") + dateAdoption.Value.ToString("d MMMM yyyy", culture);
        return adopte
            ? new(etiquette, "✓", "ch-ok", en ? $"Adopted by {devant}{date} ({votes})" : $"Adopté par {devant}{date} ({votes})")
            : new(etiquette, "✗", "ch-ko", en ? $"Not adopted by {devant} in the votes on the whole text ({votes})" : $"Pas adopté par {devant} lors des votes sur l'ensemble ({votes})");
    }
}

/// <summary>
/// Décision prise en séance publique à l'Assemblée sur un texte (une lecture), avec ou sans scrutin public (table texte_etapes).
/// Classe à propriétés (et non record) : Dapper lie un constructeur positionnel par nom ET type exact.
/// </summary>
public sealed class EtapeTexte
{
    public string CodeActe { get; set; } = "";
    public DateTime DateActe { get; set; }
    /// <summary>Libellé de la source : « adopté », « rejetée », « adoptée sans modification »…</summary>
    public string? Resultat { get; set; }
    /// <summary>Scrutin public lié (détail des votes disponible) ; null = aucun scrutin lié.</summary>
    public string? ScrutinId { get; set; }
    /// <summary>Vrai = décision prise sans scrutin public (main levée ou adoption sans vote nominatif) : détail des votes non disponible.</summary>
    public bool SansScrutinPublic { get; set; }

    /// <summary>Nom de la lecture d'après le code de l'acte (AN1 = première lecture, AN2 = deuxième, etc.).</summary>
    public string Lecture => LectureEn(false);

    /// <summary>Nom de la lecture dans la langue demandée.</summary>
    public string LectureEn(bool en) => en ? CodeActe.Split('-')[0] switch
    {
        "AN1" => "1st reading",
        "AN2" => "2nd reading",
        "ANLUNI" => "Single reading",
        "ANNLEC" => "New reading",
        "ANLDEF" => "Final reading",
        _ => "Reading",
    } : CodeActe.Split('-')[0] switch
    {
        "AN1" => "1re lecture",
        "AN2" => "2e lecture",
        "ANLUNI" => "Lecture unique",
        "ANNLEC" => "Nouvelle lecture",
        "ANLDEF" => "Lecture définitive",
        _ => "Lecture",
    };
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
    /// <summary>Titre dans la langue demandée ; sans titre anglais, le titre français (rien n'est inventé).</summary>
    public string TitreLangue(bool en) => en && !string.IsNullOrWhiteSpace(TitreEn) ? Majuscule(TitreEn.Trim()) : TitreAffiche;
    /// <summary>Vrai quand le titre affiché est une traduction automatique (à signaler au lecteur).</summary>
    public bool TitreTraduitAuto(bool en) => en && TitreEnAuto && !string.IsNullOrWhiteSpace(TitreEn);
    /// <summary>Infobulle d'un titre traduit automatiquement : le titre officiel français ; null sinon (attribut omis).</summary>
    public string? InfobulleTitre(bool en) => TitreTraduitAuto(en) ? "Official title (French): " + TitreAffiche : null;
    private static string Majuscule(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

}
