using IlsOntVote.Importer;

namespace IlsOntVote.Tests;

/// <summary>Sénat du Canada (pages HTML du site officiel) : lecture de la liste et du tableau d'un vote, positions, groupes, vote final calculé, numéro unique par session, décomptes officiels.</summary>
public class CanadaSenatTests
{
    // Reproduit la structure réelle de sencanada.ca (juin 2026) : date + rang dans data-order, titre et décomptes dans la 2e cellule, projet de loi, résultat.
    private const string Liste = """
        <table><thead><tr><th>Date</th></tr></thead><tbody>
        <tr>
          <td class="vote-centered" style="white-space: nowrap;" data-order="2026-06-18 13:30:00 37"><a href="/en/content/sen/chamber/451/journals/086jr_2026-06-18-e">2026-06-18</a></td>
          <td data-order="x"><a class="vote-web-title-link" title="View" href="/en/in-the-chamber/votes/details/702799/45-1">
              Spring Economic Update 2026 Implementation Act &#x2013; C-30 &#x2013; Third Reading </a><br />
              Yeas: 58 <text>|</text> Nays: 9 <text>|</text> Abstentions: 1 <text>|</text> Total: 68 </td>
          <!--For Bills, we will want to sort by the bill number only, <td>piège</td> -->
          <td class="vote-centered" data-order="130037"><a href="http://www.parl.ca/LEGISInfo/BillDetails.aspx?billId=1">C-30</a></td>
          <td class="vote-centered"> Adopted </td>
        </tr>
        <tr>
          <td class="vote-centered" data-order="2026-06-18 15:00:00 38"><a href="#">2026-06-18</a></td>
          <td data-order="y"><a class="vote-web-title-link" href="/en/in-the-chamber/votes/details/702800/45-1">Adjournment of the Senate</a><br />
              Yeas: 20 <text>|</text> Nays: 30 <text>|</text> Abstentions: 0 <text>|</text> Total: 50 </td>
          <td class="vote-centered" data-order=""></td>
          <td class="vote-centered"> Defeated </td>
        </tr>
        <tr><td>ligne sans lien de détail</td><td>x</td><td>y</td><td>z</td></tr>
        </tbody></table>
        """;

    private static string Detail(params (int Id, string Nom, string Groupe, string Province, int Marque)[] lignes)
    {
        // Marque : 0 = aucune, 1 = Yea, 2 = Nay, 3 = Abstention.
        string Case(int m, int k) => m == k ? "<td class=\"sc-vote-details-table-centered\"><i class=\"fa-solid fa-times\" aria-hidden=\"true\"></i></td>" : "<td class=\"sc-vote-details-table-centered\"> </td>";
        var corps = string.Concat(lignes.Select(l =>
            $"<tr><td data-order=\"{l.Nom}\"><a href=\"/en/in-the-chamber/votes/senator/{l.Id}/45-1\"> {l.Nom} </a></td><td class=\"c\"> {l.Groupe} </td><td class=\"c\"> {l.Province} </td>{Case(l.Marque, 1)}{Case(l.Marque, 2)}{Case(l.Marque, 3)}</tr>"));
        return "<main><table class=\"table sc-table\" id=\"sc-vote-details-table\"><thead><tr><th>Senator</th><th>Affiliation</th><th>Province</th><th>Yea</th><th>Nay</th><th>Abstention</th></tr></thead><tbody>" + corps + "</tbody></table></main>";
    }

    private static VoteSenatCa Vote(string titre = "Act – C-30 – Third Reading", string? resultat = "Adopted", int oui = 2, int non = 1, int abst = 1, int session = 1, int ordre = 37) =>
        new(45, session, 702799, ordre, new DateOnly(2026, 6, 18), titre, "Titre fr", oui, non, abst, oui + non + abst, resultat, "C-30");

