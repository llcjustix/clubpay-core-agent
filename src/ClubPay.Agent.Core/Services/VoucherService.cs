using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSec.Cryptography;
using ClubPay.Agent.Core.Contracts;
using ClubPay.Agent.Core.Contracts.Enums;
using ClubPay.Agent.Core.Contracts.Payloads;
using ClubPay.Agent.Core.Exceptions;
using ClubPay.Agent.Core.Models;

namespace ClubPay.Agent.Core.Services;

/// <summary>
/// ТЗ §6/10/13 offline voucher redemption. Token format is JWT-style compact serialization:
/// base64url(payload_json) + "." + base64url(signature), where the signature covers the ASCII bytes of
/// the base64url payload segment (never the raw JSON) — so verification never has to re-serialize JSON
/// and can't be broken by property-order/whitespace differences. Only the Ed25519 public key is ever
/// held here; the private key that signs vouchers lives on the Billing side.
/// </summary>
public sealed class VoucherService : IVoucherService
{
    private readonly ISessionCoordinator _coordinator;
    private readonly IAgentService _agent;
    private readonly ISystemClock _clock;
    private readonly ILogger<VoucherService> _logger;
    private readonly PublicKey? _publicKey;

    public VoucherService(
        IConfiguration config,
        ISessionCoordinator coordinator,
        IAgentService agent,
        ISystemClock clock,
        ILogger<VoucherService> logger)
    {
        _coordinator = coordinator;
        _agent = agent;
        _clock = clock;
        _logger = logger;
        _publicKey = TryLoadPublicKey(config["Voucher:PublicKeyBase64"], logger);
    }

    public async Task<VoucherRedemptionResult> RedeemAsync(string token, CancellationToken ct = default)
    {
        if (_publicKey is null)
            return VoucherRedemptionResult.Rejected(VoucherRejectionReason.ServiceUnavailable, "voucher public key not configured");

        var parts = token.Split('.');
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
            return VoucherRedemptionResult.Rejected(VoucherRejectionReason.MalformedToken, "malformed voucher token");

        byte[] payloadBytes, signatureBytes;
        try
        {
            payloadBytes = Base64UrlDecode(parts[0]);
            signatureBytes = Base64UrlDecode(parts[1]);
        }
        catch (FormatException)
        {
            return VoucherRedemptionResult.Rejected(VoucherRejectionReason.MalformedToken, "malformed voucher token");
        }

        if (!SignatureAlgorithm.Ed25519.Verify(_publicKey, Encoding.ASCII.GetBytes(parts[0]), signatureBytes))
            return VoucherRedemptionResult.Rejected(VoucherRejectionReason.InvalidSignature, "invalid voucher signature");

        VoucherPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<VoucherPayload>(payloadBytes, ControllerJsonOptions.Default);
        }
        catch (JsonException)
        {
            payload = null;
        }

        if (payload is null)
            return VoucherRedemptionResult.Rejected(VoucherRejectionReason.MalformedToken, "malformed voucher token");

        var now = _clock.UtcNow;
        if (now >= payload.ExpiresAtUtc)
            return VoucherRedemptionResult.Rejected(VoucherRejectionReason.Expired, "voucher expired");

        if (!IsBoundToThisAgent(payload, out var bindingRejection))
            return bindingRejection!;

        var startPayload = new StartSessionPayload(
            _agent.ExternalPcId, payload.VoucherId, PaymentOrderId: null,
            payload.Seconds, now.AddSeconds(payload.Seconds), Zone: null, StartAt: now);

        try
        {
            var (result, isDuplicate) = await _coordinator.StartSessionAsync(startPayload, ct);
            if (isDuplicate)
                return VoucherRedemptionResult.Rejected(VoucherRejectionReason.AlreadyUsed, "voucher already used");

            return VoucherRedemptionResult.Ok(result.CoreSessionId, result.RemainingSeconds);
        }
        catch (SessionCommandException ex)
        {
            _logger.LogWarning("Vaucher sessiyani boshlay olmadi: {ErrorCode} {Message}", ex.ErrorCode, ex.Message);
            return VoucherRedemptionResult.Rejected(VoucherRejectionReason.SessionRejected, ex.Message);
        }
    }

    private bool IsBoundToThisAgent(VoucherPayload payload, out VoucherRedemptionResult? rejection)
    {
        if (payload.ExternalPcId is { } pcId)
        {
            if (!string.Equals(pcId, _agent.ExternalPcId, StringComparison.Ordinal))
            {
                rejection = VoucherRedemptionResult.Rejected(VoucherRejectionReason.WrongPc, "voucher issued for another pc");
                return false;
            }
        }
        else if (payload.ClubId is { } clubId)
        {
            if (!string.Equals(clubId, _agent.ClubId, StringComparison.Ordinal))
            {
                rejection = VoucherRedemptionResult.Rejected(VoucherRejectionReason.WrongPc, "voucher issued for another club");
                return false;
            }
        }
        else
        {
            rejection = VoucherRedemptionResult.Rejected(VoucherRejectionReason.MalformedToken, "voucher has no pc/club binding");
            return false;
        }

        rejection = null;
        return true;
    }

    private static PublicKey? TryLoadPublicKey(string? base64, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            logger.LogWarning("Voucher:PublicKeyBase64 sozlanmagan — vaucherlar rad etiladi");
            return null;
        }

        try
        {
            var keyBytes = Convert.FromBase64String(base64);
            return PublicKey.Import(SignatureAlgorithm.Ed25519, keyBytes, KeyBlobFormat.RawPublicKey);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            logger.LogError(ex, "Voucher:PublicKeyBase64 import qilib bo'lmadi — vaucherlar rad etiladi");
            return null;
        }
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }
}
