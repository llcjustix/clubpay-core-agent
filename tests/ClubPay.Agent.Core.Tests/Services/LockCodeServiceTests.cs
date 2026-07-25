using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ClubPay.Agent.Core.Contracts.Enums;
using ClubPay.Agent.Core.Models;
using ClubPay.Agent.Core.Services;

namespace ClubPay.Agent.Core.Tests.Services;

public class LockCodeServiceTests
{
    private static readonly DateTime Expiry = new(2026, 1, 1, 12, 30, 0, DateTimeKind.Utc);

    private readonly Mock<IVoucherService> _vouchers = new();
    private readonly Mock<IManagerCodeService> _managerCodes = new();

    private LockCodeService BuildSut() =>
        new(_vouchers.Object, _managerCodes.Object, NullLogger<LockCodeService>.Instance);

    private static string VoucherToken()
    {
        var keys = VoucherTestTokens.GenerateKeyPair();
        return VoucherTestTokens.BuildToken(keys.SigningKey, new VoucherPayload("v_1", "club12-pc07", null, 900, Expiry));
    }

    private static string ManagerToken()
    {
        var keys = ManagerCodeTestTokens.GenerateKeyPair();
        return ManagerCodeTestTokens.BuildToken(keys.SigningKey, new ManagerCodePayload("mc_1", "mgr_01", "club12-pc07", null, 3600, Expiry));
    }

    [Fact]
    public async Task SubmitAsync_VoucherToken_RoutesToVoucherService()
    {
        _vouchers
            .Setup(v => v.RedeemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(VoucherRedemptionResult.Ok("cs_1", 900));
        var sut = BuildSut();
        var token = VoucherToken();

        var result = await sut.SubmitAsync(token);

        Assert.True(result.Accepted);
        Assert.Equal(LockCodeKind.Voucher, result.Kind);
        _vouchers.Verify(v => v.RedeemAsync(token, It.IsAny<CancellationToken>()), Times.Once);
        _managerCodes.Verify(m => m.RedeemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SubmitAsync_ManagerToken_RoutesToManagerCodeService()
    {
        _managerCodes
            .Setup(m => m.RedeemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ManagerCodeResult.Ok());
        var sut = BuildSut();
        var token = ManagerToken();

        var result = await sut.SubmitAsync(token);

        Assert.True(result.Accepted);
        Assert.Equal(LockCodeKind.ManagerCode, result.Kind);
        _managerCodes.Verify(m => m.RedeemAsync(token, It.IsAny<CancellationToken>()), Times.Once);
        _vouchers.Verify(v => v.RedeemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(VoucherRejectionReason.MalformedToken, LockCodeRejectionReason.InvalidCode)]
    [InlineData(VoucherRejectionReason.InvalidSignature, LockCodeRejectionReason.InvalidCode)]
    [InlineData(VoucherRejectionReason.Expired, LockCodeRejectionReason.Expired)]
    [InlineData(VoucherRejectionReason.WrongPc, LockCodeRejectionReason.WrongPc)]
    [InlineData(VoucherRejectionReason.AlreadyUsed, LockCodeRejectionReason.AlreadyUsed)]
    [InlineData(VoucherRejectionReason.SessionRejected, LockCodeRejectionReason.PcBusy)]
    [InlineData(VoucherRejectionReason.ServiceUnavailable, LockCodeRejectionReason.ServiceUnavailable)]
    public async Task SubmitAsync_RejectedVoucher_MapsReasonToLockCodeReason(
        VoucherRejectionReason voucherReason, LockCodeRejectionReason expected)
    {
        _vouchers
            .Setup(v => v.RedeemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(VoucherRedemptionResult.Rejected(voucherReason, "rejected"));
        var sut = BuildSut();

        var result = await sut.SubmitAsync(VoucherToken());

        Assert.False(result.Accepted);
        Assert.Equal(expected, result.RejectionReason);
    }

    [Fact]
    public async Task SubmitAsync_RejectedManagerCode_PassesReasonThrough()
    {
        _managerCodes
            .Setup(m => m.RedeemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ManagerCodeResult.Rejected(LockCodeRejectionReason.AlreadyUsed, "already used"));
        var sut = BuildSut();

        var result = await sut.SubmitAsync(ManagerToken());

        Assert.False(result.Accepted);
        Assert.Equal(LockCodeRejectionReason.AlreadyUsed, result.RejectionReason);
    }

    [Theory]
    [InlineData("just-random-text")]
    [InlineData("not$base64.signature")]
    [InlineData(".no-payload")]
    public async Task SubmitAsync_Garbage_ReturnsInvalidCode(string code)
    {
        var sut = BuildSut();

        var result = await sut.SubmitAsync(code);

        Assert.False(result.Accepted);
        Assert.Equal(LockCodeKind.Unknown, result.Kind);
        Assert.Equal(LockCodeRejectionReason.InvalidCode, result.RejectionReason);
        _vouchers.Verify(v => v.RedeemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _managerCodes.Verify(m => m.RedeemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SubmitAsync_EmptyInput_ReturnsInvalidCode(string code)
    {
        var sut = BuildSut();

        var result = await sut.SubmitAsync(code);

        Assert.False(result.Accepted);
        Assert.Equal(LockCodeRejectionReason.InvalidCode, result.RejectionReason);
    }
}
