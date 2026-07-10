namespace ClubPay.Agent.Core.Models;

/// <summary>Session-timer lifecycle state. Manager lock and repair mode are orthogonal flags, not part of this enum.</summary>
public enum AgentState
{
    Locked,
    Active,
    Frozen
}
