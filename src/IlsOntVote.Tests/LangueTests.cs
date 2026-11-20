using IlsOntVote.Web.Models;
using IlsOntVote.Web.Services;
using Microsoft.AspNetCore.Http;

namespace IlsOntVote.Tests;

/// <summary>L'API de traduction à clés stables : repli sur le texte du code (sans table de traduction), formats par langue, aides à clé fixe.</summary>
public class LangueTests
{
    private static Langue Pour(string langue)
    {
        var ctx = new DefaultHttpContext();
        ctx.Items["lang"] = langue;
        return new Langue(new HttpContextAccessor { HttpContext = ctx });
    }

    [Theory]
    [InlineData("fr", "Lois")]
    [InlineData("en", "Laws")]
    [InlineData("it", "Laws")]   // autre langue sans table de traduction : l'anglais sert de repli
    public void T_donne_le_texte_du_code_selon_la_langue(string langue, string attendu) => Assert.Equal(attendu, Pour(langue).T("lois", "Lois", "Laws"));

    [Fact]
    public void La_cle_ne_change_pas_le_texte_affiche()
    {
        // Retoucher la phrase ne dépend pas de la clé : même clé, autre phrase = autre affichage, sans effet de bord.
        Assert.Equal("Bonjour", Pour("fr").T("salut", "Bonjour", "Hello"));
        Assert.Equal("Salut", Pour("fr").T("salut", "Salut", "Hello"));
    }

    [Fact]
    public void Tf_met_en_forme_les_valeurs_dans_la_culture_de_la_langue()
    {
        // L'espace des milliers français est insécable (U+00A0 ou U+202F selon la version d'ICU) : on compare sans cette nuance.
        Assert.Equal("4 781 votes", Pour("fr").Tf("n-votes", "{0:N0} votes", "{0:N0} votes", 4781).Replace(' ', ' ').Replace(' ', ' '));
        Assert.Equal("4,781 votes", Pour("en").Tf("n-votes", "{0:N0} votes", "{0:N0} votes", 4781));
    }

    [Fact]
    public void THf_insere_les_valeurs_sans_les_encoder()
    {
        Assert.Equal("<a href=\"/it/methodologie\">m</a>", Pour("en").THf("lien", "<a href=\"{0}\">m</a>", "<a href=\"{0}\">m</a>", "/it/methodologie").Value);
    }

    [Fact]
    public void Aides_a_cle_fixe_donnent_les_libelles_de_chambres_et_de_positions()
    {
        Assert.Equal("Sénat", Pour("fr").Chambre(Chambres.Senat));
        Assert.Equal("Senate", Pour("en").Chambre(Chambres.Senat));
        Assert.Equal("Pour", Pour("fr").Position(Pos.Pour));
        Assert.Equal("For", Pour("en").Position(Pos.Pour));
    }
}
