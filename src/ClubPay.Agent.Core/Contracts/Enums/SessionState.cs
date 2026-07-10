using System.Text.Json.Serialization;

namespace ClubPay.Agent.Core.Contracts.Enums;

/// <summary>Contract §3 session_state.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SessionState>))]
public enum SessionState
{
    [JsonStringEnumMemberName("STARTING")] Starting,
    [JsonStringEnumMemberName("ACTIVE")] Active,
    [JsonStringEnumMemberName("FROZEN")] Frozen,
    [JsonStringEnumMemberName("ENDED")] Ended,
    [JsonStringEnumMemberName("FAILED")] Failed,
}
