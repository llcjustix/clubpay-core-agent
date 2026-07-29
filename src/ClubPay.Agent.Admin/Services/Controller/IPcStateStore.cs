using ClubPay.Agent.Core.Contracts;

namespace ClubPay.Agent.Admin.Services.Controller;

/// <summary>Live per-PC state, seeded from <see cref="IPcRegistry"/> and kept up to date by the
/// ControllerHub as agents connect/disconnect and send events. The only thing AdminViewModel
/// needs to subscribe to for the PC grid — it never touches sockets or wire formats.</summary>
public interface IPcStateStore
{
    IReadOnlyCollection<PcLiveState> All { get; }
    PcLiveState? Get(string externalPcId);

    /// <summary>Raised whenever a PC's live state changes, with the affected external_pc_id.</summary>
    event Action<string>? Changed;

    void MarkConnected(string externalPcId);
    void MarkDisconnected(string externalPcId);
    void ApplyEvent(string externalPcId, EventEnvelope evt);
    void ApplyGetStatusResult(string externalPcId, Core.Contracts.Payloads.GetStatusResult result);
}
