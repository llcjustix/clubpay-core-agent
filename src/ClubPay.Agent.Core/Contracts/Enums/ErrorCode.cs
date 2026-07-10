using System.Text.Json.Serialization;

namespace ClubPay.Agent.Core.Contracts.Enums;

/// <summary>Contract §3 error_code (unified list).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ErrorCode>))]
public enum ErrorCode
{
    [JsonStringEnumMemberName("agent_offline")] AgentOffline,
    [JsonStringEnumMemberName("pc_busy")] PcBusy,
    [JsonStringEnumMemberName("pc_locked_error")] PcLockedError,
    [JsonStringEnumMemberName("command_timeout")] CommandTimeout,
    [JsonStringEnumMemberName("invalid_state")] InvalidState,
    [JsonStringEnumMemberName("internal_error")] InternalError,
    [JsonStringEnumMemberName("pc_in_repair")] PcInRepair,
    [JsonStringEnumMemberName("duplicate")] Duplicate,
    [JsonStringEnumMemberName("wol_failed")] WolFailed,
}
