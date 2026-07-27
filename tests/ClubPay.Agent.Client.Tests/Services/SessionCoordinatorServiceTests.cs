using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ClubPay.Agent.Client.Services;
using ClubPay.Agent.Core.Contracts.Enums;
using ClubPay.Agent.Core.Contracts.Events;
using ClubPay.Agent.Core.Contracts.Payloads;
using ClubPay.Agent.Core.Exceptions;
using ClubPay.Agent.Core.Models;
using ClubPay.Agent.Core.Services;

namespace ClubPay.Agent.Client.Tests.Services;

public class SessionCoordinatorServiceTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private sealed class Mocks
    {
        public Mock<ISessionStore> Store { get; } = new();
        public Mock<IGrantIdempotencyStore> Idempotency { get; } = new();
        public Mock<IConnectionStateProvider> ConnectionState { get; } = new();
        public Mock<IControllerOutbox> Outbox { get; } = new();
        public Mock<IAgentService> Agent { get; } = new();
        public Mock<IKioskLockService> KioskLock { get; } = new();
        public Mock<IProcessCleanupService> ProcessCleanup { get; } = new();
        public Mock<IIdleDetectionService> Idle { get; } = new();
        public Mock<ISystemClock> Clock { get; } = new();
        public IConfiguration Config { get; set; } = new ConfigurationBuilder().Build();

        public Mocks()
        {
            Store.Setup(s => s.SaveAsync(It.IsAny<Session>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Store.Setup(s => s.ClearAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Idempotency.Setup(i => i.HasAppliedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
            Idempotency.Setup(i => i.RecordAppliedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Outbox.Setup(o => o.PublishEventAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Agent.SetupGet(a => a.ExternalPcId).Returns("club12-pc07");
            Clock.SetupGet(c => c.UtcNow).Returns(Now);
        }

        public SessionCoordinatorService BuildSut() => new(
            Store.Object, Idempotency.Object, ConnectionState.Object, Outbox.Object, Agent.Object,
            KioskLock.Object, ProcessCleanup.Object, Idle.Object, Clock.Object, Config,
            NullLogger<SessionCoordinatorService>.Instance);
    }

    private static StartSessionPayload MakeStartPayload(string grantId = "grant_1001", string? extendUrl = null) =>
        new("club12-pc07", grantId, PaymentOrderId: null, GrantedSeconds: 3600, EndsAt: Now.AddSeconds(3600), Zone: "Standard", StartAt: Now, ExtendUrl: extendUrl);

    private static Session MakeExpiredSession() =>
        new(
            Guid.NewGuid(), "club12-pc07",
            new Tariff(Guid.NewGuid(), "Standard", ZoneType.Standard, 60, 0),
            StartedAtUtc: Now.AddSeconds(-3700), GrantedSeconds: 3600,
            CoreSessionId: Guid.NewGuid(), GrantId: "grant_expired", EndsAtUtc: Now.AddSeconds(-100), Zone: "Standard");

    [Fact]
    public async Task StartSessionAsync_WhenRepairModeOn_ThrowsPcInRepair()
    {
        var m = new Mocks();
        var sut = m.BuildSut();
        await sut.SetRepairModeAsync(true);

        var ex = await Assert.ThrowsAsync<SessionCommandException>(() => sut.StartSessionAsync(MakeStartPayload()));

        Assert.Equal(ErrorCode.PcInRepair, ex.ErrorCode);
    }

    [Fact]
    public async Task StartSessionAsync_WhenGrantIdAlreadyApplied_ReturnsDuplicateWithoutAddingTime()
    {
        var m = new Mocks();
        m.Idempotency.Setup(i => i.HasAppliedAsync("grant_1001", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var sut = m.BuildSut();

        var (result, isDuplicate) = await sut.StartSessionAsync(MakeStartPayload());

        Assert.True(isDuplicate);
        Assert.Equal(0, result.RemainingSeconds);
        m.Store.Verify(s => s.SaveAsync(It.IsAny<Session>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StartSessionAsync_WhenLocked_TransitionsToActiveAndPublishesSessionStarted()
    {
        var m = new Mocks();
        var sut = m.BuildSut();

        var (result, isDuplicate) = await sut.StartSessionAsync(MakeStartPayload());

        Assert.False(isDuplicate);
        Assert.Equal(3600, result.RemainingSeconds);
        Assert.Equal(AgentState.Active, sut.State);
        m.Outbox.Verify(o => o.PublishEventAsync("session_started", It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
        m.KioskLock.Verify(k => k.SetMode(KioskLockMode.Session), Times.Once);
    }

    [Fact]
    public async Task StartSessionAsync_WhenExtendUrlProvided_PersistsOnSessionAndResult()
    {
        var m = new Mocks();
        var sut = m.BuildSut();
        const string extendUrl = "https://clubpay.justix.uz/qr/se_abc123";

        var (result, _) = await sut.StartSessionAsync(MakeStartPayload(extendUrl: extendUrl));

        Assert.Equal(extendUrl, sut.CurrentSession?.ExtendUrl);
        Assert.Equal("club12-pc07", result.ExternalPcId);
        Assert.Equal("grant_1001", result.GrantId);
        Assert.Equal(Now, result.StartedAt);
        Assert.Equal(Now.AddSeconds(3600), result.EndsAt);
    }

    [Fact]
    public async Task StartSessionAsync_WhenGrantIdAlreadyApplied_ResultReflectsCurrentSession()
    {
        var m = new Mocks();
        m.Idempotency.Setup(i => i.HasAppliedAsync("grant_1001", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var sut = m.BuildSut();

        var (result, _) = await sut.StartSessionAsync(MakeStartPayload());

        Assert.Equal("club12-pc07", result.ExternalPcId);
        Assert.Equal("grant_1001", result.GrantId);
    }

    [Fact]
    public async Task StartSessionAsync_WhenAlreadyActive_ThrowsPcBusy()
    {
        var m = new Mocks();
        var sut = m.BuildSut();
        await sut.StartSessionAsync(MakeStartPayload("grant_first"));

        var ex = await Assert.ThrowsAsync<SessionCommandException>(() => sut.StartSessionAsync(MakeStartPayload("grant_second")));

        Assert.Equal(ErrorCode.PcBusy, ex.ErrorCode);
    }

    [Fact]
    public async Task ExtendSessionAsync_WhenGrantIdAlreadyApplied_ReturnsCurrentRemainingSecondsWithoutAddingTime()
    {
        var m = new Mocks();
        var sut = m.BuildSut();
        await sut.StartSessionAsync(MakeStartPayload());

        m.Idempotency.Setup(i => i.HasAppliedAsync("grant_extend", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var (result, isDuplicate) = await sut.ExtendSessionAsync(new ExtendSessionPayload("cs", "grant_extend", null, 1800));

        Assert.True(isDuplicate);
        Assert.Equal(3600, result.RemainingSeconds);
    }

    [Fact]
    public async Task ExtendSessionAsync_WhenActiveBeforeExpiry_AddsSecondsToRemainingBudget()
    {
        var m = new Mocks();
        var sut = m.BuildSut();
        await sut.StartSessionAsync(MakeStartPayload());

        m.Clock.SetupGet(c => c.UtcNow).Returns(Now.AddSeconds(100));
        var (result, isDuplicate) = await sut.ExtendSessionAsync(new ExtendSessionPayload("cs", "grant_extend", null, 1800));

        Assert.False(isDuplicate);
        Assert.Equal(3600 - 100 + 1800, result.RemainingSeconds);
        Assert.Equal(AgentState.Active, sut.State);
    }

    [Fact]
    public async Task ExtendSessionAsync_WhenAlreadyExpiredAfterLongWait_GrantsFullAddedSecondsNotReducedByWait()
    {
        var m = new Mocks();
        var sut = m.BuildSut();
        await sut.StartSessionAsync(MakeStartPayload());

        // Granted time (3600s) has fully elapsed, plus the PC sat "frozen"/waiting for another
        // 120s before the extend command arrived — that wait must not eat into added_seconds.
        m.Clock.SetupGet(c => c.UtcNow).Returns(Now.AddSeconds(3600 + 120));
        var (result, isDuplicate) = await sut.ExtendSessionAsync(new ExtendSessionPayload("cs", "grant_extend", null, 65));

        Assert.False(isDuplicate);
        Assert.Equal(65, result.RemainingSeconds);
        Assert.Equal(AgentState.Active, sut.State);
    }

    [Fact]
    public async Task ExtendSessionAsync_WhenActiveBeforeExpiry_PreservesExtendUrlAndFillsResultFields()
    {
        var m = new Mocks();
        var sut = m.BuildSut();
        const string extendUrl = "https://clubpay.justix.uz/qr/se_abc123";
        await sut.StartSessionAsync(MakeStartPayload(extendUrl: extendUrl));

        m.Clock.SetupGet(c => c.UtcNow).Returns(Now.AddSeconds(100));
        var (result, _) = await sut.ExtendSessionAsync(new ExtendSessionPayload("cs", "grant_extend", null, 1800));

        Assert.Equal(extendUrl, sut.CurrentSession?.ExtendUrl);
        Assert.Equal("cs", result.CoreSessionId);
        Assert.Equal("grant_extend", result.GrantId);
        Assert.Equal(Now.AddSeconds(100).AddSeconds(result.RemainingSeconds), result.NewEndsAt);
    }

    [Fact]
    public async Task ExtendSessionAsync_WhenLocked_ThrowsInvalidState()
    {
        var m = new Mocks();
        var sut = m.BuildSut();

        var ex = await Assert.ThrowsAsync<SessionCommandException>(
            () => sut.ExtendSessionAsync(new ExtendSessionPayload("cs", "grant_extend", null, 1800)));

        Assert.Equal(ErrorCode.InvalidState, ex.ErrorCode);
    }

    [Fact]
    public async Task EndSessionAsync_WithReason_PublishesSessionEndedWithSameReasonAndTransitionsToLocked()
    {
        var m = new Mocks();
        var sut = m.BuildSut();
        var (started, _) = await sut.StartSessionAsync(MakeStartPayload());

        var result = await sut.EndSessionAsync(new EndSessionPayload(started.CoreSessionId, EndReason.Manager));

        Assert.Equal(AgentState.Locked, sut.State);
        Assert.Null(sut.CurrentSession);
        m.Outbox.Verify(o => o.PublishEventAsync("session_ended", It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
        m.ProcessCleanup.Verify(p => p.KillSessionProcesses(), Times.Never);
        Assert.Equal(3600, result.RemainingSeconds);
        Assert.Equal(0, result.ConsumedSeconds);
    }

    [Fact]
    public async Task EndSessionAsync_WhenNoActiveSession_ThrowsInvalidState()
    {
        var m = new Mocks();
        var sut = m.BuildSut();

        var ex = await Assert.ThrowsAsync<SessionCommandException>(
            () => sut.EndSessionAsync(new EndSessionPayload("cs", EndReason.Manager)));

        Assert.Equal(ErrorCode.InvalidState, ex.ErrorCode);
    }

    [Fact]
    public async Task LockAsync_DuringActiveSession_DoesNotChangeSessionState()
    {
        var m = new Mocks();
        var sut = m.BuildSut();
        await sut.StartSessionAsync(MakeStartPayload());

        await sut.LockAsync("manager override");

        Assert.Equal(AgentState.Active, sut.State);
        Assert.True(sut.IsManagerLocked);
        Assert.NotNull(sut.CurrentSession);
    }

    [Fact]
    public async Task UnlockAsync_AfterLock_ClearsManagerLockedFlag()
    {
        var m = new Mocks();
        var sut = m.BuildSut();
        await sut.LockAsync(null);

        await sut.UnlockAsync(null);

        Assert.False(sut.IsManagerLocked);
    }

    [Fact]
    public async Task SetRepairModeAsync_WhenOn_RejectsSubsequentStartSession()
    {
        var m = new Mocks();
        var sut = m.BuildSut();

        await sut.SetRepairModeAsync(true);
        var ex = await Assert.ThrowsAsync<SessionCommandException>(() => sut.StartSessionAsync(MakeStartPayload()));

        Assert.Equal(ErrorCode.PcInRepair, ex.ErrorCode);
    }

    [Fact]
    public async Task SleepAsync_WhenSessionActive_ThrowsPcBusy()
    {
        var m = new Mocks();
        var sut = m.BuildSut();
        await sut.StartSessionAsync(MakeStartPayload());

        var ex = await Assert.ThrowsAsync<SessionCommandException>(() => sut.SleepAsync());

        Assert.Equal(ErrorCode.PcBusy, ex.ErrorCode);
    }

    [Fact]
    public async Task WakeAckAsync_AlwaysReturnsOkWithoutSideEffects()
    {
        var m = new Mocks();
        var sut = m.BuildSut();

        var result = await sut.WakeAckAsync();

        Assert.NotNull(result);
        m.Agent.Verify(a => a.SleepAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetStatus_WhenLocked_ReturnsFreeWithNoSession()
    {
        var m = new Mocks();
        var sut = m.BuildSut();

        var status = sut.GetStatus();

        Assert.Equal(PcState.Free, status.PcState);
        Assert.Null(status.SessionState);
        Assert.Null(status.CoreSessionId);
    }

    [Fact]
    public async Task GetStatus_WhenActive_ReturnsOccupiedWithRemainingSeconds()
    {
        var m = new Mocks();
        var sut = m.BuildSut();
        await sut.StartSessionAsync(MakeStartPayload());

        var status = sut.GetStatus();

        Assert.Equal(PcState.Occupied, status.PcState);
        Assert.Equal(SessionState.Active, status.SessionState);
        Assert.Equal(3600, status.RemainingSeconds);
    }

    [Fact]
    public async Task PublishHeartbeatAsync_WhenChannelConnected_ReportsControllerSeenAndReachable()
    {
        var m = new Mocks();
        m.ConnectionState.SetupGet(c => c.ConnectionState).Returns(ChannelConnectionState.Connected);
        var sut = m.BuildSut();

        await sut.PublishHeartbeatAsync(CancellationToken.None);

        m.Outbox.Verify(o => o.PublishEventAsync(
            "heartbeat",
            It.Is<object>(p => ((HeartbeatEvent)p).ControllersSeen == 1 && ((HeartbeatEvent)p).ServerReachable),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishHeartbeatAsync_WhenChannelDisconnected_ReportsZeroControllersAndUnreachable()
    {
        var m = new Mocks();
        m.ConnectionState.SetupGet(c => c.ConnectionState).Returns(ChannelConnectionState.Disconnected);
        var sut = m.BuildSut();

        await sut.PublishHeartbeatAsync(CancellationToken.None);

        m.Outbox.Verify(o => o.PublishEventAsync(
            "heartbeat",
            It.Is<object>(p => ((HeartbeatEvent)p).ControllersSeen == 0 && !((HeartbeatEvent)p).ServerReachable),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_SessionExpiredWhileDown_KillsProcessesMatchingConfiguredLauncherProcessNames()
    {
        var m = new Mocks();
        m.Config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Launcher:Apps:0:ProcessNames:0"] = "cs2",
            })
            .Build();
        m.Store.SetupGet(s => s.Current).Returns(MakeExpiredSession());
        m.ProcessCleanup
            .Setup(p => p.GetProcessIdsStartedAfterBaseline(
                It.IsAny<IReadOnlySet<int>>(),
                It.Is<IReadOnlySet<string>>(n => n.Contains("cs2")),
                It.IsAny<string?>()))
            .Returns(new List<int> { 4242 });
        var sut = m.BuildSut();

        await sut.StartAsync();

        m.ProcessCleanup.Verify(
            p => p.KillProcesses(It.Is<IReadOnlyCollection<int>>(ids => ids.Contains(4242))),
            Times.Once);
    }

    [Fact]
    public async Task StartAsync_SessionExpiredWhileDown_NoLauncherProcessNamesConfigured_DoesNotCallKillProcesses()
    {
        var m = new Mocks();
        m.Store.SetupGet(s => s.Current).Returns(MakeExpiredSession());
        var sut = m.BuildSut();

        await sut.StartAsync();

        m.ProcessCleanup.Verify(p => p.KillProcesses(It.IsAny<IReadOnlyCollection<int>>()), Times.Never);
    }

    [Fact]
    public async Task StartAsync_SessionNotExpired_DoesNotRunOrphanRecoverySweep()
    {
        var m = new Mocks();
        m.Config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Launcher:Apps:0:ProcessNames:0"] = "cs2",
            })
            .Build();
        var activeSession = MakeExpiredSession() with { EndsAtUtc = Now.AddSeconds(3600) };
        m.Store.SetupGet(s => s.Current).Returns(activeSession);
        var sut = m.BuildSut();

        await sut.StartAsync();

        m.ProcessCleanup.Verify(
            p => p.GetProcessIdsStartedAfterBaseline(It.IsAny<IReadOnlySet<int>>(), It.IsAny<IReadOnlySet<string>>(), It.IsAny<string?>()),
            Times.Never);
    }
}
