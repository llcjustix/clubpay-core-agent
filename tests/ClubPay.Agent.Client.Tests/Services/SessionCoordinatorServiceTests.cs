using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ClubPay.Agent.Client.Services;
using ClubPay.Agent.Core.Contracts.Enums;
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
        public Mock<IControllerChannel> Channel { get; } = new();
        public Mock<IAgentService> Agent { get; } = new();
        public Mock<IKioskLockService> KioskLock { get; } = new();
        public Mock<IProcessCleanupService> ProcessCleanup { get; } = new();
        public Mock<IIdleDetectionService> Idle { get; } = new();
        public Mock<ISystemClock> Clock { get; } = new();

        public Mocks()
        {
            Store.Setup(s => s.SaveAsync(It.IsAny<Session>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Store.Setup(s => s.ClearAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Idempotency.Setup(i => i.HasAppliedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
            Idempotency.Setup(i => i.RecordAppliedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Channel.Setup(c => c.PublishEventAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Agent.SetupGet(a => a.ExternalPcId).Returns("club12-pc07");
            Clock.SetupGet(c => c.UtcNow).Returns(Now);
        }

        public SessionCoordinatorService BuildSut() => new(
            Store.Object, Idempotency.Object, Channel.Object, Agent.Object,
            KioskLock.Object, ProcessCleanup.Object, Idle.Object, Clock.Object,
            NullLogger<SessionCoordinatorService>.Instance);
    }

    private static StartSessionPayload MakeStartPayload(string grantId = "grant_1001") =>
        new("club12-pc07", grantId, PaymentOrderId: null, GrantedSeconds: 3600, EndsAt: Now.AddSeconds(3600), Zone: "Standard", StartAt: Now);

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
        m.Channel.Verify(c => c.PublishEventAsync("session_started", It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
        m.KioskLock.Verify(k => k.SetMode(KioskLockMode.Session), Times.Once);
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
        m.Channel.Verify(c => c.PublishEventAsync("session_ended", It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
        m.ProcessCleanup.Verify(p => p.KillSessionProcesses(), Times.Once);
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
}
