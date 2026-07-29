using ClubPay.Agent.Core.Contracts.Enums;
using ClubPay.Agent.Core.Models;

namespace ClubPay.Agent.Admin.Services.Controller;

/// <summary>Wire-level live state for one registered PC, as observed by the ControllerHub —
/// the source AdminViewModel's PcCard grid renders from.</summary>
public sealed class PcLiveState
{
    public required string ExternalPcId { get; init; }
    public required string PcId { get; init; }
    public required ZoneType Zone { get; init; }

    public bool IsConnected { get; set; }
    public PcState PcState { get; set; } = PcState.Offline;
    public SessionState? SessionState { get; set; }
    public string? CoreSessionId { get; set; }
    public int? RemainingSeconds { get; set; }
    public DateTime? LastEventAtUtc { get; set; }
}
