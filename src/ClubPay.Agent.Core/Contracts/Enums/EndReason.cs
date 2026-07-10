using System.Text.Json.Serialization;

namespace ClubPay.Agent.Core.Contracts.Enums;

/// <summary>Contract §3 end_reason.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EndReason>))]
public enum EndReason
{
    [JsonStringEnumMemberName("TIME_UP")] TimeUp,
    [JsonStringEnumMemberName("MANAGER")] Manager,
    [JsonStringEnumMemberName("REFUND")] Refund,
    [JsonStringEnumMemberName("CLIENT_LEFT")] ClientLeft,
    [JsonStringEnumMemberName("ERROR")] Error,
}
