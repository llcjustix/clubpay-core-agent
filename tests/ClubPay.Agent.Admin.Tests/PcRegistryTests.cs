using Microsoft.Extensions.Configuration;
using ClubPay.Agent.Admin.Services.Controller;
using ClubPay.Agent.Core.Models;

namespace ClubPay.Agent.Admin.Tests;

public class PcRegistryTests
{
    private static IPcRegistry BuildRegistry(params (string ExternalPcId, string PcId, string Zone, string Token)[] pcs)
    {
        var data = new Dictionary<string, string?>();
        for (int i = 0; i < pcs.Length; i++)
        {
            data[$"Controller:Pcs:{i}:ExternalPcId"] = pcs[i].ExternalPcId;
            data[$"Controller:Pcs:{i}:PcId"] = pcs[i].PcId;
            data[$"Controller:Pcs:{i}:Zone"] = pcs[i].Zone;
            data[$"Controller:Pcs:{i}:AgentToken"] = pcs[i].Token;
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection(data).Build();
        return new PcRegistry(config);
    }

    [Fact]
    public void All_ReturnsEntriesFromConfig_WithParsedZone()
    {
        var registry = BuildRegistry(("club12-pc01", "PC-01", "Pro", "secret-1"));

        var entry = Assert.Single(registry.All);
        Assert.Equal("club12-pc01", entry.ExternalPcId);
        Assert.Equal("PC-01", entry.PcId);
        Assert.Equal(ZoneType.Pro, entry.Zone);
        Assert.Equal("secret-1", entry.AgentToken);
    }

    [Fact]
    public void ValidateToken_CorrectToken_ReturnsTrue()
    {
        var registry = BuildRegistry(("club12-pc01", "PC-01", "Standard", "secret-1"));

        Assert.True(registry.ValidateToken("club12-pc01", "secret-1"));
    }

    [Theory]
    [InlineData("wrong-secret")]
    [InlineData(null)]
    [InlineData("")]
    public void ValidateToken_WrongOrMissingToken_ReturnsFalse(string? token)
    {
        var registry = BuildRegistry(("club12-pc01", "PC-01", "Standard", "secret-1"));

        Assert.False(registry.ValidateToken("club12-pc01", token));
    }

    [Fact]
    public void ValidateToken_UnknownPc_ReturnsFalse()
    {
        var registry = BuildRegistry(("club12-pc01", "PC-01", "Standard", "secret-1"));

        Assert.False(registry.ValidateToken("club12-pc99", "secret-1"));
    }
}
