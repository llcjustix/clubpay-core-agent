using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ClubPay.Agent.Client.Services;
using ClubPay.Agent.Core;
using ClubPay.Agent.Core.Contracts;
using ClubPay.Agent.Core.Contracts.Enums;
using ClubPay.Agent.Core.Contracts.Events;
using ClubPay.Agent.Core.Contracts.Payloads;
using ClubPay.Agent.Core.Exceptions;
using ClubPay.Agent.Core.Services;

namespace ClubPay.Agent.Client.Tests.Services;

public class CommandDispatcherServiceTests
{
    private sealed class Mocks
    {
        public Mock<ISessionCoordinator> Coordinator { get; } = new();
        public Mock<ICommandValidator> Validator { get; } = new();
        public Mock<IControllerChannel> Channel { get; } = new();
        public Mock<IAgentService> Agent { get; } = new();
        public Mock<ICommandIdempotencyStore> ResultStore { get; } = new();

        public Mocks()
        {
            Channel.Setup(c => c.PublishEventAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Agent.SetupGet(a => a.ExternalPcId).Returns("club12-pc07");
            ResultStore.Setup(s => s.FindCommandResultAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((StoredCommandResult?)null);
            ResultStore.Setup(s => s.RecordCommandResultAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CommandResultEnvelope>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }

        public CommandDispatcherService BuildSut() => new(Coordinator.Object, Validator.Object, Channel.Object, Agent.Object,
            ResultStore.Object, NullLogger<CommandDispatcherService>.Instance);
    }

    private static CommandEnvelope MakeGetStatusCommand(string commandId = "cmd_1") =>
        new(Constants.ControllerChannel.MessageType.Command, Constants.ControllerChannel.CommandName.GetStatus, commandId, DateTime.UtcNow, null);

    [Fact]
    public async Task DispatchAsync_WhenSessionCommandExceptionThrown_PublishesCommandFailedWithMappedErrorCode()
    {
        var m = new Mocks();
        m.Coordinator.Setup(c => c.GetStatus()).Throws(new SessionCommandException(ErrorCode.PcBusy, "busy"));
        var sut = m.BuildSut();
        var command = MakeGetStatusCommand();

        var result = await sut.DispatchAsync(command);

        Assert.Equal("error", result.Status);
        Assert.Equal(ErrorCode.PcBusy, result.ErrorCode);
        m.Channel.Verify(c => c.PublishEventAsync(
            "command_failed",
            It.Is<object>(o => ((CommandFailedEvent)o).CommandId == command.CommandId
                && ((CommandFailedEvent)o).ExternalPcId == "club12-pc07"
                && ((CommandFailedEvent)o).ErrorCode == ErrorCode.PcBusy),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchAsync_WhenUnexpectedExceptionThrown_PublishesCommandFailedWithInternalError()
    {
        var m = new Mocks();
        m.Coordinator.Setup(c => c.GetStatus()).Throws(new InvalidOperationException("boom"));
        var sut = m.BuildSut();
        var command = MakeGetStatusCommand();

        var result = await sut.DispatchAsync(command);

        Assert.Equal("error", result.Status);
        Assert.Equal(ErrorCode.InternalError, result.ErrorCode);
        m.Channel.Verify(c => c.PublishEventAsync(
            "command_failed",
            It.Is<object>(o => ((CommandFailedEvent)o).ErrorCode == ErrorCode.InternalError),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchAsync_WhenSuccessful_DoesNotPublishCommandFailed()
    {
        var m = new Mocks();
        m.Coordinator.Setup(c => c.GetStatus()).Returns(new GetStatusResult(PcState.Free, null, null, null));
        var sut = m.BuildSut();

        var result = await sut.DispatchAsync(MakeGetStatusCommand());

        Assert.Equal("ok", result.Status);
        m.Channel.Verify(c => c.PublishEventAsync(
            It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DispatchAsync_StartSessionWithMissingExtendUrl_LogsIntegrationError()
    {
        var m = new Mocks();
        var loggerMock = new Mock<ILogger<CommandDispatcherService>>();
        var now = DateTime.UtcNow;
        m.Coordinator
            .Setup(c => c.StartSessionAsync(It.IsAny<StartSessionPayload>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new StartSessionResult("cs", 3600, "club12-pc07", "grant_1", now, now.AddSeconds(3600)), false));
        var sut = new CommandDispatcherService(m.Coordinator.Object, m.Validator.Object, m.Channel.Object, m.Agent.Object, m.ResultStore.Object, loggerMock.Object);
        var payload = new StartSessionPayload("club12-pc07", "grant_1", null, 3600, now.AddSeconds(3600), "Standard", now, ExtendUrl: null);
        var command = new CommandEnvelope(
            Constants.ControllerChannel.MessageType.Command,
            Constants.ControllerChannel.CommandName.StartSession,
            "cmd_1",
            now,
            JsonSerializer.SerializeToElement(payload, ControllerJsonOptions.Default));

        await sut.DispatchAsync(command);

        loggerMock.Verify(l => l.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    private static CommandEnvelope MakeCommand<T>(string name, T payload, string commandId = "cmd_1") =>
        new(Constants.ControllerChannel.MessageType.Command, name, commandId, DateTime.UtcNow,
            JsonSerializer.SerializeToElement(payload, ControllerJsonOptions.Default));

    [Fact]
    public async Task DispatchAsync_WhenCommandIdBlank_ReturnsErrorWithoutCallingCoordinator()
    {
        var m = new Mocks();
        m.Validator.Setup(v => v.ValidateCommandId(" "))
            .Throws(new SessionCommandException(ErrorCode.InvalidState, "command_id is required"));
        var sut = m.BuildSut();
        var command = MakeGetStatusCommand(commandId: " ");

        var result = await sut.DispatchAsync(command);

        Assert.Equal("error", result.Status);
        Assert.Equal(ErrorCode.InvalidState, result.ErrorCode);
        m.Coordinator.Verify(c => c.GetStatus(), Times.Never);
    }

    [Fact]
    public async Task DispatchAsync_WhenValidatorThrowsSessionCommandException_PublishesCommandFailedAndDoesNotCallCoordinator()
    {
        var m = new Mocks();
        var payload = new SetRepairPayload("club12-pc07", true);
        m.Validator.Setup(v => v.ValidateSetRepair(It.IsAny<SetRepairPayload>()))
            .Throws(new SessionCommandException(ErrorCode.InvalidState, "external_pc_id does not match this agent"));
        var sut = m.BuildSut();
        var command = MakeCommand(Constants.ControllerChannel.CommandName.SetRepair, payload);

        var result = await sut.DispatchAsync(command);

        Assert.Equal("error", result.Status);
        Assert.Equal(ErrorCode.InvalidState, result.ErrorCode);
        m.Coordinator.Verify(c => c.SetRepairModeAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        m.Channel.Verify(c => c.PublishEventAsync(
            "command_failed", It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleSleep_DeserializesPayloadAndCallsValidateSleep()
    {
        var m = new Mocks();
        var sut = m.BuildSut();
        var command = MakeCommand(Constants.ControllerChannel.CommandName.Sleep, new SleepPayload("club12-pc07"));

        await sut.DispatchAsync(command);

        m.Validator.Verify(v => v.ValidateSleep(It.Is<SleepPayload>(p => p.ExternalPcId == "club12-pc07")), Times.Once);
        m.Coordinator.Verify(c => c.SleepAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleStartAsync_CallsValidateStartSessionBeforeCoordinatorStartSessionAsync()
    {
        var m = new Mocks();
        var now = DateTime.UtcNow;
        var payload = new StartSessionPayload("club12-pc07", "grant_1", null, 3600, now.AddSeconds(3600), "Standard", now);
        m.Validator.Setup(v => v.ValidateStartSession(It.IsAny<StartSessionPayload>()))
            .Throws(new SessionCommandException(ErrorCode.InvalidState, "invalid"));
        var sut = m.BuildSut();
        var command = MakeCommand(Constants.ControllerChannel.CommandName.StartSession, payload);

        await sut.DispatchAsync(command);

        m.Coordinator.Verify(c => c.StartSessionAsync(It.IsAny<StartSessionPayload>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleExtendAsync_CallsValidateExtendSessionBeforeCoordinatorExtendSessionAsync()
    {
        var m = new Mocks();
        var payload = new ExtendSessionPayload("cs_1", "grant_1", null, 600);
        m.Validator.Setup(v => v.ValidateExtendSession(It.IsAny<ExtendSessionPayload>()))
            .Throws(new SessionCommandException(ErrorCode.InvalidState, "invalid"));
        var sut = m.BuildSut();
        var command = MakeCommand(Constants.ControllerChannel.CommandName.ExtendSession, payload);

        await sut.DispatchAsync(command);

        m.Coordinator.Verify(c => c.ExtendSessionAsync(It.IsAny<ExtendSessionPayload>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleEndAsync_CallsValidateEndSessionBeforeCoordinatorEndSessionAsync()
    {
        var m = new Mocks();
        var payload = new EndSessionPayload("cs_1", EndReason.Manager);
        m.Validator.Setup(v => v.ValidateEndSession(It.IsAny<EndSessionPayload>()))
            .Throws(new SessionCommandException(ErrorCode.InvalidState, "invalid"));
        var sut = m.BuildSut();
        var command = MakeCommand(Constants.ControllerChannel.CommandName.EndSession, payload);

        await sut.DispatchAsync(command);

        m.Coordinator.Verify(c => c.EndSessionAsync(It.IsAny<EndSessionPayload>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleLockAsync_CallsValidateLockBeforeCoordinatorLockAsync()
    {
        var m = new Mocks();
        var payload = new LockPayload("club12-pc07", "manager");
        m.Validator.Setup(v => v.ValidateLock(It.IsAny<LockPayload>()))
            .Throws(new SessionCommandException(ErrorCode.InvalidState, "invalid"));
        var sut = m.BuildSut();
        var command = MakeCommand(Constants.ControllerChannel.CommandName.Lock, payload);

        await sut.DispatchAsync(command);

        m.Coordinator.Verify(c => c.LockAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleUnlockAsync_CallsValidateUnlockBeforeCoordinatorUnlockAsync()
    {
        var m = new Mocks();
        var payload = new UnlockPayload("club12-pc07", null);
        m.Validator.Setup(v => v.ValidateUnlock(It.IsAny<UnlockPayload>()))
            .Throws(new SessionCommandException(ErrorCode.InvalidState, "invalid"));
        var sut = m.BuildSut();
        var command = MakeCommand(Constants.ControllerChannel.CommandName.Unlock, payload);

        await sut.DispatchAsync(command);

        m.Coordinator.Verify(c => c.UnlockAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleSetRepairAsync_CallsValidateSetRepairBeforeCoordinatorSetRepairModeAsync()
    {
        var m = new Mocks();
        var payload = new SetRepairPayload("club12-pc07", true);
        m.Validator.Setup(v => v.ValidateSetRepair(It.IsAny<SetRepairPayload>()))
            .Throws(new SessionCommandException(ErrorCode.InvalidState, "invalid"));
        var sut = m.BuildSut();
        var command = MakeCommand(Constants.ControllerChannel.CommandName.SetRepair, payload);

        await sut.DispatchAsync(command);

        m.Coordinator.Verify(c => c.SetRepairModeAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Deserialize_WhenPayloadHasInvalidEnumValue_ReturnsInvalidStateNotInternalError()
    {
        var m = new Mocks();
        var sut = m.BuildSut();
        var rawPayload = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["core_session_id"] = "cs_1",
            ["reason"] = "not_a_real_reason",
        });
        var command = new CommandEnvelope(
            Constants.ControllerChannel.MessageType.Command,
            Constants.ControllerChannel.CommandName.EndSession,
            "cmd_1", DateTime.UtcNow, rawPayload);

        var result = await sut.DispatchAsync(command);

        Assert.Equal("error", result.Status);
        Assert.Equal(ErrorCode.InvalidState, result.ErrorCode);
        m.Coordinator.Verify(c => c.EndSessionAsync(It.IsAny<EndSessionPayload>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─── command_id idempotency ─────────────────────────────────────────────────────────────────

    private static CommandResultEnvelope MakeCachedEnvelope(string commandId) =>
        new(Constants.ControllerChannel.MessageType.CommandResult, commandId, "ok", new EmptyResult());

    // xUnit 2.x cannot discover generic [Theory] methods, so payloads here are typed `object` and
    // serialized via their runtime type rather than via CommandDispatcherServiceTests' generic MakeCommand<T>.
    private static JsonElement SerializePayload(object payload) =>
        JsonSerializer.SerializeToElement(payload, payload.GetType(), ControllerJsonOptions.Default);

    private static CommandEnvelope MakeUntypedCommand(string name, object payload, string commandId) =>
        new(Constants.ControllerChannel.MessageType.Command, name, commandId, DateTime.UtcNow, SerializePayload(payload));

    private static void SeedCachedResult(Mocks m, string commandId, string commandName, object payload, CommandResultEnvelope cached) =>
        m.ResultStore.Setup(s => s.FindCommandResultAsync(commandId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StoredCommandResult(commandName, SerializePayload(payload), cached));

    public static IEnumerable<object[]> TrackedCommandCases()
    {
        var now = DateTime.UtcNow;
        yield return
        [
            Constants.ControllerChannel.CommandName.StartSession,
            new StartSessionPayload("club12-pc07", "grant_1", null, 3600, now.AddSeconds(3600), "Standard", now),
            new StartSessionPayload("club12-pc07", "grant_2", null, 3600, now.AddSeconds(3600), "Standard", now),
        ];
        yield return
        [
            Constants.ControllerChannel.CommandName.ExtendSession,
            new ExtendSessionPayload("cs_1", "grant_1", null, 600),
            new ExtendSessionPayload("cs_1", "grant_1", null, 900),
        ];
        yield return
        [
            Constants.ControllerChannel.CommandName.EndSession,
            new EndSessionPayload("cs_1", EndReason.Manager),
            new EndSessionPayload("cs_1", EndReason.TimeUp),
        ];
        yield return
        [
            Constants.ControllerChannel.CommandName.Lock,
            new LockPayload("club12-pc07", "manager"),
            new LockPayload("club12-pc07", "other"),
        ];
        yield return
        [
            Constants.ControllerChannel.CommandName.Unlock,
            new UnlockPayload("club12-pc07", null),
            new UnlockPayload("club12-pc07", "other"),
        ];
        yield return
        [
            Constants.ControllerChannel.CommandName.Sleep,
            new SleepPayload("club12-pc07"),
            new SleepPayload("club12-pc99"),
        ];
        yield return
        [
            Constants.ControllerChannel.CommandName.SetRepair,
            new SetRepairPayload("club12-pc07", true),
            new SetRepairPayload("club12-pc07", false),
        ];
    }

    private static void VerifyCoordinatorNeverCalledFor(Mocks m, string commandName)
    {
        switch (commandName)
        {
            case Constants.ControllerChannel.CommandName.StartSession:
                m.Coordinator.Verify(c => c.StartSessionAsync(It.IsAny<StartSessionPayload>(), It.IsAny<CancellationToken>()), Times.Never);
                break;
            case Constants.ControllerChannel.CommandName.ExtendSession:
                m.Coordinator.Verify(c => c.ExtendSessionAsync(It.IsAny<ExtendSessionPayload>(), It.IsAny<CancellationToken>()), Times.Never);
                break;
            case Constants.ControllerChannel.CommandName.EndSession:
                m.Coordinator.Verify(c => c.EndSessionAsync(It.IsAny<EndSessionPayload>(), It.IsAny<CancellationToken>()), Times.Never);
                break;
            case Constants.ControllerChannel.CommandName.Lock:
                m.Coordinator.Verify(c => c.LockAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
                break;
            case Constants.ControllerChannel.CommandName.Unlock:
                m.Coordinator.Verify(c => c.UnlockAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
                break;
            case Constants.ControllerChannel.CommandName.Sleep:
                m.Coordinator.Verify(c => c.SleepAsync(It.IsAny<CancellationToken>()), Times.Never);
                break;
            case Constants.ControllerChannel.CommandName.SetRepair:
                m.Coordinator.Verify(c => c.SetRepairModeAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(commandName), commandName, null);
        }
    }

    [Theory]
    [MemberData(nameof(TrackedCommandCases))]
    public async Task DispatchAsync_WhenCommandIdRepeatedWithSamePayload_ReturnsCachedResultWithoutCallingCoordinator(
        string commandName, object payload, object _)
    {
        var m = new Mocks();
        var cached = MakeCachedEnvelope("cmd_dup");
        SeedCachedResult(m, "cmd_dup", commandName, payload, cached);
        var sut = m.BuildSut();
        var command = MakeUntypedCommand(commandName, payload, "cmd_dup");

        var result = await sut.DispatchAsync(command);

        Assert.Same(cached, result);
        VerifyCoordinatorNeverCalledFor(m, commandName);
        m.ResultStore.Verify(s => s.RecordCommandResultAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CommandResultEnvelope>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [MemberData(nameof(TrackedCommandCases))]
    public async Task DispatchAsync_WhenCommandIdRepeatedWithDifferentPayload_ReturnsConflict(
        string commandName, object originalPayload, object differentPayload)
    {
        var m = new Mocks();
        var cached = MakeCachedEnvelope("cmd_dup");
        SeedCachedResult(m, "cmd_dup", commandName, originalPayload, cached);
        var sut = m.BuildSut();
        var command = MakeUntypedCommand(commandName, differentPayload, "cmd_dup");

        var result = await sut.DispatchAsync(command);

        Assert.Equal("error", result.Status);
        Assert.Equal(ErrorCode.Conflict, result.ErrorCode);
        VerifyCoordinatorNeverCalledFor(m, commandName);
        m.Channel.Verify(c => c.PublishEventAsync(
            "command_failed",
            It.Is<object>(o => ((CommandFailedEvent)o).ErrorCode == ErrorCode.Conflict),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchAsync_WhenCommandIdRepeatedWithDifferentCommandName_ReturnsConflict()
    {
        var m = new Mocks();
        var lockPayload = new LockPayload("club12-pc07", "manager");
        var cached = MakeCachedEnvelope("cmd_dup");
        SeedCachedResult(m, "cmd_dup", Constants.ControllerChannel.CommandName.Lock, lockPayload, cached);
        var sut = m.BuildSut();
        var unlockPayload = new UnlockPayload("club12-pc07", null);
        var command = MakeCommand(Constants.ControllerChannel.CommandName.Unlock, unlockPayload, "cmd_dup");

        var result = await sut.DispatchAsync(command);

        Assert.Equal("error", result.Status);
        Assert.Equal(ErrorCode.Conflict, result.ErrorCode);
        m.Coordinator.Verify(c => c.UnlockAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DispatchAsync_GetStatus_RepeatedCommandId_NeverConsultsIdempotencyStore()
    {
        var m = new Mocks();
        m.Coordinator.Setup(c => c.GetStatus()).Returns(new GetStatusResult(PcState.Free, null, null, null));
        var sut = m.BuildSut();

        await sut.DispatchAsync(MakeGetStatusCommand("cmd_1"));
        await sut.DispatchAsync(MakeGetStatusCommand("cmd_1"));

        m.ResultStore.Verify(s => s.FindCommandResultAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        m.ResultStore.Verify(s => s.RecordCommandResultAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CommandResultEnvelope>(), It.IsAny<CancellationToken>()),
            Times.Never);
        m.Coordinator.Verify(c => c.GetStatus(), Times.Exactly(2));
    }

    [Fact]
    public async Task DispatchAsync_WhenResultIsInternalError_DoesNotCacheIt()
    {
        var m = new Mocks();
        m.Coordinator.Setup(c => c.SleepAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));
        var sut = m.BuildSut();
        var command = MakeCommand(Constants.ControllerChannel.CommandName.Sleep, new SleepPayload("club12-pc07"), "cmd_1");

        var result = await sut.DispatchAsync(command);

        Assert.Equal(ErrorCode.InternalError, result.ErrorCode);
        m.ResultStore.Verify(s => s.RecordCommandResultAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CommandResultEnvelope>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DispatchAsync_WhenIdempotencyStoreThrowsOnFind_FailsOpenAndExecutesCommand()
    {
        var m = new Mocks();
        m.ResultStore.Setup(s => s.FindCommandResultAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db unavailable"));
        var sut = m.BuildSut();
        var command = MakeCommand(Constants.ControllerChannel.CommandName.Sleep, new SleepPayload("club12-pc07"), "cmd_1");

        var result = await sut.DispatchAsync(command);

        Assert.Equal("ok", result.Status);
        m.Coordinator.Verify(c => c.SleepAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchAsync_WhenIdempotencyStoreThrowsOnRecord_StillReturnsSuccessfulResult()
    {
        var m = new Mocks();
        m.ResultStore.Setup(s => s.RecordCommandResultAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CommandResultEnvelope>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db unavailable"));
        var sut = m.BuildSut();
        var command = MakeCommand(Constants.ControllerChannel.CommandName.Sleep, new SleepPayload("club12-pc07"), "cmd_1");

        var result = await sut.DispatchAsync(command);

        Assert.Equal("ok", result.Status);
    }

    [Fact]
    public async Task DispatchAsync_ConcurrentIdenticalCommandId_ExecutesCoordinatorExactlyOnce()
    {
        var m = new Mocks();
        // Real store so both racers actually share persisted state, not two independent mock instances.
        var store = new InMemoryCommandIdempotencyStore();
        var recordedFind = new List<string>();
        m.ResultStore.Setup(s => s.FindCommandResultAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string id, CancellationToken ct) => store.FindCommandResultAsync(id, ct));
        m.ResultStore.Setup(s => s.RecordCommandResultAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CommandResultEnvelope>(), It.IsAny<CancellationToken>()))
            .Returns((string id, string name, object? payload, CommandResultEnvelope result, CancellationToken ct) =>
                store.RecordCommandResultAsync(id, name, payload, result, ct));
        m.Coordinator.Setup(c => c.LockAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns<string?, CancellationToken>(async (_, _) => await Task.Delay(50));
        var sut = m.BuildSut();
        var payload = new LockPayload("club12-pc07", "manager");
        var command1 = MakeCommand(Constants.ControllerChannel.CommandName.Lock, payload, "cmd_race");
        var command2 = MakeCommand(Constants.ControllerChannel.CommandName.Lock, payload, "cmd_race");

        var results = await Task.WhenAll(sut.DispatchAsync(command1), sut.DispatchAsync(command2));

        m.Coordinator.Verify(c => c.LockAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("ok", results[0].Status);
        Assert.Equal(results[0].Status, results[1].Status);
        Assert.Equal(results[0].CommandId, results[1].CommandId);
    }

    /// <summary>Minimal thread-safe in-memory stand-in for AgentStateRepository's command_results table,
    /// used only by the concurrency test above so both racing DispatchAsync calls observe the same
    /// persisted state (two independently-configured Moq setups would not share state with each other).</summary>
    private sealed class InMemoryCommandIdempotencyStore
    {
        private readonly Dictionary<string, StoredCommandResult> _rows = [];
        private readonly object _lock = new();

        public Task<StoredCommandResult?> FindCommandResultAsync(string commandId, CancellationToken ct)
        {
            lock (_lock)
                return Task.FromResult(_rows.TryGetValue(commandId, out var row) ? row : null);
        }

        public Task RecordCommandResultAsync(string commandId, string commandName, object? payload, CommandResultEnvelope result, CancellationToken ct)
        {
            lock (_lock)
                _rows.TryAdd(commandId, new StoredCommandResult(commandName, JsonSerializer.SerializeToElement(payload, ControllerJsonOptions.Default), result));
            return Task.CompletedTask;
        }
    }
}
