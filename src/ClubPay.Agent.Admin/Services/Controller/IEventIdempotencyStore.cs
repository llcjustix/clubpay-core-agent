namespace ClubPay.Agent.Admin.Services.Controller;

/// <summary>In-memory event_id dedup for ControllerHubService. Admin has no persistent storage anywhere
/// (see PcStateStore) — this loses its history on process restart exactly like live PC state does today;
/// that's consistent with existing Admin architecture, not a new regression.</summary>
public interface IEventIdempotencyStore
{
    /// <summary>Atomically records event_id as processed if this is the first time it's been seen within
    /// the retention window; returns true only on that first call.</summary>
    bool TryMarkProcessed(string eventId);
}
