namespace ClubPay.Agent.Core.Models;

public record Session(
    Guid Id,
    string PcId,
    Tariff Tariff,
    DateTime StartedAtUtc,
    int GrantedSeconds,
    Guid? CoreSessionId = null,
    string? GrantId = null,
    DateTime? EndsAtUtc = null,
    string? Zone = null
)
{
    // Wall-clock (UTC) diffing is the primary timing mechanism (contract: agent computes the
    // countdown locally, ends_at is only a cross-check). EndsAtUtc is persisted and re-checked on
    // every tick/resume-from-sleep so a backward-wound system clock can never extend a session past
    // what was actually granted. A monotonic clock (Stopwatch/TickCount64) was considered and
    // rejected for this phase: it does not survive a process restart, which persistence already has
    // to handle via EndsAtUtc regardless.
    public int ElapsedSeconds(DateTime nowUtc) => (int)(nowUtc - StartedAtUtc).TotalSeconds;
    public int RemainingSeconds(DateTime nowUtc) => Math.Max(0, GrantedSeconds - ElapsedSeconds(nowUtc));
    public bool IsExpired(DateTime nowUtc) => RemainingSeconds(nowUtc) == 0;

    public int TotalPlayedMinutes(DateTime nowUtc) => ElapsedSeconds(nowUtc) / 60;
}
