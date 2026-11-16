using System.Text;
using IlsOntVote.Importer;
using IlsOntVote.Web.Models;
using IlsOntVote.Web.Services;

namespace IlsOntVote.Tests;

/// <summary>Périodes saisies dans l'adresse (?du=…&amp;au=…) : jamais d'erreur, bornes remises dans l'ordre.</summary>
public class PeriodeTests
{
    [Fact]
    public void UneDateValide_EstLue()
    {
        var (du, au) = IlsOntVote.Web.Services.Periode.Lire("2026-11-12", null);
        Assert.Equal(new DateTime(2026, 11, 12), du);
        Assert.Null(au);
    }

    [Fact]
    public void BornesInversees_SontRemisesDansLOrdre()
    {
        var (du, au) = IlsOntVote.Web.Services.Periode.Lire("2026-11-30", "2026-11-01");
        Assert.Equal(new DateTime(2026, 11, 1), du);
        Assert.Equal(new DateTime(2026, 11, 30), au);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("2026-13-45")]
    [InlineData("12/11/2026")]   // seul le format ISO du champ HTML <input type="date"> est accepté
    [InlineData("")]
    [InlineData(null)]
    public void ValeurIllisible_EstIgnoree(string? valeur)
    {
        var (du, au) = IlsOntVote.Web.Services.Periode.Lire(valeur, valeur);
        Assert.Null(du);
        Assert.Null(au);
    }

    [Fact]
    public void LaMemeDateDesDeuxCotes_DesigneCeJourLa()
    {
        var (du, au) = IlsOntVote.Web.Services.Periode.Lire("2026-11-12", "2026-11-12");
        Assert.Equal(du, au);
    }
}

/// <summary>Export : CSV lisible par Excel (séparateur « ; », UTF-8 avec BOM) et JSON avec accents lisibles.</summary>
public class ExporteurTests
{
    private record Ligne(string Titre, int? Pour);
    private static readonly IReadOnlyList<Colonne<Ligne>> Colonnes = [new("Titre", l => l.Titre), new("Pour", l => l.Pour)];

    [Fact]
    public void Csv_CommenceParLeBom_EtUtiliseLePointVirgule()
    {
        var r = Exporteur.Ecrire("csv", "test", Colonnes, [new Ligne("Loi « énergie »", 12)]);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, r.FileContents[..3]);
        var texte = Encoding.UTF8.GetString(r.FileContents).TrimStart('﻿');
        Assert.StartsWith("Titre;Pour\r\n", texte);
        Assert.Contains("Loi « énergie »;12", texte);
    }

    [Fact]
    public void Csv_EchappeLesPointsVirgulesEtLesGuillemets()
    {
        var r = Exporteur.Ecrire("csv", "test", Colonnes, [new Ligne("a;b \"c\"", null)]);
        var texte = Encoding.UTF8.GetString(r.FileContents);
        Assert.Contains("\"a;b \"\"c\"\"\"", texte);
    }

    [Fact]
    public void Json_GardeLesAccentsLisibles()
    {
        var r = Exporteur.Ecrire("json", "test", Colonnes, [new Ligne("Résultat", 3)]);
        var texte = Encoding.UTF8.GetString(r.FileContents);
        Assert.Contains("Résultat", texte);
        Assert.DoesNotContain("\\u00E9", texte);
    }

    [Theory]
    [InlineData("csv", true)]
    [InlineData("json", true)]
    [InlineData("xls", false)]
    [InlineData(null, false)]
    public void SeulsLesFormatsConnusSontAcceptes(string? format, bool attendu) => Assert.Equal(attendu, Exporteur.EstFormat(format));
}

/// <summary>Noms d'élus : le balisage contient toujours les deux parties (l'ordre est décidé par CSS) et échappe le HTML.</summary>
public class NomsTests
{
    [Fact]
    public void ContientPrenomEtNom_Echappes()
    {
        var html = Noms.Html("Jean <b>", "Dupont & Fils").Value!;
        Assert.Contains("np-p", html);
        Assert.Contains("np-n", html);
        Assert.Contains("Jean &lt;b&gt;", html);
        Assert.Contains("Dupont &amp; Fils", html);
    }
}