    [Fact]
    public void Liste_lit_date_rang_titre_decomptes_projet_et_resultat_et_ignore_le_reste()
    {
        var l = CanadaSenatSource.ParserListe(Liste, 45, 1);
        Assert.Equal(2, l.Count);                                   // la ligne sans lien de détail est ignorée, le commentaire HTML aussi
        var v = l[0];
        Assert.Equal((702799, 37, new DateOnly(2026, 6, 18)), (v.Id, v.Ordre, v.Date));
        Assert.Equal("Spring Economic Update 2026 Implementation Act – C-30 – Third Reading", v.TitreEn);   // entité &#x2013; décodée, espaces réduits
        Assert.Equal((58, 9, 1, 68), (v.Oui, v.Non, v.Abstentions, v.Total));
        Assert.Equal("Adopted", v.Resultat);
        Assert.Equal("C-30", v.Projet);
        Assert.Null(l[1].Projet);                                   // pas de projet de loi : rien d'inventé
        Assert.Equal("Defeated", l[1].Resultat);
    }

    [Fact]
    public void Liste_d_une_autre_session_n_est_pas_lue()
    {
        Assert.Empty(CanadaSenatSource.ParserListe(Liste, 44, 1));   // le lien indique 45-1 : jamais mélanger deux sessions
        Assert.Empty(CanadaSenatSource.ParserListe("<html>erreur</html>", 45, 1));
    }

    [Fact]
    public void Detail_lit_nom_groupe_province_et_la_case_cochee()
    {
        var html = Detail((2753, "Ringuette, Pierrette", "ISG", "New Brunswick", 2), (2755, "Downe, Percy E.", "CSG", "Prince Edward Island", 0), (206043, "Gagné, Raymonde", "Non-affiliated", "Manitoba", 3), (9, "Un, Autre", "C", "Ontario", 1));
        var b = CanadaSenatSource.ParserDetail(html);
        Assert.Equal(4, b.Count);                                    // l'en-tête (<th>) n'est pas une ligne
        Assert.Equal(("Pierrette", "Ringuette", "ISG", "New Brunswick", "Nay"), (b[0].Prenom, b[0].Nom, b[0].Groupe, b[0].Province, b[0].Valeur));
        Assert.Equal(2753, b[0].SenateurId);
        Assert.Null(b[1].Valeur);                                    // aucune case : n'a pas voté (absent)
        Assert.Equal("Percy E.", b[1].Prenom);
        Assert.Equal("Abstention", b[2].Valeur);
        Assert.Equal("Yea", b[3].Valeur);
        Assert.Empty(CanadaSenatSource.ParserDetail("<p>pas de tableau</p>"));
    }

    [Fact]
    public void Detail_avec_deux_cases_cochees_est_lu_comme_sans_vote()
    {
        var html = Detail((1, "A, B", "C", "Ontario", 1)).Replace("<td class=\"sc-vote-details-table-centered\"> </td>", "<td><i></i></td>");
        Assert.Null(Assert.Single(CanadaSenatSource.ParserDetail(html)).Valeur);   // incohérent : jamais d'invention
    }

    [Theory]
    [InlineData("Yea", Positions.Pour)]
    [InlineData("Nay", Positions.Contre)]
    [InlineData("Abstention", Positions.Abstention)]
    [InlineData(null, null)]
    [InlineData("Autre", null)]
    public void Position_d_apres_la_case(string? valeur, string? attendu) => Assert.Equal(attendu, CanadaSenatSource.Position(valeur));

    [Theory]
    [InlineData("Budget Implementation Act, 2021, No. 1 – C-30 – Third Reading", true)]
    [InlineData("Third Reading", true)]
    [InlineData("An Act – C-3 – Third Reading – Amendment (Sen. White)", false)]
    [InlineData("An Act – C-29 – Third Reading – Adjournment of debate", false)]
    [InlineData("Appropriation Act No. 5 – C-17 – Second Reading", false)]
    [InlineData("Time Allocation of Debate at Third Reading of Bill C-21", false)]
    public void Vote_final_calcule_d_apres_le_dernier_segment_du_titre(string titre, bool attendu) => Assert.Equal(attendu, CanadaSenatSource.EstVoteFinal(titre));

