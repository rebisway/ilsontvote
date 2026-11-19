using IlsOntVote.Importer;

namespace IlsOntVote.Tests;

/// <summary>Congrès des États-Unis (Voteview) : lecture des CSV, positions, résultat, vote sur l'ensemble, majorité, conversion en scrutin.</summary>
public class UsTests
{
    [Fact]
    public void Csv_gere_guillemets_virgules_et_guillemets_doubles()
    {
        var l = UsSource.LireCsv("a,b,c\n1,\"x, y\",\"il dit \"\"oui\"\"\"\n2,,fin\n").ToList();
        Assert.Equal(3, l.Count);
        Assert.Equal(["1", "x, y", "il dit \"oui\""], l[1]);
        Assert.Equal(["2", "", "fin"], l[2]);
    }

    [Theory]
    [InlineData("STRONG, Dale", "Dale", "Strong")]
    [InlineData("ROGERS, Harold Dallas (Hal)", "Hal", "Rogers")]
    [InlineData("McCARTHY, Kevin Owen", "Kevin", "McCARTHY")]   // casse mixte : conservée (l'import ne devine pas)
    [InlineData("O'ROURKE, Beto", "Beto", "O'Rourke")]
    [InlineData("SMITH-JONES, Ann", "Ann", "Smith-Jones")]
    public void Nom_decoupe_en_prenom_et_nom(string bioname, string prenom, string nom)
    {
        var (p, n) = UsSource.DecouperNom(bioname);
        Assert.Equal(prenom, p);
        Assert.Equal(nom, n);
    }

    [Theory]
    [InlineData(1, Positions.Pour)]
    [InlineData(6, Positions.Contre)]
    [InlineData(7, Positions.Abstention)]
    [InlineData(9, null)]      // not voting : absent, non stocké
    [InlineData(3, null)]      // annoncé : pas un vote
    [InlineData(5, null)]      // apparié : pas un vote
    public void Position_d_apres_le_code(int code, string? attendu) => Assert.Equal(attendu, UsSource.Position(code));

    [Theory]
    [InlineData("Passed", "adopté")]
    [InlineData("Failed", "rejeté")]
    [InlineData("Nomination Confirmed", "adopté")]
    [InlineData("Cloture Motion Agreed to", "adopté")]
    [InlineData("Amendment Rejected", "rejeté")]
    [InlineData("Johnson (LA)", null)]   // élection du Président de la Chambre : aucun résultat deviné
    public void Resultat_d_apres_le_texte(string texte, string? attendu) => Assert.Equal(attendu, UsSource.Sort(texte));

    [Theory]
    [InlineData("On Passage", true)]
    [InlineData("On Motion to Suspend the Rules and Pass, as Amended", true)]
    [InlineData("On the Nomination", true)]
    [InlineData("On Agreeing to the Resolution, as Amended", true)]
    [InlineData("On the Cloture Motion", false)]
    [InlineData("On the Amendment", false)]
    [InlineData("On Motion to Recommit", false)]
    [InlineData("On Ordering the Previous Question", false)]
    public void Vote_sur_l_ensemble_calcule_d_apres_la_question(string question, bool attendu) => Assert.Equal(attendu, UsSource.EstVoteSurEnsemble(question));

    [Theory]
    [InlineData("1/2", 430, 216)]
    [InlineData("3/5", 100, 60)]
    [InlineData("2/3", 410, 274)]
    public void Majorite_requise_calculee(string regle, int exprimes, int attendu) => Assert.Equal(attendu, UsSource.Majorite(regle, exprimes).Requis);

    [Fact]
    public void Appel_converti_decompte_officiel_garde_et_ecart_dit_dans_la_note()
    {
        var a = new AppelNominalUs(Chambres.UsHouse, 119, 49, new DateOnly(2025, 2, 25), 1, 50, "On Passage", "Passed", "HR1", "Test Act", "", 217, 215, "1/2");
        var codes = new List<CodeVoteUs>();
        for (var i = 0; i < 218; i++) codes.Add(new(Chambres.UsHouse, 49, "A" + i, 1));   // 218 yea listés pour 217 officiels
        for (var i = 0; i < 215; i++) codes.Add(new(Chambres.UsHouse, 49, "B" + i, 6));
        codes.Add(new(Chambres.UsHouse, 49, "C1", 9));
        codes.Add(new(Chambres.UsHouse, 49, "C2", 3));   // position annoncée
        var r = UsSource.VersScrutin(a, codes, new Dictionary<string, string>());
        Assert.Equal(217, r.Scrutin.NbPour);
        Assert.Equal(215, r.Scrutin.NbContre);
        Assert.Equal(433, r.Votes.Count);                                    // le non-votant et l'annonce ne sont pas stockés
        Assert.Contains("Official count: 217 yea", r.Scrutin.Note);
        Assert.Contains("announced or paired", r.Scrutin.Note);
        Assert.True(r.Scrutin.SurEnsemble);
        Assert.Equal("adopté", r.Scrutin.Sort);
        Assert.Equal("https://clerk.house.gov/Votes/2025050", r.Scrutin.DossierRef);
        Assert.Equal("HR1: Test Act", r.Scrutin.Titre);
    }

