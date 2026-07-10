using System.Text.Json.Serialization;

namespace ClubPay.Agent.Core.Contracts.Enums;

/// <summary>Contract §3 pc_state.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PcState>))]
public enum PcState
{
    [JsonStringEnumMemberName("OFFLINE")] Offline,
    [JsonStringEnumMemberName("FREE")] Free,
    [JsonStringEnumMemberName("OCCUPIED")] Occupied,
    [JsonStringEnumMemberName("FROZEN")] Frozen,
    [JsonStringEnumMemberName("SLEEPING")] Sleeping,
    [JsonStringEnumMemberName("REPAIR")] Repair,
    [JsonStringEnumMemberName("ATTENTION")] Attention,
}
