using IlsOntVote.Importer;

namespace IlsOntVote.Tests;

/// <summary>Import des divisions de la Chambre des Communes : analyse du JSON et conversion en scrutin + votes.</summary>
public class CommonsTests
{
    private static string Membre(int id, string nom, string sigle, string parti, string? proxy = null) =>
        $$"""{"MemberId":{{id}},"Name":"{{nom}}","Party":"{{parti}}","PartyAbbreviation":"{{sigle}}","PartyColour":"d50000","MemberFrom":"Quelque part","ListAs":"{{nom.Split(' ')[1]}}, {{nom.Split(' ')[0]}}","ProxyName":{{(proxy is null ? "null" : $"\"{proxy}\"")}}}""";

    private static string Division(int officielPour, int officielContre, string ayes, string noes, string sans, string ayeTellers = "", string noTellers = "") =>
        $$"""{"DivisionId":2000,"Number":12,"Date":"2026-11-18T17:00:00","Title":"Test Bill Second Reading","AyeCount":{{officielPour}},"NoCount":{{officielContre}},"AyeTellers":[{{ayeTellers}}],"NoTellers":[{{noTellers}}],"Ayes":[{{ayes}}],"Noes":[{{noes}}],"NoVoteRecorded":[{{sans}}]}""";

    [Fact]
    public void Division_simple_pour_contre_et_sans_vote_non_stocke()
    {
        var json = Division(2, 1,
            Membre(1, "Anna Pour", "Lab", "Labour") + "," + Membre(2, "Bob Pour", "Lab", "Labour", proxy: "Chief Whip"),
            Membre(3, "Carl Contre", "Con", "Conservative"),
            Membre(4, "Dora Absente", "Con", "Conservative"));
        var sc = CommonsSource.VersScrutin(CommonsSource.Analyser(json));
        Assert.Equal("COMMONS-2000", sc.Scrutin.Id);
        Assert.Equal("adopté", sc.Scrutin.Sort);
        Assert.Equal(3, sc.Votes.Count);                                   // « sans vote enregistré » n'est jamais stocké : absent = absent
        Assert.True(sc.Votes.Single(v => v.ParlementaireId == "COMMONS-2").ParDelegation);   // vote par procuration
        Assert.Equal("contre", sc.Votes.Single(v => v.ParlementaireId == "COMMONS-3").Position);
        Assert.Equal(0, sc.Scrutin.NbVotesManquants);
        Assert.Null(sc.Scrutin.Note);
    }

    [Fact]
    public void Membre_dans_les_deux_listes_est_une_abstention_et_le_dit()
    {
        var double_ = Membre(9, "Eve Deux", "Green", "Green Party");
        var json = Division(2, 2, Membre(1, "Anna Pour", "Lab", "Labour") + "," + double_, Membre(3, "Carl Contre", "Con", "Conservative") + "," + double_, "");
        var sc = CommonsSource.VersScrutin(CommonsSource.Analyser(json));
        Assert.Equal(3, sc.Votes.Count);                                   // pas de clé (scrutin, membre) en double
        Assert.Equal("abstention", sc.Votes.Single(v => v.ParlementaireId == "COMMONS-9").Position);
        Assert.Equal(1, sc.Scrutin.NbAbstentions);
        Assert.Contains("both lobbies", sc.Scrutin.Note);
        Assert.Null(sc.Scrutin.Sort);                                      // 2 contre 2 : égalité, résultat indéterminé, rien d'inventé
    }

    [Fact]
    public void Listes_differentes_du_decompte_officiel_marquent_le_scrutin_incomplet()
    {
        var json = Division(5, 3, Membre(1, "Anna Pour", "Lab", "Labour"), Membre(3, "Carl Contre", "Con", "Conservative"), "");
        var sc = CommonsSource.VersScrutin(CommonsSource.Analyser(json));
        Assert.Equal(6, sc.Scrutin.NbVotesManquants);                      // 8 attendus, 2 nommés
        Assert.Contains("Official count: 5 ayes, 3 noes", sc.Scrutin.Note);
    }

    [Fact]
    public void Scrutateurs_comptent_dans_leur_camp_sans_doublon()
    {
        var teller = Membre(7, "Tom Teller", "Lab", "Labour");
        // Décompte officiel = liste Ayes seule (1) ; le scrutateur, listé à part, est ajouté aux votes sans créer de « manquant ».
        var json = Division(1, 0, Membre(1, "Anna Pour", "Lab", "Labour"), "", "", ayeTellers: teller);
        var sc = CommonsSource.VersScrutin(CommonsSource.Analyser(json));
        Assert.Equal(2, sc.Votes.Count(v => v.Position == "pour"));
        Assert.Equal(0, sc.Scrutin.NbVotesManquants);
        Assert.Null(sc.Scrutin.Note);
    }

    [Fact]
    public void Liste_absente_ou_nulle_ne_plante_pas_et_sigle_vide_prend_le_libelle()
    {
        // Président de séance : « PartyAbbreviation » vide, « Party » = « Deputy Speaker » ; divisions différées : listes nulles.
        var json = """{"DivisionId":1,"Number":1,"Date":"2026-11-18T10:00:00","Title":"T","AyeCount":0,"NoCount":0,"Ayes":null,"Noes":null,"AyeTellers":null,"NoTellers":null,"NoVoteRecorded":[{"MemberId":5,"Name":"Judith Cummins","Party":"Deputy Speaker","PartyAbbreviation":"","PartyColour":"","MemberFrom":"X","ListAs":"Cummins, Judith","ProxyName":null}]}""";
        var d = CommonsSource.Analyser(json);
        Assert.Empty(d.Pour);
        var m = Assert.Single(d.SansVote);
        Assert.Equal("Deputy Speaker", m.PartiSigle);
        Assert.Equal("Cummins", m.Nom);
        Assert.Equal(135, CommonsSource.OrdreParti(m.PartiSigle));
    }

    [Theory]
    [InlineData("Con", 90)]
    [InlineData("Lab", 40)]
    [InlineData("inconnu", 100)]
    public void Ordre_des_partis(string sigle, int attendu) => Assert.Equal(attendu, CommonsSource.OrdreParti(sigle));
}
