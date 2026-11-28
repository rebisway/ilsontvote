using System.Text;
using IlsOntVote.Importer;

namespace IlsOntVote.Tests;

/// <summary>Pétitions du Canada (fichier XML téléversé) : lecture, dates en toutes lettres, fichier partiel, données personnelles non lues.</summary>
public class PetitionsCanadaTests
{
    private const string Xml = """
        <Results RecordsFound="3"><Petitions>
          <Petition Id="1" ParliamentNumber="45" Session="1" SignatureCount="11">
            <StatusName>Ouverte pour signature</StatusName><PetitionNumber>e-8003</PetitionNumber><Title>e-8003 (Santé)</Title>
            <PetitionerFirstName>Prénom</PetitionerFirstName><PetitionerLastName>Secret</PetitionerLastName>
            <SignatureOpeningDateTime>27 novembre 2026 à 15 h 20 (HAE)</SignatureOpeningDateTime>
            <SignatureClosingDateTime>1er mars 2027 à 15 h 20 (HAE)</SignatureClosingDateTime>
            <Prayer><Petition><Title>Pétition</Title><Grievances>Attendu que :<WhereAs>un <i>premier</i> point;</WhereAs></Grievances></Petition></Prayer>
          </Petition>
          <Petition Id="2" ParliamentNumber="45" Session="1">
            <StatusName>Présentée à la Chambre des communes</StatusName><PetitionNumber>451-01423</PetitionNumber><Title>451-01423 (Médias)</Title>
            <PresentedDateTime>27 novembre 2026</PresentedDateTime>
          </Petition>
          <Petition Id="3"><Title>sans numéro</Title></Petition>
        </Petitions></Results>
        """;

    private static FichierPetitionsCanada Lire(string xml) => PetitionsCanadaSource.Lire(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    [Fact]
    public void Lit_les_petitions_et_signale_un_fichier_partiel()
    {
        var f = Lire(Xml);
        Assert.Equal(2, f.Petitions.Count);
        Assert.True(f.Partiel);
        var e = f.Petitions.Single(p => p.Id == "CA-e-8003");
        Assert.Equal(("CA", 11, new DateTime(2026, 11, 27), new DateTime(2027, 3, 1)), (e.Chambre, e.NbVotes, e.DatePublication, e.DateLimiteVote));
        Assert.Contains("un premier point;", e.Description);
        Assert.DoesNotContain("Secret", e.ToString());                       // nom du pétitionnaire jamais lu
        var papier = f.Petitions.Single(p => p.Id == "CA-451-01423");
        Assert.Null(papier.NbVotes);                                         // manquant ≠ zéro
        Assert.Equal(new DateTime(2026, 11, 27), papier.DatePublication);
    }

    [Theory]
    [InlineData("November 27, 2026 at 3:20 p.m. (EDT)", true)]
    [InlineData("bientôt", false)]
    [InlineData(null, false)]
    public void Date_en_toutes_lettres(string? valeur, bool lisible) => Assert.Equal(lisible ? new DateTime(2026, 11, 27) : null, PetitionsCanadaSource.LireDate(valeur));

    [Fact]
    public void Refuse_un_autre_fichier() => Assert.Throws<InvalidDataException>(() => Lire("<html><body/></html>"));
}
