namespace ClubPay.Agent.Core.Models;

public record Session(
    Guid Id,
    string PcId,
    Tariff Tariff,
    DateTime StartedAtUtc,
    int GrantedSeconds
)
{
    public int ElapsedSeconds(DateTime nowUtc) => (int)(nowUtc - StartedAtUtc).TotalSeconds;
    public int RemainingSeconds(DateTime nowUtc) => Math.Max(0, GrantedSeconds - ElapsedSeconds(nowUtc));
    public bool IsExpired(DateTime nowUtc) => RemainingSeconds(nowUtc) == 0;

    public int TotalPlayedMinutes(DateTime nowUtc) => ElapsedSeconds(nowUtc) / 60;
}
