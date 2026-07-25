using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NSec.Cryptography;
using ClubPay.Agent.Core.Contracts.Enums;
using ClubPay.Agent.Core.Contracts.Payloads;
using ClubPay.Agent.Core.Exceptions;
using ClubPay.Agent.Core.Models;
using ClubPay.Agent.Core.Services;

namespace ClubPay.Agent.Core.Tests.Services;

public class VoucherServiceTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private sealed class Mocks
    {
        public Mock<ISessionCoordinator> Coordinator { get; } = new();
        public Mock<IAgentService> Agent { get; } = new();
        public Mock<ISystemClock> Clock { get; } = new();
        public (Key SigningKey, string PublicKeyBase64) KeyPair { get; } = VoucherTestTokens.GenerateKeyPair();

        public Mocks()
        {
            Agent.SetupGet(a => a.ExternalPcId).Returns("club12-pc07");
            Agent.SetupGet(a => a.ClubId).Returns("club12");
            Clock.SetupGet(c => c.UtcNow).Returns(Now);
            Coordinator
                .Setup(c => c.StartSessionAsync(It.IsAny<StartSessionPayload>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((new StartSessionResult("cs_generated", 900), false));
        }

        public VoucherService BuildSut(string? publicKeyBase64OverrideOrOmit = "__default__")
        {
            var value = publicKeyBase64OverrideOrOmit == "__default__" ? KeyPair.PublicKeyBase64 : publicKeyBase64OverrideOrOmit;
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Voucher:PublicKeyBase64"] = value })
                .Build();

            return new VoucherService(config, Coordinator.Object, Agent.Object, Clock.Object, NullLogger<VoucherService>.Instance);
        }
    }

    private static VoucherPayload MakePayload(
        string voucherId = "v_1001", string? externalPcId = "club12-pc07", string? clubId = null,
        int seconds = 900, DateTime? expiresAtUtc = null) =>
        new(voucherId, externalPcId, clubId, seconds, expiresAtUtc ?? Now.AddMinutes(30));

    [Fact]
    public async Task RedeemAsync_WithValidSignatureAndPcBinding_StartsSessionAndReturnsAccepted()
    {
        var m = new Mocks();
        var sut = m.BuildSut();
        var token = VoucherTestTokens.BuildToken(m.KeyPair.SigningKey, MakePayload());

        var result = await sut.RedeemAsync(token);

        Assert.True(result.Accepted);
        Assert.Equal("cs_generated", result.CoreSessionId);
        Assert.Equal(900, result.RemainingSeconds);
        Assert.Null(result.RejectionReason);
    }

    [Fact]
    public async Task RedeemAsync_WithValidSignatureAndClubBinding_StartsSessionOnMatchingClub()
    {
        var m = new Mocks();
        var sut = m.BuildSut();
        var payload = MakePayload(externalPcId: null, clubId: "club12");
        var token = VoucherTestTokens.BuildToken(m.KeyPair.SigningKey, payload);

        var result = await sut.RedeemAsync(token);

        Assert.True(result.Accepted);
    }

    [Fact]
    public async Task RedeemAsync_OnAcceptance_PassesVoucherIdAsGrantIdAndSecondsToCoordinator()
    {
        var m = new Mocks();
        var sut = m.BuildSut();
        var payload = MakePayload(voucherId: "v_grant_check", seconds: 1234);
        var token = VoucherTestTokens.BuildToken(m.KeyPair.SigningKey, payload);

        await sut.RedeemAsync(token);

        m.Coordinator.Verify(c => c.StartSessionAsync(
            It.Is<StartSessionPayload>(p =>
                p.GrantId == "v_grant_check" &&
                p.ExternalPcId == "club12-pc07" &&
                p.GrantedSeconds == 1234 &&
                p.PaymentOrderId == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RedeemAsync_WithSignatureFromDifferentKeyPair_ReturnsInvalidSignature()
    {
        var m = new Mocks();
        var sut = m.BuildSut();
        var otherKeyPair = VoucherTestTokens.GenerateKeyPair();
        var token = VoucherTestTokens.BuildToken(otherKeyPair.SigningKey, MakePayload());

        var result = await sut.RedeemAsync(token);

        Assert.False(result.Accepted);
        Assert.Equal(VoucherRejectionReason.InvalidSignature, result.RejectionReason);
    }

    [Fact]
    public async Task RedeemAsync_WithTamperedPayloadAfterSigning_ReturnsInvalidSignature()
    {
        var m = new Mocks();
        var sut = m.BuildSut();
        var original = MakePayload(seconds: 900);
        var signature = VoucherTestTokens.Sign(m.KeyPair.SigningKey, VoucherTestTokens.EncodePayload(original));
        var tamperedPayloadB64 = VoucherTestTokens.EncodePayload(original with { Seconds = 999999 });
        var token = tamperedPayloadB64 + "." + signature;

        var result = await sut.RedeemAsync(token);

        Assert.False(result.Accepted);
        Assert.Equal(VoucherRejectionReason.InvalidSignature, result.RejectionReason);
    }

    [Theory]
    [InlineData("no-dot-at-all")]
    [InlineData(".missing-payload-segment")]
    [InlineData("missing-signature-segment.")]
    [InlineData("not$base64.also$not$base64")]
    public async Task RedeemAsync_WithMalformedTokenStructure_ReturnsMalformedToken(string token)
    {
        var m = new Mocks();
        var sut = m.BuildSut();

        var result = await sut.RedeemAsync(token);

        Assert.False(result.Accepted);
        Assert.Equal(VoucherRejectionReason.MalformedToken, result.RejectionReason);
    }

    [Fact]
    public async Task RedeemAsync_WhenExpired_ReturnsExpired()
    {
        var m = new Mocks();
        var sut = m.BuildSut();
        var payload = MakePayload(expiresAtUtc: Now.AddSeconds(-1));
        var token = VoucherTestTokens.BuildToken(m.KeyPair.SigningKey, payload);

        var result = await sut.RedeemAsync(token);

        Assert.False(result.Accepted);
        Assert.Equal(VoucherRejectionReason.Expired, result.RejectionReason);
    }

    [Fact]
    public async Task RedeemAsync_WhenExactlyAtExpiryBoundary_ReturnsExpired()
    {
        var m = new Mocks();
        var sut = m.BuildSut();
        var payload = MakePayload(expiresAtUtc: Now);
        var token = VoucherTestTokens.BuildToken(m.KeyPair.SigningKey, payload);

        var result = await sut.RedeemAsync(token);

        Assert.False(result.Accepted);
        Assert.Equal(VoucherRejectionReason.Expired, result.RejectionReason);
    }

    [Fact]
    public async Task RedeemAsync_ForDifferentExternalPcId_ReturnsWrongPc()
    {
        var m = new Mocks();
        var sut = m.BuildSut();
        var payload = MakePayload(externalPcId: "club12-pc99");
        var token = VoucherTestTokens.BuildToken(m.KeyPair.SigningKey, payload);

        var result = await sut.RedeemAsync(token);

        Assert.False(result.Accepted);
        Assert.Equal(VoucherRejectionReason.WrongPc, result.RejectionReason);
    }

    [Fact]
    public async Task RedeemAsync_ForDifferentClubId_ReturnsWrongPc()
    {
        var m = new Mocks();
        var sut = m.BuildSut();
        var payload = MakePayload(externalPcId: null, clubId: "some-other-club");
        var token = VoucherTestTokens.BuildToken(m.KeyPair.SigningKey, payload);

        var result = await sut.RedeemAsync(token);

        Assert.False(result.Accepted);
        Assert.Equal(VoucherRejectionReason.WrongPc, result.RejectionReason);
    }

    [Fact]
    public async Task RedeemAsync_WithNeitherPcNorClubBinding_ReturnsMalformedToken()
    {
        var m = new Mocks();
        var sut = m.BuildSut();
        var payload = MakePayload(externalPcId: null, clubId: null);
        var token = VoucherTestTokens.BuildToken(m.KeyPair.SigningKey, payload);

        var result = await sut.RedeemAsync(token);

        Assert.False(result.Accepted);
        Assert.Equal(VoucherRejectionReason.MalformedToken, result.RejectionReason);
    }

    [Fact]
    public async Task RedeemAsync_WhenCoordinatorReportsDuplicate_ReturnsAlreadyUsed()
    {
        var m = new Mocks();
        m.Coordinator
            .Setup(c => c.StartSessionAsync(It.IsAny<StartSessionPayload>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new StartSessionResult("cs_generated", 0), true));
        var sut = m.BuildSut();
        var token = VoucherTestTokens.BuildToken(m.KeyPair.SigningKey, MakePayload());

        var result = await sut.RedeemAsync(token);

        Assert.False(result.Accepted);
        Assert.Equal(VoucherRejectionReason.AlreadyUsed, result.RejectionReason);
    }

    [Fact]
    public async Task RedeemAsync_WhenCoordinatorThrowsPcBusy_ReturnsSessionRejected()
    {
        var m = new Mocks();
        m.Coordinator
            .Setup(c => c.StartSessionAsync(It.IsAny<StartSessionPayload>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SessionCommandException(ErrorCode.PcBusy, "pc already has an active session"));
        var sut = m.BuildSut();
        var token = VoucherTestTokens.BuildToken(m.KeyPair.SigningKey, MakePayload());

        var result = await sut.RedeemAsync(token);

        Assert.False(result.Accepted);
        Assert.Equal(VoucherRejectionReason.SessionRejected, result.RejectionReason);
    }

    [Fact]
    public async Task RedeemAsync_WhenPublicKeyNotConfigured_ReturnsServiceUnavailable()
    {
        var m = new Mocks();
        var sut = m.BuildSut(publicKeyBase64OverrideOrOmit: null);
        var token = VoucherTestTokens.BuildToken(m.KeyPair.SigningKey, MakePayload());

        var result = await sut.RedeemAsync(token);

        Assert.False(result.Accepted);
        Assert.Equal(VoucherRejectionReason.ServiceUnavailable, result.RejectionReason);
        m.Coordinator.Verify(c => c.StartSessionAsync(It.IsAny<StartSessionPayload>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
