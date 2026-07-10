using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClubPay.Agent.Core.Contracts;

/// <summary>Single shared wire format for all Controller traffic (commands, command_results, events).</summary>
public static class ControllerJsonOptions
{
    // No DictionaryKeyPolicy: dictionary keys in this codebase are opaque business identifiers
    // (grant_id values, etc.), not schema property names — running them through a naming policy
    // would silently corrupt them.
    public static JsonSerializerOptions Default { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
