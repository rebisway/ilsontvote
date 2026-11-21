using IlsOntVote.Web.Models;
using IlsOntVote.Web.Services;
using Microsoft.AspNetCore.Http;

namespace IlsOntVote.Tests;

/// <summary>L'API de traduction à clés stables : repli sur le texte du code (sans table de traduction), formats par langue, aides à clé fixe.</summary>
public class LangueTests
{
    private static Langue Pour(string langue, string? pays = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Items["lang"] = langue;
        if (pays is not null) ctx.Items[PaysSite.CleItems] = pays;
        // Table de traduction en mémoire (aucune base) : tous les textes vivent en base, le code ne porte que des clés.
        var tr = new Traductions(null!, Microsoft.Extensions.Logging.Abstractions.NullLogger<Traductions>.Instance);
        tr.Remplacer(
        [
            new() { Langue = "fr", Cle = "lois", Texte = "Lois" }, new() { Langue = "en", Cle = "lois", Texte = "Laws" }, new() { Langue = "it", Cle = "lois", Texte = "Leggi" },
            new() { Langue = "fr", Cle = "n-votes", Texte = "{0:N0} votes" }, new() { Langue = "en", Cle = "n-votes", Texte = "{0:N0} votes" },
            new() { Langue = "fr", Cle = "lien", Texte = "<a href=\"{0}\">m</a>" }, new() { Langue = "en", Cle = "lien", Texte = "<a href=\"{0}\">m</a>" },
            new() { Langue = "fr", Cle = "seulfr", Texte = "Seulement français" }, new() { Langue = "en", Cle = "seulfr", Texte = "English only" },
            new() { Langue = "fr", Cle = "chambre.SENAT.nom", Texte = "Sénat" }, new() { Langue = "en", Cle = "chambre.SENAT.nom", Texte = "Senate" },
            new() { Langue = "fr", Cle = "position.pour", Texte = "Pour" }, new() { Langue = "en", Cle = "position.pour", Texte = "For" },
        ]);
        return new Langue(new HttpContextAccessor { HttpContext = ctx }, tr);
    }

    [Theory]
    [InlineData("fr", "Lois")]
    [InlineData("en", "Laws")]
    [InlineData("it", "Leggi")]
    public void T_lit_le_texte_en_base_selon_la_langue(string langue, string attendu) => Assert.Equal(attendu, Pour(langue).T("lois"));

    [Fact]
    public void Langue_sans_ligne_retombe_sur_l_anglais_et_la_suisse_sur_le_francais()
    {
        Assert.Equal("English only", Pour("de").T("seulfr"));
        Assert.Equal("Seulement français", Pour("de", "CH").T("seulfr"));
    }

    [Fact]
    public void Cle_absente_de_la_base_affiche_la_cle()
    {
        // Jamais de texte vide silencieux : la clé s'affiche, donc l'oubli se voit.
        Assert.Equal("inconnue-xyz", Pour("fr").T("inconnue-xyz"));
    }

    [Fact]
    public void Tf_met_en_forme_les_valeurs_dans_la_culture_de_la_langue()
    {
        // L'espace des milliers français est insécable (U+00A0 ou U+202F selon la version d'ICU) : on compare sans cette nuance.
        Assert.Equal("4 781 votes", Pour("fr").Tf("n-votes", 4781).Replace(' ', ' ').Replace(' ', ' '));
        Assert.Equal("4,781 votes", Pour("en").Tf("n-votes", 4781));
    }

    [Fact]
    public void THf_insere_les_valeurs_sans_les_encoder()
    {
        Assert.Equal("<a href=\"/it/methodologie\">m</a>", Pour("en").THf("lien", "/it/methodologie").Value);
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
