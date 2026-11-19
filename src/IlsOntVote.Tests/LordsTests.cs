using IlsOntVote.Importer;

namespace IlsOntVote.Tests;

/// <summary>Chambre des Lords : analyse des divisions et de l'annuaire des pairs, conversion en scrutin + votes.</summary>
public class LordsTests
{
    private static string Pair(int id, string nom, string sigle, string parti) =>
        $$"""{"memberId":{{id}},"name":"{{nom}}","listAs":"x","memberFrom":"Life peer","party":"{{parti}}","partyColour":"0063ba","partyAbbreviation":"{{sigle}}","partyIsMainParty":true}""";

    private static string Division(int id, string date, string titre, string notes, int content, int notContent, string contents, string notContents) =>
        $$"""[{"divisionId":{{id}},"date":"{{date}}T00:00:00","number":1,"title":"{{titre}}","amendmentMotionNotes":"{{notes}}","authoritativeContentCount":{{content}},"authoritativeNotContentCount":{{notContent}},"contents":[{{contents}}],"notContents":[{{notContents}}]}]""";

    [Fact]
    public void Division_analysee_avec_votants_et_notes_sans_html()
    {
        var json = Division(3740, "2026-11-18", "Railways Bill", "<p>Baroness X moved amendment 16 &ldquo;a&rdquo;.</p>", 2, 1,
            Pair(1, "Lord A", "Con", "Conservative") + "," + Pair(2, "Baroness B", "Lab", "Labour"), Pair(3, "Lord C", "", "Crossbench"));
        var d = LordsSource.AnalyserPage(json).Single();
        Assert.Equal(new DateOnly(2026, 11, 18), d.Date);
        Assert.Equal(2, d.Content.Count);
        Assert.Equal("Crossbench", d.NonContent[0].Sigle);   // sigle vide : le libellé du parti sert de sigle
        Assert.DoesNotContain("<", d.Notes);
        Assert.Contains("moved amendment 16", d.Notes);
    }

    [Fact]
    public void Conversion_content_pour_not_content_contre_et_note_sur_les_absents_toujours_presente()
    {
        var d = LordsSource.AnalyserPage(Division(7, "2025-03-04", "Test Bill", "", 2, 1, Pair(1, "A", "Con", "Conservative") + "," + Pair(2, "B", "Lab", "Labour"), Pair(3, "C", "LD", "Liberal Democrat"))).Single();
        var r = LordsSource.VersScrutin(d);
        Assert.Equal("LORDS-7", r.Scrutin.Id);
        Assert.Equal("adopté", r.Scrutin.Sort);
        Assert.Equal(2, r.Votes.Count(v => v.Position == Positions.Pour));
        Assert.Equal(1, r.Votes.Count(v => v.Position == Positions.Contre));
        Assert.Equal(0, r.Scrutin.NbVotesManquants);
        Assert.Contains("deduced from their membership dates", r.Scrutin.Note);   // la limite des absents est dite sur chaque division
    }

    [Fact]
    public void Decompte_officiel_superieur_aux_listes_marque_les_votes_manquants()
    {
        var d = LordsSource.AnalyserPage(Division(8, "2025-03-04", "Test Bill", "", 5, 1, Pair(1, "A", "Con", "Conservative"), Pair(3, "C", "LD", "Liberal Democrat"))).Single();
        var r = LordsSource.VersScrutin(d);
        Assert.Equal(4, r.Scrutin.NbVotesManquants);
        Assert.Contains("Official count: 5 content", r.Scrutin.Note);
    }

    [Fact]
    public void Annuaire_des_pairs_dates_sans_date_d_entree_ignore()
    {
        var json = """{"items":[{"value":{"id":631,"nameDisplayAs":"Baroness Adams of Craigielea","latestParty":{"name":"Labour","abbreviation":"Lab","backgroundColour":"d50000"},"latestHouseMembership":{"membershipStartDate":"2005-06-28T00:00:00","membershipEndDate":null}}},{"value":{"id":9,"nameDisplayAs":"Sans date","latestHouseMembership":{"membershipStartDate":null}}}]}""";
        var pairs = LordsSource.AnalyserPairs(json);
        var p = Assert.Single(pairs);   // sans date d'entrée : aucun mandat déduit, jamais inventé
        Assert.Equal("Baroness Adams of Craigielea", p.Nom);
        Assert.Equal(new DateOnly(2005, 6, 28), p.Debut);
        Assert.Null(p.Fin);
        Assert.Equal("Lab", p.Sigle);
    }
}
