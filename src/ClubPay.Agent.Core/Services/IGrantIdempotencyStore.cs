namespace ClubPay.Agent.Core.Services;

/// <summary>Contract §7: Core stores applied grant_ids so a repeated start_session/extend_session with
/// the same grant_id is a no-op (status: ok, error_code: duplicate) rather than adding time twice.</summary>
public interface IGrantIdempotencyStore
{
    Task<bool> HasAppliedAsync(string grantId, CancellationToken ct = default);
    Task RecordAppliedAsync(string grantId, CancellationToken ct = default);
}
