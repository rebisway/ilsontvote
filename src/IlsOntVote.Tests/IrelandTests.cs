using System.Text.Json;
using IlsOntVote.Importer;

namespace IlsOntVote.Tests;

/// <summary>Oireachtas (API officielle) : conversion d'une division, parti à la date du vote, numéro unique, vote final calculé, listes nulles du Seanad.</summary>
public class IrelandTests
{
    private static MembreIe Membre(string code, params PartiIe[] partis) => new(code, "Prénom", "Nom", "Dublin", new DateOnly(2024, 12, 18), null, partis);

    private static DivisionIe Division(string issue = "Carried", string question = "That the motion be agreed to.", params VoteIe[] votes) =>
        new(IrelandSource.Dail, 34, new DateOnly(2026, 11, 18), "vote_252", "Horse Racing Fund: Motion", question, issue, 2, 1, 1, votes);

    private static VoteIe V(string code, string liste) => new(code, liste);

    [Theory]
    [InlineData("ta", Positions.Pour)]
    [InlineData("nil", Positions.Contre)]
    [InlineData("staon", Positions.Abstention)]
    [InlineData("autre", null)]
    public void Position_d_apres_la_liste(string liste, string? attendu) => Assert.Equal(attendu, IrelandSource.Position(liste));

    [Fact]
    public void Numero_unique_par_date_et_numero_de_vote()
    {
        var a = Division(); var b = a with { Date = a.Date.AddDays(1) };
        Assert.NotEqual(IrelandSource.Numero(a), IrelandSource.Numero(b));   // même « vote_252 » à deux dates : deux numéros
        Assert.Equal(252, IrelandSource.NumeroVote("vote_252"));
        Assert.Equal(0, IrelandSource.NumeroVote("n'importe quoi"));
    }

    [Fact]
    public void Division_convertie_totaux_officiels_parti_a_la_date_et_resultat()
    {
        var ff = new PartiIe("Fianna_Fáil", "Fianna Fáil", new DateOnly(2024, 12, 18), null);
        var membres = new Dictionary<string, MembreIe> { ["a"] = Membre("a", ff), ["b"] = Membre("b"), ["c"] = Membre("c", ff) };
        var r = IrelandSource.VersScrutin(Division(votes: [V("a", "ta"), V("b", "ta"), V("c", "nil"), V("a2", "staon")]), membres, []);
        Assert.Equal("IEDAIL-34-" + IrelandSource.Numero(Division()), r.Scrutin.Id);
        Assert.Equal("IEDAIL-34-Fianna_Fáil", r.Votes.First(x => x.ParlementaireId == "IEDAIL-a").GroupeId);
        Assert.Null(r.Votes.First(x => x.ParlementaireId == "IEDAIL-b").GroupeId);      // sans parti connu à cette date
        Assert.Equal("adopté", r.Scrutin.Sort);
        Assert.Equal(2, r.Scrutin.NbPour); Assert.Equal(1, r.Scrutin.NbContre); Assert.Equal(1, r.Scrutin.NbAbstentions);
        Assert.Contains("Sitting of 2026-11-18, vote 252.", r.Scrutin.Note);
        Assert.Null(r.Scrutin.DossierRef);                                              // aucune adresse officielle vérifiée : rien d'inventé
    }

    [Fact]
    public void Membre_inconnu_ignore_et_dit_dans_la_note()
    {
        var inconnus = new HashSet<string>();
        var r = IrelandSource.VersScrutin(Division(votes: [V("a", "ta"), V("x", "nil")]), new Dictionary<string, MembreIe> { ["a"] = Membre("a") }, inconnus);
        Assert.Single(r.Votes);
        Assert.Contains("x", inconnus);
        Assert.Contains("no entry in the members directory", r.Scrutin.Note);
    }

