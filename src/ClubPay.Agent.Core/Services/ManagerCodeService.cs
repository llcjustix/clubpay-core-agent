using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSec.Cryptography;
using ClubPay.Agent.Core.Contracts;
using ClubPay.Agent.Core.Contracts.Enums;
using ClubPay.Agent.Core.Contracts.Events;
using ClubPay.Agent.Core.Contracts.Payloads;
using ClubPay.Agent.Core.Exceptions;
using ClubPay.Agent.Core.Models;

namespace ClubPay.Agent.Core.Services;

/// <summary>
/// ТЗ §7/§10 manager master-code redemption. Same compact-token mechanics as
/// <see cref="VoucherService"/>: base64url(payload_json) + "." + base64url(signature), signature over the
/// ASCII bytes of the base64url payload segment, Ed25519 public key only. Unlike a voucher (which always
/// starts a session and gets its replay protection from the coordinator's grant idempotency), a manager
/// code can also clear a manager lock — that path records the code in the grant store itself. Every
/// acceptance is published to the controller outbox as a manager_unlock audit event (ТЗ §11 anti-theft:
/// who/when/which PC), which the outbox persists and delivers even if the agent is offline right now.
/// </summary>
public sealed class ManagerCodeService : IManagerCodeService
{
    private const string GrantKeyPrefix = "mgr:";

    private readonly ISessionCoordinator _coordinator;
    private readonly IAgentService _agent;
    private readonly ISystemClock _clock;
    private readonly IGrantIdempotencyStore _idempotency;
    private readonly IControllerOutbox _outbox;
    private readonly ILogger<ManagerCodeService> _logger;
    private readonly PublicKey? _publicKey;

    public ManagerCodeService(
        IConfiguration config,
        ISessionCoordinator coordinator,
        IAgentService agent,
        ISystemClock clock,
        IGrantIdempotencyStore idempotency,
        IControllerOutbox outbox,
        ILogger<ManagerCodeService> logger)
    {
        _coordinator = coordinator;
        _agent = agent;
        _clock = clock;
        _idempotency = idempotency;
        _outbox = outbox;
        _logger = logger;
        _publicKey = TryLoadPublicKey(config["ManagerCode:PublicKeyBase64"], logger);
    }

    public async Task<ManagerCodeResult> RedeemAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return ManagerCodeResult.Rejected(LockCodeRejectionReason.InvalidCode, "empty manager code");

        if (_publicKey is null)
            return ManagerCodeResult.Rejected(LockCodeRejectionReason.ServiceUnavailable, "manager code public key not configured");

        var parts = token.Split('.');
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
            return ManagerCodeResult.Rejected(LockCodeRejectionReason.InvalidCode, "malformed manager code token");

        byte[] payloadBytes, signatureBytes;
        try
        {
            payloadBytes = Base64Url.Decode(parts[0]);
            signatureBytes = Base64Url.Decode(parts[1]);
        }
        catch (FormatException)
        {
            return ManagerCodeResult.Rejected(LockCodeRejectionReason.InvalidCode, "malformed manager code token");
        }

        if (!SignatureAlgorithm.Ed25519.Verify(_publicKey, Encoding.ASCII.GetBytes(parts[0]), signatureBytes))
            return ManagerCodeResult.Rejected(LockCodeRejectionReason.InvalidCode, "invalid manager code signature");

        ManagerCodePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<ManagerCodePayload>(payloadBytes, ControllerJsonOptions.Default);
        }
        catch (JsonException)
        {
            payload = null;
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.CodeId) || string.IsNullOrWhiteSpace(payload.ManagerId))
            return ManagerCodeResult.Rejected(LockCodeRejectionReason.InvalidCode, "malformed manager code payload");

        var now = _clock.UtcNow;
        if (now >= payload.ExpiresAtUtc)
            return ManagerCodeResult.Rejected(LockCodeRejectionReason.Expired, "manager code expired");

        if (!IsBoundToThisAgent(payload, out var bindingRejection))
            return bindingRejection!;

        var grantKey = GrantKeyPrefix + payload.CodeId;
        if (await _idempotency.HasAppliedAsync(grantKey, ct))
            return ManagerCodeResult.Rejected(LockCodeRejectionReason.AlreadyUsed, "manager code already used");

        if (_coordinator.IsManagerLocked)
            return await UnlockManagerLockAsync(payload, grantKey, ct);

        if (_coordinator.State == AgentState.Locked)
            return await StartSessionAsync(payload, grantKey, now, ct);

        return ManagerCodeResult.Rejected(LockCodeRejectionReason.PcBusy, "pc has an active or frozen session");
    }

    private async Task<ManagerCodeResult> UnlockManagerLockAsync(ManagerCodePayload payload, string grantKey, CancellationToken ct)
    {
        await _coordinator.UnlockAsync($"manager_code:{payload.CodeId}", ct);
        await _idempotency.RecordAppliedAsync(grantKey, ct);
        await PublishAuditEventAsync(payload, action: "unlock", ct);
        return ManagerCodeResult.Ok();
    }

    private async Task<ManagerCodeResult> StartSessionAsync(ManagerCodePayload payload, string grantKey, DateTime now, CancellationToken ct)
    {
        if (payload.Seconds <= 0)
            return ManagerCodeResult.Rejected(LockCodeRejectionReason.InvalidCode, "manager code grants no session time");

        var startPayload = new StartSessionPayload(
            _agent.ExternalPcId, grantKey, PaymentOrderId: null,
            payload.Seconds, now.AddSeconds(payload.Seconds), Zone: null, StartAt: now);

        try
        {
            var (_, isDuplicate) = await _coordinator.StartSessionAsync(startPayload, ct);
            if (isDuplicate)
                return ManagerCodeResult.Rejected(LockCodeRejectionReason.AlreadyUsed, "manager code already used");
        }
        catch (SessionCommandException ex)
        {
            _logger.LogWarning("Menejer kodi sessiyani boshlay olmadi: {ErrorCode} {Message}", ex.ErrorCode, ex.Message);
            return ManagerCodeResult.Rejected(LockCodeRejectionReason.PcBusy, ex.Message);
        }

        await PublishAuditEventAsync(payload, action: "start_session", ct);
        return ManagerCodeResult.Ok();
    }

    private async Task PublishAuditEventAsync(ManagerCodePayload payload, string action, CancellationToken ct)
    {
        try
        {
            var evt = new ManagerUnlockEvent(_agent.ExternalPcId, payload.CodeId, payload.ManagerId, action, _clock.UtcNow);
            await _outbox.PublishEventAsync(Constants.ControllerChannel.EventName.ManagerUnlock, evt, ct);
        }
        catch (Exception ex)
        {
            // The unlock itself already happened — an audit-publish failure must not undo it or surface
            // to the person at the keyboard; the error stays in the log for the owner.
            _logger.LogError(ex, "manager_unlock audit-hodisasini outbox'ga yozib bo'lmadi");
        }
    }

    private bool IsBoundToThisAgent(ManagerCodePayload payload, out ManagerCodeResult? rejection)
    {
        if (payload.ExternalPcId is { } pcId)
        {
            if (!string.Equals(pcId, _agent.ExternalPcId, StringComparison.Ordinal))
            {
                rejection = ManagerCodeResult.Rejected(LockCodeRejectionReason.WrongPc, "manager code issued for another pc");
                return false;
            }
        }
        else if (payload.ClubId is { } clubId)
        {
            if (!string.Equals(clubId, _agent.ClubId, StringComparison.Ordinal))
            {
                rejection = ManagerCodeResult.Rejected(LockCodeRejectionReason.WrongPc, "manager code issued for another club");
                return false;
            }
        }
        else
        {
            rejection = ManagerCodeResult.Rejected(LockCodeRejectionReason.InvalidCode, "manager code has no pc/club binding");
            return false;
        }

        rejection = null;
        return true;
    }

    private static PublicKey? TryLoadPublicKey(string? base64, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            logger.LogWarning("ManagerCode:PublicKeyBase64 sozlanmagan — menejer kodlari rad etiladi");
            return null;
        }

        try
        {
            var keyBytes = Convert.FromBase64String(base64);
            return PublicKey.Import(SignatureAlgorithm.Ed25519, keyBytes, KeyBlobFormat.RawPublicKey);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            logger.LogError(ex, "ManagerCode:PublicKeyBase64 import qilib bo'lmadi — menejer kodlari rad etiladi");
            return null;
        }
    }
}
