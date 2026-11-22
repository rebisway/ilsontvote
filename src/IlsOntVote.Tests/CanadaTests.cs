using System.Xml.Linq;
using IlsOntVote.Importer;

namespace IlsOntVote.Tests;

/// <summary>Chambre des communes du Canada (données ouvertes officielles) : conversion d'une division, jumelés, caucus à la date du vote, numéro unique par session, vote final calculé, annuaire daté.</summary>
public class CanadaTests
{
    private static readonly DateOnly Debut = new(2025, 4, 28);

    private static MembreCa Membre(int id, params PartiCa[] partis) => new(id, "Prénom", "Nom", "Edmonton Manning", "Alberta", Debut, null, partis);

    private static DivisionCa Division(string sujet = "Motion", string resultat = "Agreed To", int session = 1, int numero = 196) =>
        new(45, session, numero, new DateTime(2026, 11, 19, 17, 45, 0), sujet, "Sujet fr", resultat, 2, 1, 16, "Ways and means", null);

    private static BulletinCa B(int id, string valeur) => new(id, valeur);

    [Theory]
    [InlineData("Yea", Positions.Pour)]
    [InlineData("Nay", Positions.Contre)]
    [InlineData("Paired", null)]      // jumelé : pas un vote
    [InlineData("Autre", null)]
    public void Position_d_apres_la_valeur(string valeur, string? attendu) => Assert.Equal(attendu, CanadaSource.Position(valeur));

    [Fact]
    public void Numero_unique_par_session_car_le_numero_de_la_source_repart_de_1()
    {
        Assert.NotEqual(CanadaSource.Numero(Division(session: 1, numero: 5)), CanadaSource.Numero(Division(session: 2, numero: 5)));
        Assert.Equal(10_196, CanadaSource.Numero(Division()));
    }

    [Theory]
    [InlineData("3rd reading and adoption of Bill C-79, An Act", true)]
    [InlineData("Third reading of Bill S-205", true)]
    [InlineData("2nd reading of Bill C-380, An Act", false)]
    [InlineData("Concurrence at report stage of Bill C-79", false)]
    public void Vote_final_calcule_d_apres_le_sujet(string sujet, bool attendu) => Assert.Equal(attendu, CanadaSource.EstVoteFinal(sujet));

    [Fact]
    public void Division_convertie_decomptes_officiels_caucus_a_la_date_jumeles_et_resultat()
    {
        var lib = new PartiCa("Liberal", Debut, null);
        var membres = new Dictionary<int, MembreCa> { [1] = Membre(1, lib), [2] = Membre(2), [3] = Membre(3, lib), [4] = Membre(4, lib) };
        var r = CanadaSource.VersScrutin(Division(), [B(1, "Yea"), B(2, "Yea"), B(3, "Nay"), B(4, "Paired"), B(99, "Yea")], membres, []);
        Assert.Equal("CACOMMONS-45-" + CanadaSource.Numero(Division()), r.Scrutin.Id);
        Assert.Equal("CACOMMONS-45-liberal", r.Votes.First(x => x.ParlementaireId == "CACOMMONS-1").GroupeId);
        Assert.Null(r.Votes.First(x => x.ParlementaireId == "CACOMMONS-2").GroupeId);   // sans caucus connu à cette date
        Assert.Equal(3, r.Votes.Count);                                                  // le jumelé (4) et l'inconnu (99) ne sont pas des votes
        Assert.DoesNotContain(r.Votes, v => v.ParlementaireId is "CACOMMONS-4");
        Assert.Equal(2, r.Scrutin.NbPour); Assert.Equal(1, r.Scrutin.NbContre);          // décompte OFFICIEL, pas celui des listes
        Assert.Equal("adopté", r.Scrutin.Sort);
        Assert.Contains("16 member(s) were paired", r.Scrutin.Note);
        Assert.Contains("no entry in the members directory", r.Scrutin.Note);
        Assert.Equal("Sujet fr", r.Scrutin.Titre); Assert.Equal("Motion", r.Scrutin.TitreEn);
    }

    [Fact]
    public void Resultat_inconnu_donne_aucun_sort_et_ecart_de_liste_est_dit()
    {
        var membres = new Dictionary<int, MembreCa> { [1] = Membre(1) };
        var r = CanadaSource.VersScrutin(Division(resultat: "Tie"), [B(1, "Yea")], membres, []);
        Assert.Null(r.Scrutin.Sort);                      // jamais deviné
        Assert.Equal(2, r.Scrutin.NbVotesManquants);      // 3 votes officiels, 1 nom : division incomplète
        Assert.Contains("Official count", r.Scrutin.Note);
    }

    [Fact]
    public void Annuaire_regroupe_les_periodes_par_depute()
    {
        var xml = XElement.Parse("""
            <A>
              <MemberOfParliament><PersonId>7</PersonId><PersonOfficialFirstName>Ada</PersonOfficialFirstName><PersonOfficialLastName>Lovelace</PersonOfficialLastName>
                <ConstituencyName>X</ConstituencyName><ConstituencyProvinceTerritoryName>Ontario</ConstituencyProvinceTerritoryName><CaucusShortName>Liberal</CaucusShortName>
                <FromDateTime>2021-09-20T00:00:00</FromDateTime><ToDateTime>2023-05-01T23:59:59</ToDateTime></MemberOfParliament>
              <MemberOfParliament><PersonId>7</PersonId><PersonOfficialFirstName>Ada</PersonOfficialFirstName><PersonOfficialLastName>Lovelace</PersonOfficialLastName>
                <ConstituencyName>X</ConstituencyName><ConstituencyProvinceTerritoryName>Ontario</ConstituencyProvinceTerritoryName><CaucusShortName>Independent</CaucusShortName>
                <FromDateTime>2023-05-02T00:00:00</FromDateTime><ToDateTime xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:nil="true" /></MemberOfParliament>
            </A>
            """);
        var m = Assert.Single(CanadaSource.ConvertirMembres(xml.Elements("MemberOfParliament")));
        Assert.Equal(new DateOnly(2021, 9, 20), m.Debut);
        Assert.Null(m.Fin);                               // une période ouverte garde le mandat ouvert
        Assert.Equal(2, m.Partis.Count);
        Assert.Equal("Liberal", CanadaSource.PartiA(m, new DateOnly(2022, 1, 1))!.Nom);
        Assert.Equal("Independent", CanadaSource.PartiA(m, new DateOnly(2024, 1, 1))!.Nom);
    }

    [Fact]
    public void Groupe_connu_a_son_sigle_et_son_ordre_inconnu_garde_son_nom()
    {
        var lib = CanadaSource.Groupe(45, "Liberal");
        Assert.Equal("Lib", lib.Sigle);
        var autre = CanadaSource.Groupe(45, "Parti Rhinocéros");
        Assert.Equal("Parti Rhinocéros", autre.Sigle);
        Assert.Equal(5, autre.Ordre);
    }
}
