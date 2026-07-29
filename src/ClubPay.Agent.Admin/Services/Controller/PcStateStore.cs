using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ClubPay.Agent.Core;
using ClubPay.Agent.Core.Contracts;
using ClubPay.Agent.Core.Contracts.Enums;
using ClubPay.Agent.Core.Contracts.Events;
using ClubPay.Agent.Core.Contracts.Payloads;

namespace ClubPay.Agent.Admin.Services.Controller;

public sealed class PcStateStore : IPcStateStore
{
    private readonly ConcurrentDictionary<string, PcLiveState> _states = new();
    private readonly ILogger<PcStateStore> _logger;

    public event Action<string>? Changed;

    public PcStateStore(IPcRegistry registry, ILogger<PcStateStore> logger)
    {
        _logger = logger;
        foreach (var entry in registry.All)
        {
            _states[entry.ExternalPcId] = new PcLiveState
            {
                ExternalPcId = entry.ExternalPcId,
                PcId = entry.PcId,
                Zone = entry.Zone,
            };
        }
    }

    public IReadOnlyCollection<PcLiveState> All => _states.Values.ToList();

    public PcLiveState? Get(string externalPcId) =>
        _states.TryGetValue(externalPcId, out var state) ? state : null;

    public void MarkConnected(string externalPcId)
    {
        if (!_states.TryGetValue(externalPcId, out var state))
            return;

        state.IsConnected = true;
        if (state.PcState == PcState.Offline)
            state.PcState = PcState.Free;
        Changed?.Invoke(externalPcId);
    }

    public void MarkDisconnected(string externalPcId)
    {
        if (!_states.TryGetValue(externalPcId, out var state))
            return;

        state.IsConnected = false;
        state.PcState = PcState.Offline;
        Changed?.Invoke(externalPcId);
    }

    public void ApplyEvent(string externalPcId, EventEnvelope evt)
    {
        if (!_states.TryGetValue(externalPcId, out var state))
            return;

        try
        {
            switch (evt.Name)
            {
                case Constants.ControllerChannel.EventName.AgentOnline:
                    state.IsConnected = true;
                    if (state.PcState == PcState.Offline)
                        state.PcState = PcState.Free;
                    break;

                case Constants.ControllerChannel.EventName.PcStateChanged:
                    var pcStateChanged = Deserialize<PcStateChangedEvent>(evt.Payload);
                    state.PcState = pcStateChanged.PcState;
                    break;

                case Constants.ControllerChannel.EventName.SessionStarted:
                    var started = Deserialize<SessionStartedEvent>(evt.Payload);
                    state.CoreSessionId = started.CoreSessionId;
                    state.SessionState = SessionState.Active;
                    state.PcState = PcState.Occupied;
                    break;

                case Constants.ControllerChannel.EventName.SessionExtended:
                    var extended = Deserialize<SessionExtendedEvent>(evt.Payload);
                    state.CoreSessionId = extended.CoreSessionId;
                    state.SessionState = SessionState.Active;
                    state.PcState = PcState.Occupied;
                    break;

                case Constants.ControllerChannel.EventName.SessionEnded:
                    var ended = Deserialize<SessionEndedEvent>(evt.Payload);
                    state.SessionState = Core.Contracts.Enums.SessionState.Ended;
                    state.RemainingSeconds = ended.RemainingSeconds;
                    if (state.PcState is PcState.Occupied or PcState.Frozen)
                        state.PcState = PcState.Free;
                    break;

                case Constants.ControllerChannel.EventName.TimeLow:
                    var timeLow = Deserialize<TimeLowEvent>(evt.Payload);
                    state.RemainingSeconds = timeLow.RemainingSeconds;
                    break;

                case Constants.ControllerChannel.EventName.Heartbeat:
                    var heartbeat = Deserialize<HeartbeatEvent>(evt.Payload);
                    state.PcState = heartbeat.PcState;
                    break;

                case Constants.ControllerChannel.EventName.CommandFailed:
                    var failed = Deserialize<CommandFailedEvent>(evt.Payload);
                    _logger.LogWarning(
                        "command_failed: {ExternalPcId} {ErrorCode}", externalPcId, failed.ErrorCode);
                    break;

                case Constants.ControllerChannel.EventName.ManagerUnlock:
                    // audit-only — no live-state field for this yet, logged for now
                    _logger.LogInformation("manager_unlock: {ExternalPcId}", externalPcId);
                    break;

                default:
                    _logger.LogWarning("Noma'lum event: {Name} ({ExternalPcId})", evt.Name, externalPcId);
                    return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Event payload'ni qayta ishlashda xato: {Name} ({ExternalPcId})", evt.Name, externalPcId);
            return;
        }

        state.LastEventAtUtc = DateTime.UtcNow;
        Changed?.Invoke(externalPcId);
    }

    public void ApplyGetStatusResult(string externalPcId, GetStatusResult result)
    {
        if (!_states.TryGetValue(externalPcId, out var state))
            return;

        state.PcState = result.PcState;
        state.SessionState = result.SessionState;
        state.CoreSessionId = result.CoreSessionId;
        state.RemainingSeconds = result.RemainingSeconds;
        state.LastEventAtUtc = DateTime.UtcNow;
        Changed?.Invoke(externalPcId);
    }

    private static T Deserialize<T>(object payload)
    {
        if (payload is not JsonElement element)
            throw new InvalidOperationException($"unexpected payload type for {typeof(T).Name}: {payload.GetType()}");

        return element.Deserialize<T>(ControllerJsonOptions.Default)
            ?? throw new InvalidOperationException($"null payload for {typeof(T).Name}");
    }
}
