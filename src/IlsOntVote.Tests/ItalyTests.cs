using IlsOntVote.Importer;

namespace IlsOntVote.Tests;

/// <summary>Chambre des députés d'Italie (SPARQL officiel) : conversion d'une votation et de ses votes, scrutin secret, noms, groupes, texte de la source.</summary>
public class ItalyTests
{
    private static VotazioneIt Vote(bool secret = false, bool fiducia = false, int fav = 2, int con = 1, int ast = 1) =>
        new("http://dati.camera.it/ocd/votazione.rdf/vs19_009_074", "vs19_009_074", 19, 9, 74, new DateOnly(2022, 11, 10), "Votazione finale DDL n. 0005", "Conversione in legge del decreto", "Finale atto Camera",
            fav, con, ast, fav + con + ast, fav + con, 2, true, true, fiducia, secret, "https://documenti.camera.it/x");

    private static VotoIt V(string dep, string type, string grp = "http://dati.camera.it/ocd/gruppoParlamentare.rdf/gr4133", string sigle = "FDI") => new("u", dep, type, grp, sigle);

    [Theory]
    [InlineData("DE LUCA", "De Luca")]
    [InlineData("D'ALESSANDRO", "D'Alessandro")]
    [InlineData("MARIA-GRAZIA", "Maria-Grazia")]
    [InlineData("  fabio ", "Fabio")]
    public void Casse_des_noms_refaite(string brut, string attendu) => Assert.Equal(attendu, ItalyCameraSource.CasseNom(brut));

    [Fact]
    public void Texte_de_la_source_est_decode_deux_fois() =>
        Assert.Equal("produttività delle imprese", ItalyCameraSource.Nettoyer("produttivit&amp;agrave; delle  imprese"));

    [Theory]
    [InlineData("Favorevole", Positions.Pour)]
    [InlineData("Contrario", Positions.Contre)]
    [InlineData("Astensione", Positions.Abstention)]
    [InlineData("Non ha votato", null)]   // absent : non stocké
    [InlineData("Ha votato", null)]       // scrutin secret : on ne sait pas ce qu'il a voté
    public void Position_d_apres_le_type(string type, string? attendu) => Assert.Equal(attendu, ItalyCameraSource.Position(type));

    [Fact]
    public void Votation_convertie_totaux_officiels_et_absents_non_stockes()
    {
        var votes = new[] { V("1", "Favorevole"), V("2", "Favorevole"), V("3", "Contrario"), V("4", "Astensione"), V("5", "Non ha votato") };
        var r = ItalyCameraSource.VersScrutin(Vote(), votes, new HashSet<string> { "1", "2", "3", "4", "5" }, []);
        Assert.Equal("ITCAMERA-19-90074", r.Scrutin.Id);
        Assert.Equal(4, r.Votes.Count);                  // le « non ha votato » n'est pas stocké
        Assert.Equal(2, r.Scrutin.NbPour);
        Assert.Equal("adopté", r.Scrutin.Sort);
        Assert.True(r.Scrutin.SurEnsemble);
        Assert.Equal(0, r.Scrutin.NbVotesManquants);
        Assert.Contains("Sitting 9, vote 74.", r.Scrutin.Note);
    }

    [Fact]
    public void Scrutin_secret_totaux_seuls_marque_incomplet()
    {
        var votes = new[] { V("1", "Ha votato"), V("2", "Ha votato") };
        var r = ItalyCameraSource.VersScrutin(Vote(secret: true), votes, new HashSet<string> { "1", "2" }, []);
        Assert.Empty(r.Votes);
        Assert.Equal(4, r.Scrutin.NbVotesManquants);     // tous les votes attendus manquent : exclu des taux
        Assert.Contains("Secret ballot", r.Scrutin.Note);
    }

    [Fact]
    public void Question_de_confiance_dite_dans_la_note_et_deputé_inconnu_compte()
    {
        var inconnus = new HashSet<string>();
        var r = ItalyCameraSource.VersScrutin(Vote(fiducia: true, fav: 1, con: 0, ast: 0), [V("1", "Favorevole"), V("9", "Favorevole")], new HashSet<string> { "1" }, inconnus);
        Assert.Contains("Question of confidence", r.Scrutin.Note);
        Assert.Contains("no entry in the members directory", r.Scrutin.Note);
        Assert.Contains("9", inconnus);
        Assert.Single(r.Votes);
    }

    [Fact]
    public void Groupe_identifiant_stable_et_sans_groupe()
    {
        Assert.Equal("ITCAMERA-19-gr4133", ItalyCameraSource.Groupe(V("1", "Favorevole"), 19)!.Id);
        var sans = ItalyCameraSource.Groupe(V("1", "Favorevole", "http://dati.camera.it/ocd/gruppoParlamentare.rdf/gr9", "------"), 19)!;
        Assert.Equal("No group", sans.Sigle);
        Assert.Null(ItalyCameraSource.Groupe(new VotoIt("u", "1", "Favorevole", null, null), 19));
    }

    [Fact]
    public void Ligne_de_resultat_convertie_et_adresse_officielle_en_https()
    {
        var l = new Dictionary<string, string>
        {
            ["v"] = "http://dati.camera.it/ocd/votazione.rdf/vs19_020_022", ["d"] = "20221206", ["den"] = "Votazione finale DDL n. 0547", ["des"] = "Conversione &amp;amp; altro",
            ["fav"] = "164", ["con"] = "131", ["ast"] = "0", ["pre"] = "295", ["vot"] = "295", ["mag"] = "148", ["app"] = "1", ["fin"] = "1", ["fid"] = "0", ["seg"] = "0",
            ["rel"] = ItalyCameraSource.EnHttps("http://documenti.camera.it/x")!,
        };
        var v = ItalyCameraSource.Convertir(l, 19)!;
        Assert.Equal(20, v.Seduta); Assert.Equal(22, v.NumeroDansSeduta); Assert.Equal(new DateOnly(2022, 12, 6), v.Date);
        Assert.Equal("Conversione & altro", v.Description);
        Assert.True(v.Approvata); Assert.True(v.Finale); Assert.False(v.Segreta);
        Assert.StartsWith("https://", v.Fiche);
        Assert.Equal(200022, ItalyCameraSource.Numero(v));
    }
}
