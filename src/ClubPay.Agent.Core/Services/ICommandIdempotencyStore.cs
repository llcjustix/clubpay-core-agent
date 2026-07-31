using System.Text.Json;
using ClubPay.Agent.Core.Contracts;

namespace ClubPay.Agent.Core.Services;

/// <summary>
/// Persists the final CommandResultEnvelope for every tracked command, keyed by command_id, so a
/// repeated command_id short-circuits to the prior result instead of re-running the command — including
/// across an agent restart. Distinct from IGrantIdempotencyStore, which dedups by the Controller's own
/// grant_id concept for start_session/extend_session specifically; both run side by side.
/// </summary>
public interface ICommandIdempotencyStore
{
    Task<StoredCommandResult?> FindCommandResultAsync(string commandId, CancellationToken ct = default);

    /// <summary>No-ops if command_id already has a stored row.</summary>
    Task RecordCommandResultAsync(string commandId, string commandName, object? payload,
        CommandResultEnvelope result, CancellationToken ct = default);
}

/// <param name="Payload">Original payload exactly as received, round-tripped as a JsonElement — compare
/// with JsonElement.DeepEquals (not string/hash equality) to detect a command_id reused with a different
/// payload while ignoring immaterial whitespace/property-order differences.</param>
public sealed record StoredCommandResult(string CommandName, JsonElement Payload, CommandResultEnvelope Result);
