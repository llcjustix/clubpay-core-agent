using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ClubPay.Agent.Client.Services;
using ClubPay.Agent.Core.Contracts;
using ClubPay.Agent.Core.Models;

namespace ClubPay.Agent.Client.Tests.Services;

public class AgentStateRepositoryTests : IDisposable
{
    private readonly string _dataDir;

    public AgentStateRepositoryTests()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), "clubpay-agent-tests-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDir))
            Directory.Delete(_dataDir, recursive: true);
    }

    private AgentStateRepository BuildSut()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:DataDirectory"] = _dataDir })
            .Build();
        return new AgentStateRepository(config, NullLogger<AgentStateRepository>.Instance);
    }

    private static Session MakeSession() =>
        new(Guid.NewGuid(), "PC-12", Tariff.DefaultStandard[1], DateTime.UtcNow, 3600,
            GrantId: "grant_1001", EndsAtUtc: DateTime.UtcNow.AddSeconds(3600), Zone: "Standard");

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsSessionFields()
    {
        var session = MakeSession();
        var sut = BuildSut();
        await sut.SaveAsync(session);

        var restarted = BuildSut();
        var loaded = await restarted.LoadAsync();

        Assert.NotNull(loaded);
        Assert.Equal(session.Id, loaded!.Id);
        Assert.Equal(session.GrantId, loaded.GrantId);
        Assert.Equal(session.EndsAtUtc, loaded.EndsAtUtc);
    }

    [Fact]
    public async Task SaveAsync_ExpandsMachineNameInSharedImageStateDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "clubpay-agent-template-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Agent:DataDirectory"] = Path.Combine(root, "{MACHINE_NAME_LOWER}"),
                })
                .Build();
            var sut = new AgentStateRepository(config, NullLogger<AgentStateRepository>.Instance);

            await sut.SaveAsync(MakeSession());

            Assert.True(File.Exists(Path.Combine(root, Environment.MachineName.ToLowerInvariant(), "agent-state.json")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RecordAppliedAsync_ThenHasAppliedAsync_ReturnsTrueAfterRestart()
    {
        var sut = BuildSut();
        await sut.RecordAppliedAsync("grant_1001");

        var restarted = BuildSut();
        var applied = await restarted.HasAppliedAsync("grant_1001");

        Assert.True(applied);
    }

    [Fact]
    public async Task HasAppliedAsync_WhenGrantNeverRecorded_ReturnsFalse()
    {
        var sut = BuildSut();

        var applied = await sut.HasAppliedAsync("grant_unknown");

        Assert.False(applied);
    }

    [Fact]
    public async Task LoadAsync_WhenFileCorrupted_ReturnsNullAndDoesNotThrow()
    {
        Directory.CreateDirectory(_dataDir);
        await File.WriteAllTextAsync(Path.Combine(_dataDir, "agent-state.json"), "{ not valid json");

        var sut = BuildSut();
        var loaded = await sut.LoadAsync();

        Assert.Null(loaded);
    }

    [Fact]
    public async Task EnqueueAsync_ThenGetPendingAsync_ReturnsInOrder()
    {
        var sut = BuildSut();
        var first = new EventEnvelope("event", "session_started", "ev_1", DateTime.UtcNow, new { });
        var second = new EventEnvelope("event", "time_low", "ev_2", DateTime.UtcNow, new { });

        await sut.EnqueueAsync(first);
        await sut.EnqueueAsync(second);
        var pending = await sut.GetPendingAsync();

        Assert.Equal(["ev_1", "ev_2"], pending.Select(e => e.EventId));
    }

    [Fact]
    public async Task MarkSentAsync_RemovesFromPending()
    {
        var sut = BuildSut();
        await sut.EnqueueAsync(new EventEnvelope("event", "session_started", "ev_1", DateTime.UtcNow, new { }));
        await sut.EnqueueAsync(new EventEnvelope("event", "time_low", "ev_2", DateTime.UtcNow, new { }));

        await sut.MarkSentAsync("ev_1");
        var pending = await sut.GetPendingAsync();

        Assert.Equal(["ev_2"], pending.Select(e => e.EventId));
    }

    [Fact]
    public async Task ClearAsync_RemovesPersistedSession()
    {
        var sut = BuildSut();
        await sut.SaveAsync(MakeSession());

        await sut.ClearAsync();
        var restarted = BuildSut();
        var loaded = await restarted.LoadAsync();

        Assert.Null(loaded);
    }
}
