using ClubPay.Agent.Client.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClubPay.Agent.Client.Tests.Services;

public sealed class AgentServiceTests
{
    [Fact]
    public void Constructor_ExpandsMachineNameTemplatesForSharedDisklessImage()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agent:PcId"] = "PC {MACHINE_NAME}",
                ["Controller:ExternalPcId"] = "{MACHINE_NAME_LOWER}",
            })
            .Build();

        var sut = new AgentService(config, NullLogger<AgentService>.Instance);

        Assert.Equal($"PC {Environment.MachineName}", sut.PcId);
        Assert.Equal(Environment.MachineName.ToLowerInvariant(), sut.ExternalPcId);
    }

    [Fact]
    public void Constructor_LeavesExistingStaticIdentityUnchanged()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agent:PcId"] = "Pilot PC #01",
                ["Controller:ExternalPcId"] = "pilot-real-network-pc-001",
            })
            .Build();

        var sut = new AgentService(config, NullLogger<AgentService>.Instance);

        Assert.Equal("Pilot PC #01", sut.PcId);
        Assert.Equal("pilot-real-network-pc-001", sut.ExternalPcId);
    }
}
