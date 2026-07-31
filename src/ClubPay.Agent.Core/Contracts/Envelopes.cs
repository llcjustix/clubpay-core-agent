using ClubPay.Agent.Core.Contracts.Enums;

namespace ClubPay.Agent.Core.Contracts;

/// <summary>Contract §4 wrapper: Controller → Agent. Payload is a raw JsonElement until the dispatcher
/// deserializes it into the concrete Payloads/* type for the given command name.</summary>
public sealed record CommandEnvelope(string Type, string Name, string CommandId, DateTime Ts, object? Payload);

/// <summary>Contract §6: always the response to a CommandEnvelope.</summary>
public sealed record CommandResultEnvelope(
    string Type,
    string CommandId,
    string Status,
    object? Payload = null,
    ErrorCode? ErrorCode = null,
    string? Message = null);

/// <summary>Contract §5 wrapper: Agent → Controller, async, no reply expected.</summary>
public sealed record EventEnvelope(string Type, string Name, string EventId, DateTime Ts, object Payload);

/// <summary>Contract §5 wrapper: Controller → Agent, sent for every received EventEnvelope (new or
/// duplicate) so the Agent can stop retrying/holding it in the durable outbox. No status field: an ack
/// means "received and deduped," not "applied without error" — PcStateStore.ApplyEvent already
/// catches+logs its own application failures without blocking the ack.</summary>
public sealed record EventAckEnvelope(string Type, string EventId);
