using ClubPay.Agent.Core.Contracts.Enums;
using ClubPay.Agent.Core.Models;

namespace ClubPay.Agent.Core.Contracts;

/// <summary>Pure mapping from internal session/flag state to the contract's wire-level pc_state —
/// keeps the enum-explosion (Locked/Active/Frozen × manager-lock × repair × asleep × connected)
/// out of the internal model.</summary>
public static class PcStateMapper
{
    public static PcState ToWireState(
        AgentState state,
        bool isManagerLocked,
        bool isRepairMode,
        bool isAsleep,
        bool isConnected)
    {
        if (isRepairMode)
            return PcState.Repair;

        if (!isConnected)
            return PcState.Offline;

        if (isAsleep)
            return PcState.Sleeping;

        // Manager lock overlays Locked (idle/free) PCs as "needs attention"; it does not
        // override an in-progress session's own state (session timer keeps running underneath).
        if (isManagerLocked && state == AgentState.Locked)
            return PcState.Attention;

        return state switch
        {
            AgentState.Active => PcState.Occupied,
            AgentState.Frozen => PcState.Frozen,
            _ => PcState.Free,
        };
    }
}
