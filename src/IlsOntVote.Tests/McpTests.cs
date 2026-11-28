using IlsOntVote.Web.Mcp;

namespace IlsOntVote.Tests;

/// <summary>Limite de débit du serveur MCP : une dérogation ne vaut que jusqu'à son échéance.</summary>
public class McpTests
{
    private static readonly DateTime Maintenant = new(2026, 11, 28, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Sans_derogation_limite_normale() => Assert.Equal(LimiteMcp.Normale, LimiteMcp.Appliquer(new ReglageMcp(), Maintenant));

    [Theory]
    [InlineData(300)]
    [InlineData(null)]
    public void Derogation_en_cours_appliquee(int? limite) =>
        Assert.Equal(limite, LimiteMcp.Appliquer(new ReglageMcp { Limite = limite, FinLe = Maintenant.AddDays(1) }, Maintenant));

    [Fact]
    public void Derogation_expiree_limite_normale() =>
        Assert.Equal(LimiteMcp.Normale, LimiteMcp.Appliquer(new ReglageMcp { Limite = null, FinLe = Maintenant.AddSeconds(-1) }, Maintenant));
}
