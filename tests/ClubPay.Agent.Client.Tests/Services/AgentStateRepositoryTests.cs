using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Text.Json;
using ClubPay.Agent.Client.Services;
using ClubPay.Agent.Core;
using ClubPay.Agent.Core.Contracts;
using ClubPay.Agent.Core.Models;
using ClubPay.Agent.Core.Services;

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

    [Fact]
    public async Task LoadAsync_WhenLegacyJsonExists_MigratesStateAndKeepsBackup()
    {
        Directory.CreateDirectory(_dataDir);
        var legacy = new
        {
            Session = MakeSession(),
            AppliedGrantIds = new[] { new { GrantId = "grant_legacy", AppliedAtUtc = DateTime.UtcNow } },
            Outbox = new[] { new EventEnvelope("event", "session_started", "ev_legacy", DateTime.UtcNow, new { }) },
        };
        await File.WriteAllTextAsync(Path.Combine(_dataDir, "agent-state.json"), JsonSerializer.Serialize(legacy, ControllerJsonOptions.Default));

        var sut = BuildSut();
        var session = await sut.LoadAsync();

        Assert.NotNull(session);
        Assert.True(await sut.HasAppliedAsync("grant_legacy"));
        Assert.Contains((await sut.GetPendingAsync()), e => e.EventId == "ev_legacy");
        Assert.True(File.Exists(Path.Combine(_dataDir, "agent-state.db")));
        Assert.Empty(Directory.GetFiles(_dataDir, "agent-state.json"));
        Assert.Single(Directory.GetFiles(_dataDir, "agent-state.json.migrated-*.bak"));
    }

    [Fact]
    public async Task CommitStartAsync_PersistsSessionGrantAndEventTogether()
    {
        var sut = BuildSut();
        var session = MakeSession();
        var evt = new EventEnvelope("event", "session_started", "ev_atomic", DateTime.UtcNow, new { });

        await sut.CommitStartAsync(session, "grant_atomic", evt);

        var restarted = BuildSut();
        var loaded = await restarted.LoadAsync();
        Assert.Equal(session.Id, loaded!.Id);
        Assert.True(await restarted.HasAppliedAsync("grant_atomic"));
        Assert.Contains((await restarted.GetPendingAsync()), e => e.EventId == "ev_atomic");
    }

    [Fact]
    public async Task CommitEndAsync_RemovesSessionAndQueuesEndEvent()
    {
        var sut = BuildSut();
        await sut.SaveAsync(MakeSession());

        await sut.CommitEndAsync(new EventEnvelope("event", "session_ended", "ev_end", DateTime.UtcNow, new { }));

        var restarted = BuildSut();
        Assert.Null(await restarted.LoadAsync());
        Assert.Contains((await restarted.GetPendingAsync()), e => e.EventId == "ev_end");
    }

    private static CommandResultEnvelope MakeCommandResult(string commandId) =>
        new("command_result", commandId, "ok", new { remaining_seconds = 100 });

    [Fact]
    public async Task RecordCommandResultAsync_ThenFindCommandResultAsync_ReturnsStoredResultAfterRestart()
    {
        var sut = BuildSut();
        var payload = new { external_pc_id = "PC-12" };
        var result = MakeCommandResult("cmd_1");

        await sut.RecordCommandResultAsync("cmd_1", "lock", payload, result);

        var restarted = BuildSut();
        var found = await restarted.FindCommandResultAsync("cmd_1");

        Assert.NotNull(found);
        Assert.Equal("lock", found!.CommandName);
        Assert.Equal(result.Status, found.Result.Status);
        Assert.Equal(result.CommandId, found.Result.CommandId);
    }

    [Fact]
    public async Task FindCommandResultAsync_WhenNeverRecorded_ReturnsNull()
    {
        var sut = BuildSut();

        var found = await sut.FindCommandResultAsync("cmd_unknown");

        Assert.Null(found);
    }

    [Fact]
    public async Task RecordCommandResultAsync_CalledTwiceForSameCommandId_KeepsFirstResult()
    {
        var sut = BuildSut();
        var payload = new { };

        await sut.RecordCommandResultAsync("cmd_1", "lock", payload, MakeCommandResult("cmd_1"));
        await sut.RecordCommandResultAsync("cmd_1", "unlock", payload, new CommandResultEnvelope("command_result", "cmd_1", "error"));

        var found = await sut.FindCommandResultAsync("cmd_1");

        Assert.NotNull(found);
        Assert.Equal("lock", found!.CommandName);
        Assert.Equal("ok", found.Result.Status);
    }

    [Fact]
    public async Task RecordCommandResultAsync_PrunesEntriesOlderThanRetention()
    {
        Directory.CreateDirectory(_dataDir);
        var sut = BuildSut();
        await sut.LoadAsync(); // creates the DB file

        var dbPath = Path.Combine(_dataDir, "agent-state.db");
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = false }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO command_results(command_id, command_name, payload_json, result_json, created_at_utc) VALUES($id, 'lock', '{}', '{}', $at);";
            command.Parameters.AddWithValue("$id", "cmd_old");
            command.Parameters.AddWithValue("$at", DateTime.UtcNow.AddDays(-31).ToString("O"));
            await command.ExecuteNonQueryAsync();
        }

        await sut.RecordCommandResultAsync("cmd_new", "lock", new { }, MakeCommandResult("cmd_new"));

        Assert.Null(await sut.FindCommandResultAsync("cmd_old"));
        Assert.NotNull(await sut.FindCommandResultAsync("cmd_new"));
    }
}