/// <summary>Hémicycle : un point par élu, jamais de chevauchement massif, dans le cadre du dessin.</summary>
public class HemicycleTests
{
    private static List<Siege> Sieges(int n) =>
        Enumerable.Range(0, n).Select(i => new Siege { ParlementaireId = "P" + i, Position = i % 2 == 0 ? Pos.Pour : Pos.Contre }).ToList();

    [Theory]
    [InlineData(1)]
    [InlineData(348)]   // Sénat
    [InlineData(577)]   // Assemblée nationale
    [InlineData(720)]   // Parlement européen
    public void ChaqueElu_ADesCoordonnees_DansLeCadre(int n)
    {
        var sieges = Sieges(n);
        var vue = HemicycleLayout.Construire(sieges);
        Assert.Equal(n, vue.Sieges.Count);
        Assert.All(vue.Sieges, s =>
        {
            Assert.InRange(s.X, 0, HemicycleLayout.Largeur);
            Assert.InRange(s.Y, 0, HemicycleLayout.Hauteur);
        });
        Assert.True(vue.Rayon > 0);
    }

    [Fact]
    public void AucunSiege_NeLeveAucuneErreur()
    {
        var vue = HemicycleLayout.Construire([]);
        Assert.Empty(vue.Sieges);
    }

    [Fact]
    public void DeuxElusDistincts_NeSontJamaisAuMemeEndroit()
    {
        var vue = HemicycleLayout.Construire(Sieges(577));
        var positions = vue.Sieges.Select(s => (Math.Round(s.X, 1), Math.Round(s.Y, 1))).ToHashSet();
        Assert.Equal(577, positions.Count);
    }
}

/// <summary>Rapprochement scrutin → texte : égalité de clés normalisées, jamais « presque pareil ».</summary>
public class TitreTexteTests
{
    [Fact]
    public void TitreDeScrutinEtDeTexte_DonnentLaMemeCle()
    {
        var cleScrutin = TitreTexte.CleScrutin("l'ensemble du projet de loi de finances pour 2026");
        var cleTexte = TitreTexte.CleTexte("Projet de loi", "de finances pour 2026");
        Assert.Equal(cleTexte, cleScrutin);
    }

    [Fact]
    public void DeuxTextesDifferents_NeSontPasConfondus()
    {
        Assert.NotEqual(TitreTexte.CleScrutin("l'ensemble du projet de loi de finances pour 2026"),
                        TitreTexte.CleScrutin("l'ensemble du projet de loi de finances pour 2027"));
    }
}

/// <summary>Adresses des fiches : sans accent, stables, uniques même pour les homonymes.</summary>
public class SlugsTests
{
    [Fact]
    public void RetireAccentsEtPonctuation() => Assert.Equal("jean-francois-dupont", Slugs.Slugify("Jean-François  Dupont"));

    [Fact]
    public void Homonymes_ObtiennentDesSlugsDistincts()
    {
        var deja = new HashSet<string>();
        var a = Slugs.Unique(deja, "Jean", "Dupont", "PA1");
        var b = Slugs.Unique(deja, "Jean", "Dupont", "PA2");
        Assert.NotEqual(a, b);
        Assert.Equal("jean-dupont", a);
    }
}

/// <summary>Ordre des groupes dans l'hémicycle (gauche → droite) : valeur initiale, y compris pour le Parlement européen.</summary>
public class GroupeOrdreTests
{
    [Fact]
    public void GaucheAvantDroite_AlAssemblee()
    {
        Assert.True(GroupeOrdre.Pour("LFI-NFP") < GroupeOrdre.Pour("SOC"));
        Assert.True(GroupeOrdre.Pour("SOC") < GroupeOrdre.Pour("EPR"));
        Assert.True(GroupeOrdre.Pour("EPR") < GroupeOrdre.Pour("RN"));
    }

