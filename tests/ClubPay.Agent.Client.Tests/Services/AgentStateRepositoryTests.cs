using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ClubPay.Agent.Client.Services;
using ClubPay.Agent.Core;
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

    private AgentStateRepository BuildSut(ILogger<AgentStateRepository>? logger = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:DataDirectory"] = _dataDir })
            .Build();
        return new AgentStateRepository(config, logger ?? NullLogger<AgentStateRepository>.Instance);
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

    [Fact]
    public async Task PublishEventAsync_ThenGetPendingAsync_ReturnsEnvelope()
    {
        var sut = BuildSut();

        await sut.PublishEventAsync("session_started", new { });
        var pending = await sut.GetPendingAsync();

        Assert.Single(pending);
        Assert.Equal("session_started", pending[0].Name);
    }

    [Fact]
    public async Task PublishEventAsync_WhenHeartbeatTwice_CoalescesToLatestOnly()
    {
        var sut = BuildSut();

        await sut.PublishEventAsync(Constants.ControllerChannel.EventName.Heartbeat, new { seq = 1 });
        await sut.PublishEventAsync(Constants.ControllerChannel.EventName.Heartbeat, new { seq = 2 });
        var pending = await sut.GetPendingAsync();

        Assert.Single(pending);
        Assert.Equal(new { seq = 2 }, pending[0].Payload);
    }

    [Fact]
    public async Task PublishEventAsync_WhenSessionEventsTwice_KeepsBothNotCoalesced()
    {
        var sut = BuildSut();

        await sut.PublishEventAsync("session_started", new { });
        await sut.PublishEventAsync("session_started", new { });
        var pending = await sut.GetPendingAsync();

        Assert.Equal(2, pending.Count);
    }

    [Fact]
    public async Task WaitForPendingAsync_AfterPublish_CompletesWithoutTimeout()
    {
        var sut = BuildSut();
        var waitTask = sut.WaitForPendingAsync(TimeSpan.FromSeconds(5));

        await sut.PublishEventAsync("session_started", new { });

        var completed = await Task.WhenAny(waitTask, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Same(waitTask, completed);
    }

    [Fact]
    public async Task PublishEventAsync_WhenOutboxExceedsLimit_LogsWarning()
    {
        var logger = new Mock<ILogger<AgentStateRepository>>();
        var sut = BuildSut(logger.Object);

        for (int i = 0; i <= Constants.ControllerChannel.MaxOutboxSize; i++)
            await sut.PublishEventAsync("session_started", new { });

        logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task PublishEventAsync_WhenHeartbeat_NeverPersistedAcrossRestart()
    {
        var sut = BuildSut();

        await sut.PublishEventAsync(Constants.ControllerChannel.EventName.Heartbeat, new { });

        var restarted = BuildSut();
        var pending = await restarted.GetPendingAsync();

        Assert.Empty(pending);
    }

    [Fact]
    public async Task PublishEventAsync_WhenPcStateChanged_NeverPersistedAcrossRestart()
    {
        var sut = BuildSut();

        await sut.PublishEventAsync(Constants.ControllerChannel.EventName.PcStateChanged, new { });

        var restarted = BuildSut();
        var pending = await restarted.GetPendingAsync();

        Assert.Empty(pending);
    }

    [Fact]
    public async Task PublishEventAsync_WhenSessionStarted_PersistsAcrossRestart()
    {
        var sut = BuildSut();

        await sut.PublishEventAsync("session_started", new { });

        var restarted = BuildSut();
        var pending = await restarted.GetPendingAsync();

        Assert.Single(pending);
        Assert.Equal("session_started", pending[0].Name);
    }

    [Fact]
    public async Task GetPendingAsync_WhenTelemetryAndMoneyEventsBothPending_ReturnsBoth()
    {
        var sut = BuildSut();

        await sut.PublishEventAsync(Constants.ControllerChannel.EventName.Heartbeat, new { });
        await sut.PublishEventAsync("session_started", new { });
        var pending = await sut.GetPendingAsync();

        Assert.Equal(2, pending.Count);
        Assert.Contains(pending, e => e.Name == Constants.ControllerChannel.EventName.Heartbeat);
        Assert.Contains(pending, e => e.Name == "session_started");
    }

    [Fact]
    public async Task MarkSentAsync_WhenEventIsTelemetry_RemovesFromInMemoryPending()
    {
        var sut = BuildSut();
        await sut.PublishEventAsync(Constants.ControllerChannel.EventName.Heartbeat, new { });
        var eventId = (await sut.GetPendingAsync()).Single().EventId;

        await sut.MarkSentAsync(eventId);
        var pending = await sut.GetPendingAsync();

        Assert.Empty(pending);
    }

    [Fact]
    public async Task PublishEventAsync_WhenManyHeartbeatsWhileOffline_DoesNotCountTowardOutboxLimit()
    {
        var logger = new Mock<ILogger<AgentStateRepository>>();
        var sut = BuildSut(logger.Object);

        for (int i = 0; i < Constants.ControllerChannel.MaxOutboxSize * 3; i++)
            await sut.PublishEventAsync(Constants.ControllerChannel.EventName.Heartbeat, new { });

        logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }
}
