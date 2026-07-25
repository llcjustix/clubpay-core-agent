using System.Text.Json;
using Microsoft.Extensions.Logging;
using ClubPay.Agent.Core.Contracts;
using ClubPay.Agent.Core.Contracts.Enums;
using ClubPay.Agent.Core.Models;

namespace ClubPay.Agent.Core.Services;

/// <summary>
/// Auto-detects the code type by peeking at the (unverified) payload segment of the compact token:
/// "voucher_id" → voucher, "code_id" → manager master code. The peek only routes — every security
/// decision (signature, expiry, binding, replay) stays inside the redeemer the code is routed to.
/// </summary>
public sealed class LockCodeService : ILockCodeService
{
    private readonly IVoucherService _vouchers;
    private readonly IManagerCodeService _managerCodes;
    private readonly ILogger<LockCodeService> _logger;

    public LockCodeService(IVoucherService vouchers, IManagerCodeService managerCodes, ILogger<LockCodeService> logger)
    {
        _vouchers = vouchers;
        _managerCodes = managerCodes;
        _logger = logger;
    }

    public async Task<LockCodeResult> SubmitAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            return LockCodeResult.Rejected(LockCodeKind.Unknown, LockCodeRejectionReason.InvalidCode);

        var kind = DetectKind(code);
        switch (kind)
        {
            case LockCodeKind.Voucher:
                {
                    var result = await _vouchers.RedeemAsync(code, ct);
                    if (result.Accepted)
                        return LockCodeResult.Ok(kind);

                    _logger.LogWarning("Vaucher rad etildi: {Reason} {Message}", result.RejectionReason, result.Message);
                    return LockCodeResult.Rejected(kind, Map(result.RejectionReason));
                }

            case LockCodeKind.ManagerCode:
                {
                    var result = await _managerCodes.RedeemAsync(code, ct);
                    if (result.Accepted)
                        return LockCodeResult.Ok(kind);

                    _logger.LogWarning("Menejer kodi rad etildi: {Reason} {Message}", result.RejectionReason, result.Message);
                    return LockCodeResult.Rejected(kind, result.RejectionReason ?? LockCodeRejectionReason.InvalidCode);
                }

            default:
                _logger.LogWarning("LockScreen'ga kiritilgan kod turi aniqlanmadi");
                return LockCodeResult.Rejected(LockCodeKind.Unknown, LockCodeRejectionReason.InvalidCode);
        }
    }

    private static LockCodeKind DetectKind(string code)
    {
        var dotIndex = code.IndexOf('.');
        if (dotIndex <= 0)
            return LockCodeKind.Unknown;

        try
        {
            var payloadBytes = Base64Url.Decode(code[..dotIndex]);
            using var doc = JsonDocument.Parse(payloadBytes);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return LockCodeKind.Unknown;
            if (doc.RootElement.TryGetProperty("voucher_id", out _))
                return LockCodeKind.Voucher;
            if (doc.RootElement.TryGetProperty("code_id", out _))
                return LockCodeKind.ManagerCode;
            return LockCodeKind.Unknown;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return LockCodeKind.Unknown;
        }
    }

    private static LockCodeRejectionReason Map(VoucherRejectionReason? reason) => reason switch
    {
        VoucherRejectionReason.Expired => LockCodeRejectionReason.Expired,
        VoucherRejectionReason.WrongPc => LockCodeRejectionReason.WrongPc,
        VoucherRejectionReason.AlreadyUsed => LockCodeRejectionReason.AlreadyUsed,
        VoucherRejectionReason.SessionRejected => LockCodeRejectionReason.PcBusy,
        VoucherRejectionReason.ServiceUnavailable => LockCodeRejectionReason.ServiceUnavailable,
        _ => LockCodeRejectionReason.InvalidCode,
    };
}