    [Fact]
    public void Numero_unique_par_session_car_le_rang_repart_de_1()
    {
        Assert.NotEqual(CanadaSenatSource.Numero(Vote(session: 1, ordre: 5)), CanadaSenatSource.Numero(Vote(session: 2, ordre: 5)));
        Assert.Equal(10_037, CanadaSenatSource.Numero(Vote()));
        Assert.Equal("CASENATE-45-10037", CanadaSenatSource.IdScrutin(45, 10_037));
    }

    [Fact]
    public void Vote_converti_decomptes_officiels_positions_groupes_et_absents_non_stockes()
    {
        var b = CanadaSenatSource.ParserDetail(Detail((1, "A, A", "ISG", "Ontario", 1), (2, "B, B", "C", "Ontario", 1), (3, "C, C", "C", "Ontario", 2), (4, "D, D", "CSG", "Ontario", 3), (5, "E, E", "CSG", "Ontario", 0)));
        var r = CanadaSenatSource.VersScrutin(Vote(), b);
        Assert.Equal(4, r.Votes.Count);                              // le sénateur sans case n'est pas un vote
        Assert.DoesNotContain(r.Votes, v => v.ParlementaireId == "CASENATE-5");
        Assert.Equal("CASENATE-45-isg", r.Votes.First(v => v.ParlementaireId == "CASENATE-1").GroupeId);
        Assert.Equal(Positions.Abstention, r.Votes.First(v => v.ParlementaireId == "CASENATE-4").Position);
        Assert.Equal((2, 1, 1), (r.Scrutin.NbPour, r.Scrutin.NbContre, r.Scrutin.NbAbstentions));
        Assert.Equal(4, r.Scrutin.NbVotants);
        Assert.Equal(0, r.Scrutin.NbVotesManquants);
        Assert.DoesNotContain("Official count", r.Scrutin.Note);     // décomptes concordants : pas d'alerte
        Assert.Equal("adopté", r.Scrutin.Sort);
        Assert.True(r.Scrutin.SurEnsemble);
        Assert.Equal("Titre fr", r.Scrutin.Titre); Assert.Equal("Act – C-30 – Third Reading", r.Scrutin.TitreEn);
        Assert.Equal("https://sencanada.ca/en/in-the-chamber/votes/details/702799/45-1", r.Scrutin.DossierRef);
        Assert.Equal("CASENATE", r.Scrutin.Chambre);
    }

    [Fact]
    public void Ecart_de_decompte_est_dit_et_rend_le_vote_incomplet_resultat_inconnu_sans_sort()
    {
        var b = CanadaSenatSource.ParserDetail(Detail((1, "A, A", "C", "Ontario", 1)));
        var r = CanadaSenatSource.VersScrutin(Vote(resultat: "Tied"), b);   // 4 votes officiels, 1 nom
        Assert.Null(r.Scrutin.Sort);                                        // jamais deviné
        Assert.Equal(3, r.Scrutin.NbVotesManquants);
        Assert.Contains("Official count: 2 Yea, 1 Nay, 1 Abstention; senator list: 1 Yea, 0 Nay, 0 Abstention", r.Scrutin.Note);
        Assert.Equal("rejeté", CanadaSenatSource.VersScrutin(Vote(resultat: "Defeated"), b).Scrutin.Sort);
    }

    [Fact]
    public void Groupe_connu_a_son_sigle_son_nom_et_son_ordre_inconnu_garde_son_sigle_et_un_gris()
    {
        var isg = CanadaSenatSource.Groupe(45, "ISG");
        Assert.Equal(("ISG", "Independent Senators Group"), (isg.Sigle, isg.Libelle));
        var na = CanadaSenatSource.Groupe(45, "Non-affiliated");
        Assert.Equal("NA", na.Sigle);
        Assert.Equal("CASENATE-45-non-affiliated", na.Id);
        var autre = CanadaSenatSource.Groupe(45, "XYZ");
        Assert.Equal(("XYZ", "XYZ", "#8a8f98"), (autre.Sigle, autre.Libelle, autre.Couleur));   // jamais de nom ni de couleur inventés
    }
}
