using Moq;
using ClubPay.Agent.Core.Contracts.Enums;
using ClubPay.Agent.Core.Contracts.Payloads;
using ClubPay.Agent.Core.Exceptions;
using ClubPay.Agent.Core.Models;
using ClubPay.Agent.Core.Services;

namespace ClubPay.Agent.Core.Tests.Services;

public class CommandValidationServiceTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private const string OwnPcId = "club12-pc07";

    private sealed class Mocks
    {
        public Mock<IAgentService> Agent { get; } = new();
        public Mock<ISessionCoordinator> Coordinator { get; } = new();
        public Mock<ISystemClock> Clock { get; } = new();

        public Mocks()
        {
            Agent.SetupGet(a => a.ExternalPcId).Returns(OwnPcId);
            Clock.SetupGet(c => c.UtcNow).Returns(Now);
        }

        public CommandValidationService BuildSut() => new(Agent.Object, Coordinator.Object, Clock.Object);
    }

    private static Session MakeSession(Guid coreSessionId) =>
        new(
            Guid.NewGuid(), OwnPcId,
            new Tariff(Guid.NewGuid(), "Standard", ZoneType.Standard, 60, 0),
            StartedAtUtc: Now.AddMinutes(-5), GrantedSeconds: 3600,
            CoreSessionId: coreSessionId, GrantId: "grant_1", EndsAtUtc: Now.AddMinutes(55), Zone: "Standard");

    private static StartSessionPayload MakeStartPayload(
        string externalPcId = OwnPcId, string grantId = "grant_1001", int grantedSeconds = 3600,
        string? zone = "Standard", DateTime? startAt = null, DateTime? endsAt = null) =>
        new(externalPcId, grantId, PaymentOrderId: null, grantedSeconds, endsAt ?? Now.AddSeconds(grantedSeconds), zone, startAt ?? Now);

    // ── ValidateCommandId ───────────────────────────────────────────────

    [Fact]
    public void ValidateCommandId_WhenBlank_ThrowsInvalidState()
    {
        var sut = new Mocks().BuildSut();

        var ex = Assert.Throws<SessionCommandException>(() => sut.ValidateCommandId("  "));

        Assert.Equal(ErrorCode.InvalidState, ex.ErrorCode);
    }

    [Fact]
    public void ValidateCommandId_WhenNonEmpty_DoesNotThrow() =>
        new Mocks().BuildSut().ValidateCommandId("cmd_1");

    // ── ValidateStartSession ────────────────────────────────────────────

    [Fact]
    public void ValidateStartSession_WhenExternalPcIdMatchesAgent_DoesNotThrow() =>
        new Mocks().BuildSut().ValidateStartSession(MakeStartPayload());

    [Fact]
    public void ValidateStartSession_WhenExternalPcIdDiffersFromAgent_ThrowsInvalidState()
    {
        var sut = new Mocks().BuildSut();

        var ex = Assert.Throws<SessionCommandException>(() => sut.ValidateStartSession(MakeStartPayload(externalPcId: "club12-pc08")));

        Assert.Equal(ErrorCode.InvalidState, ex.ErrorCode);
    }

    [Fact]
    public void ValidateStartSession_WhenExternalPcIdDiffersOnlyByCase_ThrowsInvalidState()
    {
        var sut = new Mocks().BuildSut();

        Assert.Throws<SessionCommandException>(() => sut.ValidateStartSession(MakeStartPayload(externalPcId: OwnPcId.ToUpperInvariant())));
    }

    [Fact]
    public void ValidateStartSession_WhenExternalPcIdEmpty_ThrowsInvalidState()
    {
        var sut = new Mocks().BuildSut();

        Assert.Throws<SessionCommandException>(() => sut.ValidateStartSession(MakeStartPayload(externalPcId: "")));
    }

    [Fact]
    public void ValidateStartSession_WhenGrantIdEmpty_ThrowsInvalidState()
    {
        var sut = new Mocks().BuildSut();

        Assert.Throws<SessionCommandException>(() => sut.ValidateStartSession(MakeStartPayload(grantId: " ")));
    }

    [Fact]
    public void ValidateStartSession_WhenGrantedSecondsZero_ThrowsInvalidState()
    {
        var sut = new Mocks().BuildSut();

        Assert.Throws<SessionCommandException>(() => sut.ValidateStartSession(MakeStartPayload(grantedSeconds: 0)));
    }

    [Fact]
    public void ValidateStartSession_WhenGrantedSecondsNegative_ThrowsInvalidState()
    {
        var sut = new Mocks().BuildSut();

        Assert.Throws<SessionCommandException>(() => sut.ValidateStartSession(MakeStartPayload(grantedSeconds: -1)));
    }

    [Fact]
    public void ValidateStartSession_WhenGrantedSecondsExceedsMax_ThrowsInvalidState()
    {
        var sut = new Mocks().BuildSut();

        Assert.Throws<SessionCommandException>(() =>
            sut.ValidateStartSession(MakeStartPayload(grantedSeconds: Constants.SessionCommand.MaxGrantedSeconds + 1)));
    }

    [Fact]
    public void ValidateStartSession_WhenGrantedSecondsAtMax_DoesNotThrow() =>
        new Mocks().BuildSut().ValidateStartSession(MakeStartPayload(grantedSeconds: Constants.SessionCommand.MaxGrantedSeconds));

    [Fact]
    public void ValidateStartSession_WhenZoneNull_DoesNotThrow() =>
        new Mocks().BuildSut().ValidateStartSession(MakeStartPayload(zone: null));

    [Fact]
    public void ValidateStartSession_WhenZoneRecognized_DoesNotThrow() =>
        new Mocks().BuildSut().ValidateStartSession(MakeStartPayload(zone: "Vip"));

    [Fact]
    public void ValidateStartSession_WhenZoneUnrecognized_ThrowsInvalidState()
    {
        var sut = new Mocks().BuildSut();

        var ex = Assert.Throws<SessionCommandException>(() => sut.ValidateStartSession(MakeStartPayload(zone: "Ultra")));

        Assert.Equal(ErrorCode.InvalidState, ex.ErrorCode);
    }

    [Fact]
    public void ValidateStartSession_WhenEndsAtAfterStartAt_DoesNotThrow() =>
        new Mocks().BuildSut().ValidateStartSession(MakeStartPayload(startAt: Now, endsAt: Now.AddMinutes(30)));

    [Fact]
    public void ValidateStartSession_WhenEndsAtBeforeStartAt_ThrowsInvalidState()
    {
        var sut = new Mocks().BuildSut();

        Assert.Throws<SessionCommandException>(() =>
            sut.ValidateStartSession(MakeStartPayload(startAt: Now, endsAt: Now.AddMinutes(-1))));
    }

    [Fact]
    public void ValidateStartSession_WhenEndsAtEqualsStartAt_ThrowsInvalidState()
    {
        var sut = new Mocks().BuildSut();

        Assert.Throws<SessionCommandException>(() => sut.ValidateStartSession(MakeStartPayload(startAt: Now, endsAt: Now)));
    }

    [Fact]
    public void ValidateStartSession_WhenStartAtNullAndEndsAtAfterNow_DoesNotThrow()
    {
        var m = new Mocks();
        var sut = m.BuildSut();
        var payload = new StartSessionPayload(OwnPcId, "grant_1", null, 3600, Now.AddMinutes(30), "Standard", StartAt: null);

        sut.ValidateStartSession(payload);
    }

    [Fact]
    public void ValidateStartSession_WhenStartAtNullAndEndsAtBeforeNow_ThrowsInvalidState()
    {
        var m = new Mocks();
        var sut = m.BuildSut();
        var payload = new StartSessionPayload(OwnPcId, "grant_1", null, 3600, Now.AddMinutes(-1), "Standard", StartAt: null);

        Assert.Throws<SessionCommandException>(() => sut.ValidateStartSession(payload));
    }

    // ── ValidateExtendSession ───────────────────────────────────────────

    [Fact]
    public void ValidateExtendSession_WhenGrantIdEmpty_ThrowsInvalidState()
    {
        var sut = new Mocks().BuildSut();
        var payload = new ExtendSessionPayload("cs_1", GrantId: "", PaymentOrderId: null, AddedSeconds: 600);

        Assert.Throws<SessionCommandException>(() => sut.ValidateExtendSession(payload));
    }

    [Fact]
    public void ValidateExtendSession_WhenAddedSecondsZero_ThrowsInvalidState()
    {
        var sut = new Mocks().BuildSut();
        var payload = new ExtendSessionPayload("cs_1", "grant_1", null, AddedSeconds: 0);

        Assert.Throws<SessionCommandException>(() => sut.ValidateExtendSession(payload));
    }

    [Fact]
    public void ValidateExtendSession_WhenAddedSecondsNegative_ThrowsInvalidState()
    {
        var sut = new Mocks().BuildSut();
        var payload = new ExtendSessionPayload("cs_1", "grant_1", null, AddedSeconds: -100);

        Assert.Throws<SessionCommandException>(() => sut.ValidateExtendSession(payload));
    }

    [Fact]
    public void ValidateExtendSession_WhenAddedSecondsExceedsMax_ThrowsInvalidState()
    {
        var sut = new Mocks().BuildSut();
        var payload = new ExtendSessionPayload("cs_1", "grant_1", null, Constants.SessionCommand.MaxAddedSeconds + 1);

        Assert.Throws<SessionCommandException>(() => sut.ValidateExtendSession(payload));
    }

    [Fact]
    public void ValidateExtendSession_WhenCoreSessionIdEmpty_ThrowsInvalidState()
    {
        var sut = new Mocks().BuildSut();
        var payload = new ExtendSessionPayload("", "grant_1", null, 600);

        Assert.Throws<SessionCommandException>(() => sut.ValidateExtendSession(payload));
    }

    [Fact]
    public void ValidateExtendSession_WhenCoreSessionIdMatchesCurrentSession_DoesNotThrow()
    {
        var m = new Mocks();
        var coreSessionId = Guid.NewGuid();
        m.Coordinator.SetupGet(c => c.CurrentSession).Returns(MakeSession(coreSessionId));
        var sut = m.BuildSut();
        var payload = new ExtendSessionPayload(coreSessionId.ToString("N"), "grant_1", null, 600);

        sut.ValidateExtendSession(payload);
    }

    [Fact]
    public void ValidateExtendSession_WhenCoreSessionIdMatchesCurrentSessionDifferentCase_DoesNotThrow()
    {
        var m = new Mocks();
        var coreSessionId = Guid.NewGuid();
        m.Coordinator.SetupGet(c => c.CurrentSession).Returns(MakeSession(coreSessionId));
        var sut = m.BuildSut();
        var payload = new ExtendSessionPayload(coreSessionId.ToString("N").ToUpperInvariant(), "grant_1", null, 600);

        sut.ValidateExtendSession(payload);
    }

    [Fact]
    public void ValidateExtendSession_WhenCoreSessionIdDoesNotMatchCurrentSession_ThrowsInvalidState()
    {
        var m = new Mocks();
        m.Coordinator.SetupGet(c => c.CurrentSession).Returns(MakeSession(Guid.NewGuid()));
        var sut = m.BuildSut();
        var payload = new ExtendSessionPayload(Guid.NewGuid().ToString("N"), "grant_1", null, 600);

        var ex = Assert.Throws<SessionCommandException>(() => sut.ValidateExtendSession(payload));

        Assert.Equal(ErrorCode.InvalidState, ex.ErrorCode);
    }

    [Fact]
    public void ValidateExtendSession_WhenNoCurrentSession_DoesNotThrow()
    {
        var m = new Mocks();
        m.Coordinator.SetupGet(c => c.CurrentSession).Returns((Session?)null);
        var sut = m.BuildSut();
        var payload = new ExtendSessionPayload(Guid.NewGuid().ToString("N"), "grant_1", null, 600);

        sut.ValidateExtendSession(payload);
    }

    // ── ValidateEndSession ──────────────────────────────────────────────

    [Fact]
    public void ValidateEndSession_WhenCoreSessionIdEmpty_ThrowsInvalidState()
    {
        var sut = new Mocks().BuildSut();
        var payload = new EndSessionPayload("", EndReason.Manager);

        Assert.Throws<SessionCommandException>(() => sut.ValidateEndSession(payload));
    }

    [Fact]
    public void ValidateEndSession_WhenCoreSessionIdMatchesCurrentSession_DoesNotThrow()
    {
        var m = new Mocks();
        var coreSessionId = Guid.NewGuid();
        m.Coordinator.SetupGet(c => c.CurrentSession).Returns(MakeSession(coreSessionId));
        var sut = m.BuildSut();
        var payload = new EndSessionPayload(coreSessionId.ToString("N"), EndReason.Manager);

        sut.ValidateEndSession(payload);
    }

    [Fact]
    public void ValidateEndSession_WhenCoreSessionIdDoesNotMatchCurrentSession_ThrowsInvalidState()
    {
        var m = new Mocks();
        m.Coordinator.SetupGet(c => c.CurrentSession).Returns(MakeSession(Guid.NewGuid()));
        var sut = m.BuildSut();
        var payload = new EndSessionPayload(Guid.NewGuid().ToString("N"), EndReason.Manager);

        Assert.Throws<SessionCommandException>(() => sut.ValidateEndSession(payload));
    }

    [Fact]
    public void ValidateEndSession_WhenNoCurrentSession_DoesNotThrow()
    {
        var m = new Mocks();
        m.Coordinator.SetupGet(c => c.CurrentSession).Returns((Session?)null);
        var sut = m.BuildSut();
        var payload = new EndSessionPayload(Guid.NewGuid().ToString("N"), EndReason.Manager);

        sut.ValidateEndSession(payload);
    }

    // ── ValidateLock / ValidateUnlock / ValidateSleep / ValidateSetRepair ──

    [Fact]
    public void ValidateLock_WhenExternalPcIdMatchesAgent_DoesNotThrow() =>
        new Mocks().BuildSut().ValidateLock(new LockPayload(OwnPcId, "manager"));

    [Fact]
    public void ValidateLock_WhenExternalPcIdDiffersFromAgent_ThrowsInvalidState()
    {
        var sut = new Mocks().BuildSut();

        Assert.Throws<SessionCommandException>(() => sut.ValidateLock(new LockPayload("club12-pc08", "manager")));
    }

    [Fact]
    public void ValidateUnlock_WhenExternalPcIdMatchesAgent_DoesNotThrow() =>
        new Mocks().BuildSut().ValidateUnlock(new UnlockPayload(OwnPcId, null));

    [Fact]
    public void ValidateUnlock_WhenExternalPcIdDiffersFromAgent_ThrowsInvalidState()
    {
        var sut = new Mocks().BuildSut();

        Assert.Throws<SessionCommandException>(() => sut.ValidateUnlock(new UnlockPayload("club12-pc08", null)));
    }

    [Fact]
    public void ValidateSleep_WhenExternalPcIdMatchesAgent_DoesNotThrow() =>
        new Mocks().BuildSut().ValidateSleep(new SleepPayload(OwnPcId));

    [Fact]
    public void ValidateSleep_WhenExternalPcIdDiffersFromAgent_ThrowsInvalidState()
    {
        var sut = new Mocks().BuildSut();

        Assert.Throws<SessionCommandException>(() => sut.ValidateSleep(new SleepPayload("club12-pc08")));
    }

    [Fact]
    public void ValidateSetRepair_WhenExternalPcIdMatchesAgent_DoesNotThrow() =>
        new Mocks().BuildSut().ValidateSetRepair(new SetRepairPayload(OwnPcId, true));

    [Fact]
    public void ValidateSetRepair_WhenExternalPcIdDiffersFromAgent_ThrowsInvalidState()
    {
        var sut = new Mocks().BuildSut();

        Assert.Throws<SessionCommandException>(() => sut.ValidateSetRepair(new SetRepairPayload("club12-pc08", true)));
    }
}