    [Fact]
    public void GaucheAvantDroite_AuParlementEuropeen()
    {
        Assert.True(GroupeOrdre.Pour("The Left") < GroupeOrdre.Pour("S&D"));
        Assert.True(GroupeOrdre.Pour("S&D") < GroupeOrdre.Pour("EPP"));
        Assert.True(GroupeOrdre.Pour("EPP") < GroupeOrdre.Pour("ESN"));
    }
}

/// <summary>
/// Dossiers législatifs de l'Assemblée : une décision AVEC voteRefs est un scrutin public, SANS voteRefs c'est un vote sans scrutin
/// public (main levée), qui ne doit jamais être omis. Les décisions de commission et du Sénat sont ignorées.
/// </summary>
public class DossiersAnSourceTests : IDisposable
{
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "ilsontvote-tests-" + Guid.NewGuid().ToString("N"));

    public DossiersAnSourceTests() => Directory.CreateDirectory(_dossier);
    public void Dispose() => Directory.Delete(_dossier, recursive: true);

    private void Ecrire(string uid, string actes) =>
        File.WriteAllText(Path.Combine(_dossier, uid + ".json"),
            "{\"dossierParlementaire\":{\"uid\":\"" + uid + "\",\"titreDossier\":{\"titre\":\"T\",\"senatChemin\":\"http://www.senat.fr/dossier-legislatif/pjl24-123.html\"},"
            + "\"actesLegislatifs\":{\"acteLegislatif\":" + actes + "}}}");

    private static string Decision(string uid, string code, string date, string statut, string? voteRefs) =>
        "{\"uid\":\"" + uid + "\",\"codeActe\":\"" + code + "\",\"dateActe\":\"" + date + "\",\"statutConclusion\":{\"libelle\":\"" + statut + "\"},\"voteRefs\":" + (voteRefs ?? "null") + "}";

    [Fact]
    public void DecisionSansVoteRefs_EstUnVoteSansScrutinPublic_EtAvecVoteRefs_UnScrutinPublic()
    {
        Ecrire("DLR5L17N1", "[" +
            Decision("A1", "AN1-DEBATS-DEC", "2025-07-10T00:00:00.000+02:00", "modifiée", null) + "," +
            Decision("A2", "AN2-DEBATS-DEC", "2025-12-08T00:00:00.000+01:00", "adoptée sans modification", "{\"voteRef\":\"VTANR5L17V100\"}") + "]");

        var etapes = DossiersAnSource.Lire(_dossier).OrderBy(e => e.DateActe).ToList();

        Assert.Equal(2, etapes.Count);
        Assert.True(etapes[0].SansScrutinPublic);
        Assert.Null(etapes[0].ScrutinId);
        Assert.False(etapes[1].SansScrutinPublic);
        Assert.Equal("VTANR5L17V100", etapes[1].ScrutinId);
        Assert.Equal(new DateOnly(2025, 7, 10), etapes[0].DateActe);   // le jour de l'acte, sans décalage de fuseau
        Assert.Equal("pjl24-123", etapes[0].SenatChemin);              // clé de rattachement au texte
    }

    [Fact]
    public void PlusieursVotes_RetiennentLeDernier()
    {
        Ecrire("DLR5L17N2", "[" + Decision("B1", "AN1-DEBATS-DEC", "2025-01-01T00:00:00+01:00", "adopté", "{\"voteRef\":[\"VT1\",\"VT2\"]}") + "]");
        Assert.Equal("VT2", DossiersAnSource.Lire(_dossier).Single().ScrutinId);
    }

    [Fact]
    public void CommissionEtSenat_SontIgnores()
    {
        Ecrire("DLR5L17N3", "[" +
            Decision("C1", "ANLUNI-COM-CAE-DEC", "2025-01-01T00:00:00+01:00", "adopté", null) + "," +
            Decision("C2", "SN1-DEBATS-DEC", "2025-01-02T00:00:00+01:00", "adopté", null) + "]");
        Assert.Empty(DossiersAnSource.Lire(_dossier));
    }
}
