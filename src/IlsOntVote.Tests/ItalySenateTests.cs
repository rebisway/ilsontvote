using IlsOntVote.Importer;

namespace IlsOntVote.Tests;

/// <summary>Sénat d'Italie (SPARQL officiel) : conversion d'une votation, groupe à la date du vote, scrutin secret, titre du projet de loi.</summary>
public class ItalySenateTests
{
    private static VotazioneSen Vote(string tipo = "elettronica", string titre = "Votazione finale", string esito = "approvato", string? ddlTitre = null) =>
        new("http://dati.senato.it/votazione/19-103-1", "19-103-1", 19, 103, 1, new DateOnly(2023, 5, 10), titre, tipo, 2, 1, 1, 5, 4, 3, esito, 1,
            "https://dati.senato.it/ddl/56299", ddlTitre is null ? null : "S.403", ddlTitre);

    private static VotoSen V(string id, string liste) => new("u", id, liste);

    private static readonly Func<string, DateOnly, string?> Groupe = (s, d) => s == "1" ? "56" : null;

    [Theory]
    [InlineData("favorevole", Positions.Pour)]
    [InlineData("contrario", Positions.Contre)]
    [InlineData("astenuto", Positions.Abstention)]
    [InlineData("presente", null)]
    public void Position_d_apres_la_liste(string liste, string? attendu) => Assert.Equal(attendu, ItalySenateSource.Position(liste));

    [Fact]
    public void Votation_convertie_totaux_officiels_groupe_a_la_date_et_resultat()
    {
        var votes = new[] { V("1", "favorevole"), V("2", "favorevole"), V("3", "contrario"), V("4", "astenuto") };
        var r = ItalySenateSource.VersScrutin(Vote(), votes, new HashSet<string> { "1", "2", "3", "4" }, [], Groupe);
        Assert.Equal("ITSENATE-19-1030001", r.Scrutin.Id);
        Assert.Equal(4, r.Votes.Count);
        Assert.Equal("ITSENATE-19-g56", r.Votes.First(x => x.ParlementaireId == "ITSENATE-1").GroupeId);
        Assert.Null(r.Votes.First(x => x.ParlementaireId == "ITSENATE-2").GroupeId);   // sans groupe connu à cette date
        Assert.Equal("adopté", r.Scrutin.Sort);
        Assert.True(r.Scrutin.SurEnsemble);                                              // libellé « Votazione finale »
        Assert.Equal(0, r.Scrutin.NbVotesManquants);
        Assert.Equal("https://dati.senato.it/ddl/56299", r.Scrutin.DossierRef);
        Assert.Contains("Sitting 103, vote 1.", r.Scrutin.Note);
    }

    [Fact]
    public void Titre_du_vote_final_reprend_le_projet_de_loi()
    {
        var r = ItalySenateSource.VersScrutin(Vote(ddlTitre: "Sport dans les écoles"), [], new HashSet<string>(), [], Groupe);
        Assert.Equal("Votazione finale: S.403 — Sport dans les écoles", r.Scrutin.Titre);
    }

    [Fact]
    public void Scrutin_secret_totaux_seuls_et_resultat_inconnu_laisse_vide()
    {
        var r = ItalySenateSource.VersScrutin(Vote(tipo: "segreta", esito: "", titre: "Em. 1.1"), [V("1", "favorevole")], new HashSet<string> { "1" }, [], Groupe);
        Assert.Empty(r.Votes);
        Assert.Equal(4, r.Scrutin.NbVotesManquants);
        Assert.Null(r.Scrutin.Sort);                      // aucun résultat deviné
        Assert.False(r.Scrutin.SurEnsemble);
        Assert.Contains("Secret ballot", r.Scrutin.Note);
    }

    [Fact]
    public void Senateur_inconnu_dit_dans_la_note_et_compte()
    {
        var inconnus = new HashSet<string>();
        var r = ItalySenateSource.VersScrutin(Vote(), [V("1", "favorevole"), V("9", "contrario")], new HashSet<string> { "1" }, inconnus, Groupe);
        Assert.Contains("no entry in the members directory", r.Scrutin.Note);
        Assert.Contains("9", inconnus);
        Assert.Single(r.Votes);
    }

    [Fact]
    public void Groupe_a_la_date_prend_l_appartenance_qui_couvre_le_jour()
    {
        var a = new[]
        {
            new AdesioneSen("1", "10", new DateOnly(2022, 10, 13), new DateOnly(2023, 1, 31)),
            new AdesioneSen("1", "20", new DateOnly(2023, 2, 1), null),
        };
        Assert.Equal("10", ItalySenateSource.GroupeA(a, "1", new DateOnly(2023, 1, 31)));
        Assert.Equal("20", ItalySenateSource.GroupeA(a, "1", new DateOnly(2023, 2, 1)));
        Assert.Null(ItalySenateSource.GroupeA(a, "1", new DateOnly(2022, 1, 1)));
        Assert.Null(ItalySenateSource.GroupeA(a, "2", new DateOnly(2023, 2, 1)));
    }

    [Fact]
    public void Ligne_de_resultat_convertie()
    {
        var l = new Dictionary<string, string>
        {
            ["v"] = "http://dati.senato.it/votazione/19-103-1", ["d"] = "2023-05-10", ["ns"] = "103", ["n"] = "1", ["l"] = "Votazione finale", ["t"] = "elettronica",
            ["fav"] = "145", ["con"] = "0", ["ast"] = "0", ["pre"] = "146", ["vot"] = "145", ["mag"] = "73", ["esi"] = "approvato", ["cm"] = "39",
            ["dd"] = "http://dati.senato.it/ddl/56299", ["df"] = "S.403", ["dt"] = "Disposizioni &amp;amp; altro",
        };
        var v = ItalySenateSource.Convertir(l, 19)!;
        Assert.Equal(103, v.Seduta); Assert.Equal(1, v.Numero); Assert.Equal(new DateOnly(2023, 5, 10), v.Date);
        Assert.Equal("https://dati.senato.it/ddl/56299", v.DdlUri);
        Assert.Equal("Disposizioni & altro", v.DdlTitre);
        Assert.Equal(1030001, ItalySenateSource.Numero(v));
    }

    [Theory]
    [InlineData("http://dati.senato.it/senatore/17811", "17811")]
    [InlineData("http://dati.senato.it/gruppo/56", "56")]
    [InlineData("n'importe quoi", null)]
    public void Numero_d_une_adresse(string uri, string? attendu) => Assert.Equal(attendu, ItalySenateSource.NumeroDe(uri));
}
