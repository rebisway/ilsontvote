using IlsOntVote.Importer;
using IlsOntVote.Web.Models;
using IlsOntVote.Web.Repositories;
using IlsOntVote.Web.Services;

namespace IlsOntVote.Tests;

/// <summary>Langue par défaut du navigateur : français seulement si la langue la plus prioritaire est le français.</summary>
public class LangueParDefautTests
{
    [Theory]
    [InlineData("fr-FR,fr;q=0.9,en;q=0.8", false)]
    [InlineData("fr", false)]
    [InlineData("en-US,en;q=0.9", true)]
    [InlineData("de-DE", true)]
    [InlineData("en;q=0.8,fr;q=0.9", false)]   // le français a le poids le plus fort, même s'il est cité après
    [InlineData("*", false)]
    [InlineData("", false)]                      // sans en-tête (robots d'indexation) : français
    public void LaLanguePrioritaireDecide(string entete, bool anglais) => Assert.Equal(anglais, Langue.PrefereAnglais(entete));
}

/// <summary>Recherche dans les titres : tirets et apostrophes = espaces, jokers LIKE échappés, 6 mots au plus.</summary>
public class RechercheSqlTests
{
    [Fact]
    public void LesTiretsEtApostrophesSontDesEspaces() =>
        Assert.Equal(["%lot%", "%et%", "%garonne%"], RechercheSql.Mots("lot-et-garonne"));

    [Fact]
    public void LApostropheTypographiqueEstTraiteeCommeLApostropheDroite() =>
        Assert.Equal(RechercheSql.Mots("l'urgence"), RechercheSql.Mots("l’urgence"));

    [Fact]
    public void LesJokersSaisisSontEchappes() => Assert.Equal(["%100\\%%"], RechercheSql.Mots("100%"));

    [Fact]
    public void RechercheVide_NaPasDeMotif() => Assert.Empty(RechercheSql.Mots("  "));

    [Fact]
    public void SixMotsAuPlus() => Assert.Equal(6, RechercheSql.Mots("a b c d e f g h").Length);
}

/// <summary>Taux des élus : même dénominateur (les scrutins à sa charge) pour tous.</summary>
public class TauxElusTests
{
    [Fact]
    public void LesTauxPartagentLeMemeDenominateur()
    {
        var e = new StatEluDetail { Scrutins = 200, Votants = 150, Delegations = 50, MisesAuPoint = 10 };
        Assert.Equal(75.0, e.ParticipationPct);
        Assert.Equal(50.0, e.PresencePct);      // votes exprimés sans délégation
        Assert.Equal(25.0, e.DelegationPct);
        Assert.Equal(5.0, e.MiseAuPointPct);
        Assert.Equal(25.0, e.AbsencePct);       // aucun vote exprimé
    }

    [Fact]
    public void SansScrutin_AucunTaux()
    {
        var e = new StatEluDetail();
        Assert.Null(e.ParticipationPct);
        Assert.Null(e.AbsencePct);
    }

    [Fact]
    public void LePointMensuelUtiliseLesMemesDefinitions()
    {
        var p = new PointMensuel { Scrutins = 40, Votants = 30, Delegations = 10 };
        Assert.Equal(75.0, p.ParticipationPct);
        Assert.Equal(50.0, p.PresencePct);
    }
}

/// <summary>Rattachement d'un scrutin d'amendement ou d'article au texte qu'il cite : même clé que le vote sur l'ensemble de ce texte.</summary>
public class TexteCiteTests
{
    [Fact]
    public void UnAmendement_ALaMemeCleQueLeVoteSurLEnsemble()
    {
        var amendement = "l'amendement n° 219 de M. Monnet à l'article 2 du projet de loi relatif à la lutte contre les fraudes sociales et fiscales (première lecture).";
        var ensemble = "l'ensemble du projet de loi relatif à la lutte contre les fraudes sociales et fiscales (première lecture).";
        Assert.Equal(TitreTexte.CleScrutin(ensemble), TitreTexte.CleTexteCite(amendement));
    }

    [Fact]
    public void UneProposition_EstReconnue() =>
        Assert.NotNull(TitreTexte.CleTexteCite("l'article 8 de la proposition de loi visant à protéger les enfants (première lecture)."));

    [Fact]
    public void SansTexteCite_Null() =>
        Assert.Null(TitreTexte.CleTexteCite("l'article unique de la proposition de résolution européenne visant à soutenir le Danemark."));
}
