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
    public int NbScrutinsSenat { get; set; }
    public DateTime? DateAdoptionAn { get; set; }
    public DateTime? DateAdoptionSenat { get; set; }

    /// <summary>Titre lisible : première lettre en majuscule.</summary>
    public string TitreAffiche => Titre.Length == 0 ? Titre : char.ToUpperInvariant(Titre[0]) + Titre[1..];

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

    public EtatChambre EtatAn => Etat("AN", "l'Assemblée nationale", NbScrutinsAn, AdopteAn, DateAdoptionAn);
    public EtatChambre EtatSenat => Etat("Sénat", "le Sénat", NbScrutinsSenat, AdopteSenat, DateAdoptionSenat);

    private static EtatChambre Etat(string etiquette, string devant, int nb, bool adopte, DateTime? dateAdoption)
    {
        var fr = new System.Globalization.CultureInfo("fr-FR");
        var votes = nb > 1 ? $"{nb} scrutins sur l'ensemble" : "1 scrutin sur l'ensemble";

        if (nb == 0)
            return new(etiquette, "?", "ch-inconnu",
                $"Aucun scrutin public sur l'ensemble devant {devant} dans nos données : vote à main levée, ou texte pas encore examiné. " +
                "Le détail « qui a voté quoi » n'est pas disponible.");

        return adopte
            ? new(etiquette, "✓", "ch-ok", $"Adopté par {devant}{(dateAdoption is null ? "" : " le " + dateAdoption.Value.ToString("d MMMM yyyy", fr))} ({votes})")
            : new(etiquette, "✗", "ch-ko", $"Pas adopté par {devant} lors des votes sur l'ensemble ({votes})");
    }
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
}