    [Fact]
    public void Url_officielle_du_senat()
    {
        var a = new AppelNominalUs(Chambres.UsSenate, 119, 7, new DateOnly(2025, 1, 20), 1, 7, "On the Nomination", "Nomination Confirmed", "", "X", "", 51, 49, "1/2");
        Assert.Equal("https://www.senate.gov/legislative/LIS/roll_call_votes/vote1191/vote_119_1_00007.htm", UsSource.UrlOfficielle(a));
    }

    // ------------------------------------------------------------ greffe de la Chambre (XML officiel)

    private const string XmlChambre =
        "<rollcall-vote><vote-metadata><congress>119</congress><session>1st</session><rollcall-num>6</rollcall-num><legis-num>H R 29</legis-num>" +
        "<vote-question>On Passage</vote-question><vote-type>YEA-AND-NAY</vote-type><vote-result>Passed</vote-result><action-date>7-Jan-2025</action-date>" +
        "<vote-desc>Laken Riley Act</vote-desc><vote-totals><totals-by-vote><total-stub>Totals</total-stub><yea-total>2</yea-total><nay-total>1</nay-total>" +
        "<present-total>0</present-total><not-voting-total>1</not-voting-total></totals-by-vote></vote-totals></vote-metadata><vote-data>" +
        "<recorded-vote><legislator name-id=\"A1\" party=\"D\" state=\"NC\">Adams</legislator><vote>Nay</vote></recorded-vote>" +
        "<recorded-vote><legislator name-id=\"A2\" party=\"R\" state=\"AL\">Aderholt</legislator><vote>Yea</vote></recorded-vote>" +
        "<recorded-vote><legislator name-id=\"A3\" party=\"R\" state=\"CA\">Aguilar</legislator><vote>Yea</vote></recorded-vote>" +
        "<recorded-vote><legislator name-id=\"A4\" party=\"R\" state=\"TX\">Allen</legislator><vote>Not Voting</vote></recorded-vote></vote-data></rollcall-vote>";

    [Fact]
    public void Greffe_analyse_les_metadonnees_les_totaux_et_les_votes()
    {
        var a = HouseClerkSource.Analyser(XmlChambre)!;
        Assert.Equal(119, a.Congres); Assert.Equal(1, a.Session); Assert.Equal(6, a.Numero);
        Assert.Equal(new DateOnly(2025, 1, 7), a.Date);
        Assert.Equal("H.R. 29", a.Projet);
        Assert.Equal(2, a.Pour); Assert.Equal(1, a.Contre); Assert.Equal(1, a.NonVotants);
        Assert.Equal(4, a.Votes.Count);
    }

    [Fact]
    public void Greffe_un_vote_inexistant_n_est_pas_un_vote() =>
        Assert.Null(HouseClerkSource.Analyser("<xml>Error sanitizing file \"roll700.xml\". Please try again.</xml>"));   // le serveur répond 200 avec ce texte

    [Fact]
    public void Greffe_conversion_non_votant_non_stocke_et_totaux_officiels()
    {
        var a = HouseClerkSource.Analyser(XmlChambre)!;
        var icpsr = new Dictionary<string, string> { ["A1"] = "10", ["A2"] = "20", ["A3"] = "30", ["A4"] = "40" };
        var r = HouseClerkSource.VersScrutin(a, 1006, icpsr, new Dictionary<string, string>(), []);
        Assert.Equal("USHOUSE-119-1006", r.Scrutin.Id);
        Assert.Equal(3, r.Votes.Count);                     // le non-votant n'est pas stocké
        Assert.Null(r.Scrutin.Note);                         // totaux officiels = liste : aucune note
        Assert.Equal("https://clerk.house.gov/Votes/2025006", r.Scrutin.DossierRef);
        Assert.True(r.Scrutin.SurEnsemble);
    }

    [Fact]
    public void Greffe_representant_inconnu_dit_dans_la_note_et_compte()
    {
        var a = HouseClerkSource.Analyser(XmlChambre)!;
        var inconnus = new HashSet<string>();
        var r = HouseClerkSource.VersScrutin(a, 6, new Dictionary<string, string> { ["A1"] = "10", ["A2"] = "20" }, new Dictionary<string, string>(), inconnus);
        Assert.Contains("no entry in the members directory", r.Scrutin.Note);
        Assert.Contains("A3", inconnus);
    }

    [Theory]
    [InlineData("QUORUM", "Call of the House", false)]
    [InlineData("YEA-AND-NAY", "Election of the Speaker", false)]
    [InlineData("YEA-AND-NAY", "On Passage", true)]
    [InlineData("2/3 YEA-AND-NAY", "On Motion to Suspend the Rules and Pass", true)]
    public void Greffe_votes_importes(string type, string question, bool attendu) =>
        Assert.Equal(attendu, HouseClerkSource.EstUnVotePourContre(new AppelChambre(119, 1, 1, new DateOnly(2025, 1, 1), "", question, type, "", "", 0, 0, 0, 0, [])));

    [Theory]
    [InlineData("H R 29", "H.R. 29")]
    [InlineData("H RES 5", "H.Res. 5")]
    [InlineData("H J RES 1", "H.J.Res. 1")]
    [InlineData("S 1", "S. 1")]
    [InlineData("QUORUM", "QUORUM")]
    [InlineData("", "")]
    public void Projet_normalise(string brut, string attendu) => Assert.Equal(attendu, HouseClerkSource.NormaliserProjet(brut));
}
