using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NSec.Cryptography;
using ClubPay.Agent.Core.Contracts.Enums;
using ClubPay.Agent.Core.Contracts.Events;
using ClubPay.Agent.Core.Contracts.Payloads;
using ClubPay.Agent.Core.Exceptions;
using ClubPay.Agent.Core.Models;
using ClubPay.Agent.Core.Services;

namespace ClubPay.Agent.Core.Tests.Services;

public class ManagerCodeServiceTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private sealed class Mocks
    {
        public Mock<ISessionCoordinator> Coordinator { get; } = new();
        public Mock<IAgentService> Agent { get; } = new();
        public Mock<ISystemClock> Clock { get; } = new();
        public Mock<IGrantIdempotencyStore> Idempotency { get; } = new();
        public Mock<IControllerOutbox> Outbox { get; } = new();
        public (Key SigningKey, string PublicKeyBase64) KeyPair { get; } = ManagerCodeTestTokens.GenerateKeyPair();

        public Mocks()
        {
            Agent.SetupGet(a => a.ExternalPcId).Returns("club12-pc07");
            Agent.SetupGet(a => a.ClubId).Returns("club12");
            Clock.SetupGet(c => c.UtcNow).Returns(Now);
            Coordinator.SetupGet(c => c.IsManagerLocked).Returns(false);
            Coordinator.SetupGet(c => c.State).Returns(AgentState.Locked);
            Coordinator
                .Setup(c => c.StartSessionAsync(It.IsAny<StartSessionPayload>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((new StartSessionResult("cs_generated", 900, "club12-pc07", "grant_1", Now, Now.AddSeconds(900)), false));
            Idempotency
                .Setup(i => i.HasAppliedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
        }

        public ManagerCodeService BuildSut(string? publicKeyBase64OverrideOrOmit = "__default__")
        {
            var value = publicKeyBase64OverrideOrOmit == "__default__" ? KeyPair.PublicKeyBase64 : publicKeyBase64OverrideOrOmit;
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["ManagerCode:PublicKeyBase64"] = value })
                .Build();

            return new ManagerCodeService(
                config, Coordinator.Object, Agent.Object, Clock.Object,
                Idempotency.Object, Outbox.Object, NullLogger<ManagerCodeService>.Instance);
        }
    }

    private static ManagerCodePayload MakePayload(
        string codeId = "mc_1001", string managerId = "mgr_01", string? externalPcId = "club12-pc07",
        string? clubId = null, int seconds = 3600, DateTime? expiresAtUtc = null) =>
        new(codeId, managerId, externalPcId, clubId, seconds, expiresAtUtc ?? Now.AddMinutes(10));

    [Fact]
    public async Task RedeemAsync_ValidTokenWhileIdleLocked_StartsSessionWithPrefixedGrantId()
    {
        var m = new Mocks();
        var sut = m.BuildSut();
        var token = ManagerCodeTestTokens.BuildToken(m.KeyPair.SigningKey, MakePayload(codeId: "mc_grant", seconds: 1800));

        var result = await sut.RedeemAsync(token);

        Assert.True(result.Accepted);
        m.Coordinator.Verify(c => c.StartSessionAsync(
            It.Is<StartSessionPayload>(p =>
                p.GrantId == "mgr:mc_grant" &&
                p.ExternalPcId == "club12-pc07" &&
                p.GrantedSeconds == 1800),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RedeemAsync_ValidTokenWhileManagerLocked_CallsUnlockAndRecordsCode()
    {
        var m = new Mocks();
        m.Coordinator.SetupGet(c => c.IsManagerLocked).Returns(true);
        var sut = m.BuildSut();
        var token = ManagerCodeTestTokens.BuildToken(m.KeyPair.SigningKey, MakePayload(codeId: "mc_unlock"));

        var result = await sut.RedeemAsync(token);

        Assert.True(result.Accepted);
        m.Coordinator.Verify(c => c.UnlockAsync("manager_code:mc_unlock", It.IsAny<CancellationToken>()), Times.Once);
        m.Idempotency.Verify(i => i.RecordAppliedAsync("mgr:mc_unlock", It.IsAny<CancellationToken>()), Times.Once);
        m.Coordinator.Verify(c => c.StartSessionAsync(It.IsAny<StartSessionPayload>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RedeemAsync_ReplayedCode_ReturnsAlreadyUsed()
    {
        var m = new Mocks();
        m.Idempotency
            .Setup(i => i.HasAppliedAsync("mgr:mc_1001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var sut = m.BuildSut();
        var token = ManagerCodeTestTokens.BuildToken(m.KeyPair.SigningKey, MakePayload());

        var result = await sut.RedeemAsync(token);

        Assert.False(result.Accepted);
        Assert.Equal(LockCodeRejectionReason.AlreadyUsed, result.RejectionReason);
        m.Coordinator.Verify(c => c.StartSessionAsync(It.IsAny<StartSessionPayload>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RedeemAsync_CoordinatorReportsDuplicateGrant_ReturnsAlreadyUsed()
    {
        var m = new Mocks();
        m.Coordinator
            .Setup(c => c.StartSessionAsync(It.IsAny<StartSessionPayload>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new StartSessionResult("cs_generated", 0, "club12-pc07", "grant_1", Now, Now), true));
        var sut = m.BuildSut();
        var token = ManagerCodeTestTokens.BuildToken(m.KeyPair.SigningKey, MakePayload());

        var result = await sut.RedeemAsync(token);

        Assert.False(result.Accepted);
        Assert.Equal(LockCodeRejectionReason.AlreadyUsed, result.RejectionReason);
    }

    [Fact]
    public async Task RedeemAsync_ExpiredToken_ReturnsExpired()
    {
        var m = new Mocks();
        var sut = m.BuildSut();
        var token = ManagerCodeTestTokens.BuildToken(m.KeyPair.SigningKey, MakePayload(expiresAtUtc: Now.AddSeconds(-1)));

        var result = await sut.RedeemAsync(token);

        Assert.False(result.Accepted);
        Assert.Equal(LockCodeRejectionReason.Expired, result.RejectionReason);
    }

    [Fact]
    public async Task RedeemAsync_TokenForOtherPc_ReturnsWrongPc()
    {
        var m = new Mocks();
        var sut = m.BuildSut();
        var token = ManagerCodeTestTokens.BuildToken(m.KeyPair.SigningKey, MakePayload(externalPcId: "club12-pc99"));

        var result = await sut.RedeemAsync(token);

        Assert.False(result.Accepted);
        Assert.Equal(LockCodeRejectionReason.WrongPc, result.RejectionReason);
    }

    [Fact]
    public async Task RedeemAsync_TokenForMatchingClub_IsAccepted()
    {
        var m = new Mocks();
        var sut = m.BuildSut();
        var token = ManagerCodeTestTokens.BuildToken(m.KeyPair.SigningKey, MakePayload(externalPcId: null, clubId: "club12"));

        var result = await sut.RedeemAsync(token);

        Assert.True(result.Accepted);
    }

    [Fact]
    public async Task RedeemAsync_TamperedSignature_ReturnsInvalidCode()
    {
        var m = new Mocks();
        var sut = m.BuildSut();
        var otherKeyPair = ManagerCodeTestTokens.GenerateKeyPair();
        var token = ManagerCodeTestTokens.BuildToken(otherKeyPair.SigningKey, MakePayload());

        var result = await sut.RedeemAsync(token);

        Assert.False(result.Accepted);
        Assert.Equal(LockCodeRejectionReason.InvalidCode, result.RejectionReason);
    }

    [Fact]
    public async Task RedeemAsync_NoPublicKeyConfigured_ReturnsServiceUnavailable()
    {
        var m = new Mocks();
        var sut = m.BuildSut(publicKeyBase64OverrideOrOmit: null);
        var token = ManagerCodeTestTokens.BuildToken(m.KeyPair.SigningKey, MakePayload());

        var result = await sut.RedeemAsync(token);

        Assert.False(result.Accepted);
        Assert.Equal(LockCodeRejectionReason.ServiceUnavailable, result.RejectionReason);
    }

    [Fact]
    public async Task RedeemAsync_ActiveSession_ReturnsPcBusy()
    {
        var m = new Mocks();
        m.Coordinator.SetupGet(c => c.State).Returns(AgentState.Active);
        var sut = m.BuildSut();
        var token = ManagerCodeTestTokens.BuildToken(m.KeyPair.SigningKey, MakePayload());

        var result = await sut.RedeemAsync(token);

        Assert.False(result.Accepted);
        Assert.Equal(LockCodeRejectionReason.PcBusy, result.RejectionReason);
    }

    [Fact]
    public async Task RedeemAsync_CoordinatorRejectsStart_ReturnsPcBusy()
    {
        var m = new Mocks();
        m.Coordinator
            .Setup(c => c.StartSessionAsync(It.IsAny<StartSessionPayload>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SessionCommandException(ErrorCode.PcInRepair, "pc is in repair mode"));
        var sut = m.BuildSut();
        var token = ManagerCodeTestTokens.BuildToken(m.KeyPair.SigningKey, MakePayload());

        var result = await sut.RedeemAsync(token);

        Assert.False(result.Accepted);
        Assert.Equal(LockCodeRejectionReason.PcBusy, result.RejectionReason);
    }

    [Fact]
    public async Task RedeemAsync_ZeroSecondsWhileIdleLocked_ReturnsInvalidCode()
    {
        var m = new Mocks();
        var sut = m.BuildSut();
        var token = ManagerCodeTestTokens.BuildToken(m.KeyPair.SigningKey, MakePayload(seconds: 0));

        var result = await sut.RedeemAsync(token);

        Assert.False(result.Accepted);
        Assert.Equal(LockCodeRejectionReason.InvalidCode, result.RejectionReason);
    }

    [Fact]
    public async Task RedeemAsync_Success_PublishesManagerUnlockAuditEvent()
    {
        var m = new Mocks();
        var sut = m.BuildSut();
        var token = ManagerCodeTestTokens.BuildToken(m.KeyPair.SigningKey, MakePayload(codeId: "mc_audit", managerId: "mgr_77"));

        var result = await sut.RedeemAsync(token);

        Assert.True(result.Accepted);
        m.Outbox.Verify(o => o.PublishEventAsync(
            "manager_unlock",
            It.Is<ManagerUnlockEvent>(e =>
                e.ExternalPcId == "club12-pc07" &&
                e.CodeId == "mc_audit" &&
                e.ManagerId == "mgr_77" &&
                e.Action == "start_session" &&
                e.AtUtc == Now),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RedeemAsync_AuditPublishFails_StillReturnsAccepted()
    {
        var m = new Mocks();
        m.Outbox
            .Setup(o => o.PublishEventAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("outbox full"));
        var sut = m.BuildSut();
        var token = ManagerCodeTestTokens.BuildToken(m.KeyPair.SigningKey, MakePayload());

        var result = await sut.RedeemAsync(token);

        Assert.True(result.Accepted);
    }
}
