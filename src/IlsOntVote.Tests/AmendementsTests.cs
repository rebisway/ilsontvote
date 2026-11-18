using System.IO.Compression;
using IlsOntVote.Importer;

namespace IlsOntVote.Tests;

/// <summary>Import officiel des amendements : classement d'après les libellés de la source et lecture du zip en flux.</summary>
public class AmendementsTests
{
    [Theory]
    [InlineData("Adopté", "Discuté", AmendementsSource.Categorie.Adopte)]
    [InlineData("Rejeté", "Discuté", AmendementsSource.Categorie.Rejete)]
    [InlineData("Tombé", "Discuté", AmendementsSource.Categorie.Tombe)]
    [InlineData("Non soutenu", "Discuté", AmendementsSource.Categorie.NonSoutenu)]
    [InlineData("Retiré", "Discuté", AmendementsSource.Categorie.Retire)]
    [InlineData(null, "Retiré", AmendementsSource.Categorie.Retire)]            // retiré avant discussion : pas de « sort », seulement l'état
    [InlineData(null, "Irrecevable 40", AmendementsSource.Categorie.Irrecevable)]
    [InlineData(null, "Irrecevable", AmendementsSource.Categorie.Irrecevable)]
    [InlineData(null, "A discuter", AmendementsSource.Categorie.Autre)]          // pas encore de sort : compte dans les déposés seulement
    [InlineData(null, "En traitement", AmendementsSource.Categorie.Autre)]
    [InlineData("ADOPTE", null, AmendementsSource.Categorie.Adopte)]             // casse et accents non significatifs
    public void Classer_suit_les_libelles_de_la_source(string? sort, string? etat, AmendementsSource.Categorie attendu) =>
        Assert.Equal(attendu, AmendementsSource.Classer(sort, etat));

    private static void Ajouter(ZipArchive zip, string chemin, string json)
    {
        using var w = new StreamWriter(zip.CreateEntry(chemin).Open());
        w.Write(json);
    }

    [Fact]
    public void Lire_totalise_par_dossier_et_ignore_incorrect_data()
    {
        var chemin = Path.Combine(Path.GetTempPath(), $"amdt-{Guid.NewGuid():N}.zip");
        using (var zip = ZipFile.Open(chemin, ZipArchiveMode.Create))
        {
            // sort « nil » de la source : objet et non texte -> amendement non traité.
            Ajouter(zip, "json/DLR5L17N1/PIONANR5L17B1/A1.json", """{"amendement":{"cycleDeVie":{"sort":{"@xsi:nil":"true"},"etatDesTraitements":{"etat":{"libelle":"A discuter"}}}}}""");
            Ajouter(zip, "json/DLR5L17N1/PIONANR5L17B1/A2.json", """{"amendement":{"cycleDeVie":{"sort":"Adopté","etatDesTraitements":{"etat":{"libelle":"Discuté"}}}}}""");
            Ajouter(zip, "json/DLR5L17N1/PIONANR5L17B1/A3.json", """{"amendement":{"cycleDeVie":{"sort":"Rejeté"}}}""");
            Ajouter(zip, "json/DLR5L17N2/PIONANR5L17B2/A4.json", """{"amendement":{"cycleDeVie":{"etatDesTraitements":{"etat":{"libelle":"Irrecevable 40"}}}}}""");
            Ajouter(zip, "json/incorrect_data/PIONANR5L17BTC2202/A5.json", """{"amendement":{"cycleDeVie":{"sort":"Adopté"}}}""");
            Ajouter(zip, "json/DLR5L17N2/PIONANR5L17B2/A6.json", "{ pas du json");
        }
        var l = AmendementsSource.Lire(chemin, out var sansDossier, out var illisibles);
        Assert.Equal(1, sansDossier);
        Assert.Equal(1, illisibles);
        Assert.Equal(2, l.Count);
        var d1 = l.Single(x => x.DossierAn == "DLR5L17N1");
        Assert.Equal((3, 1, 1), (d1.Deposes, d1.Adoptes, d1.Rejetes));
        var d2 = l.Single(x => x.DossierAn == "DLR5L17N2");
        Assert.Equal((1, 1), (d2.Deposes, d2.Irrecevables));
    }
}