    [Fact]
    public void Resultat_inconnu_laisse_vide_et_ecart_dit()
    {
        var r = IrelandSource.VersScrutin(Division(issue: "_", votes: [V("a", "ta")]), new Dictionary<string, MembreIe> { ["a"] = Membre("a") }, []);
        Assert.Null(r.Scrutin.Sort);                      // aucun résultat deviné
        Assert.Contains("Official count", r.Scrutin.Note);
        Assert.Equal(3, r.Scrutin.NbVotesManquants);
    }

    [Fact]
    public void Vote_final_calcule_d_apres_la_question_et_nettoyage_du_texte()
    {
        Assert.True(IrelandSource.EstVoteFinal("That the Bill do now pass."));
        Assert.False(IrelandSource.EstVoteFinal("That the motion be agreed to."));
        Assert.Equal("That the Bill do now pass.", IrelandSource.Nettoyer("Question put: : That the Bill do now pass. "));
    }

    [Fact]
    public void Parti_a_la_date_prend_l_appartenance_qui_couvre_le_jour()
    {
        var m = Membre("a", new PartiIe("Labour_Party", "Labour Party", new DateOnly(2024, 12, 18), new DateOnly(2025, 6, 30)), new PartiIe("Independent", "Independent", new DateOnly(2025, 7, 1), null));
        Assert.Equal("Labour_Party", IrelandSource.PartiA(m, new DateOnly(2025, 6, 30))!.Code);
        Assert.Equal("Independent", IrelandSource.PartiA(m, new DateOnly(2025, 7, 1))!.Code);
        Assert.Null(IrelandSource.PartiA(m, new DateOnly(2020, 1, 1)));
    }

    [Fact]
    public void Division_json_convertie_listes_nulles_du_seanad_comprises()
    {
        const string json = """
        {"date":"2026-11-19","voteId":"vote_7","outcome":"Lost","subject":{"showAs":"Question put: \"That the motion be agreed to.\""},"debate":{"showAs":"Motion"},
         "tallies":{"taVotes":{"tally":1,"members":[{"member":{"memberCode":"A.S.2020-01-01","showAs":"A"}}]},"nilVotes":{"tally":0,"members":[]},"staonVotes":null}}
        """;
        using var doc = JsonDocument.Parse(json);
        var d = IrelandSource.Convertir(doc.RootElement, IrelandSource.Seanad, 27)!;
        Assert.Equal(new DateOnly(2026, 11, 19), d.Date);
        Assert.Equal(1, d.Ta); Assert.Equal(0, d.Staon);
        Assert.Single(d.Votes);
        Assert.Equal("\"That the motion be agreed to.\"", d.Question);   // « Question put: » retiré, guillemets de la source conservés
        Assert.Equal("IESEANAD", IrelandSource.IdChambre(d.Maison));
    }

    [Fact]
    public void Membre_json_filtre_sur_la_mandature_demandee()
    {
        const string json = """
        {"memberCode":"A.D.2020-02-08","firstName":"Ann","lastName":"Byrne","memberships":[
          {"membership":{"house":{"houseCode":"dail","houseNo":"33"},"dateRange":{"start":"2020-02-08","end":"2024-11-08"},"parties":[],"represents":[]}},
          {"membership":{"house":{"houseCode":"dail","houseNo":"34"},"dateRange":{"start":"2024-11-29","end":null},
            "parties":[{"party":{"partyCode":"Labour_Party","showAs":"Labour Party","dateRange":{"start":"2024-11-29","end":null}}}],
            "represents":[{"represent":{"showAs":"Dublin Bay South"}}]}}]}
        """;
        using var doc = JsonDocument.Parse(json);
        var m = IrelandSource.ConvertirMembre(doc.RootElement, IrelandSource.Dail, 34)!;
        Assert.Equal(new DateOnly(2024, 11, 29), m.Debut); Assert.Null(m.Fin);
        Assert.Equal("Dublin Bay South", m.Circonscription);
        Assert.Equal("Labour_Party", m.Partis.Single().Code);
        Assert.Null(IrelandSource.ConvertirMembre(doc.RootElement, IrelandSource.Seanad, 27));   // jamais membre de cette mandature
    }
}
