using IlsOntVote.Web.Services;
using Microsoft.AspNetCore.Http;

namespace IlsOntVote.Tests;

/// <summary>Préfixes de pays (/uk/…) : correspondance par segment entier, pages concernées, détection des robots.</summary>
public class PaysSiteTests
{
    private static readonly IReadOnlyList<PaysActif> Uk = [new("GB", "uk", "Royaume-Uni", "United Kingdom")];

    [Theory]
    [InlineData("/uk", "/")]
    [InlineData("/uk/", "/")]
    [InlineData("/uk/divisions", "/divisions")]
    [InlineData("/UK/Divisions", "/Divisions")]      // casse non significative pour le préfixe
    public void Prefixe_reconnu_et_retire(string chemin, string reste)
    {
        var (pays, r) = PaysSite.Correspond(Uk, new PathString(chemin));
        Assert.Equal("GB", pays?.Code);
        Assert.Equal(reste, r.Value is "/" or null ? "/" : r.Value.TrimEnd('/') is { Length: > 0 } t ? t : "/");
    }

    [Theory]
    [InlineData("/ukraine")]                          // segment entier : « uk » n'est pas un préfixe de « ukraine »
    [InlineData("/lois")]
    [InlineData("/en/uk")]                            // la langue /en reste une autre dimension
    [InlineData("/")]
    public void Prefixe_absent_ou_partiel_ne_correspond_pas(string chemin)
    {
        var (pays, reste) = PaysSite.Correspond(Uk, new PathString(chemin));
        Assert.Null(pays);
        Assert.Equal(chemin, reste.Value);
    }

    [Fact]
    public void Aucun_pays_actif_rien_ne_correspond() =>
        Assert.Null(PaysSite.Correspond([], new PathString("/uk/divisions")).Pays);

    [Theory]
    [InlineData("/Admin/Imports", false)]
    [InlineData("/Identity/Account/Login", false)]
    [InlineData("/apercu/scrutin/x.png", false)]
    [InlineData("/robots.txt", false)]
    [InlineData("/css/site.css", false)]
    [InlineData("/lois", true)]
    [InlineData("/", true)]
    public void Pages_du_site(string chemin, bool attendu) => Assert.Equal(attendu, PaysSite.EstPageDuSite(new PathString(chemin)));

    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/130 Safari/537.36", false)]
    [InlineData("Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)", true)]
    [InlineData("curl/8.5.0", true)]
    [InlineData("", true)]
    [InlineData(null, true)]
    public void Robots_jamais_devies(string? ua, bool robot) => Assert.Equal(robot, PaysSite.EstRobot(ua));
}
