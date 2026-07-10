namespace ClubPay.Agent.Core.Contracts.Payloads;

/// <summary>Contract §4.4 lock/unlock payloads — do not change session state.</summary>
public sealed record LockPayload(string ExternalPcId, string? Reason);
public sealed record UnlockPayload(string ExternalPcId, string? Reason);

/// <summary>Contract §4.5 wake/sleep payloads.</summary>
/// <remarks>
/// "wake" cannot be meaningfully handled by the agent itself: if the PC is truly asleep (S3), the agent
/// process is suspended and cannot receive this command at all. Real wake is Wake-on-LAN, sent by the
/// Controller directly at the network level. The agent-side handler is therefore a near no-op ack.
/// </remarks>
public sealed record WakePayload(string ExternalPcId);
public sealed record SleepPayload(string ExternalPcId);

/// <summary>Contract §4.6 set_repair payload.</summary>
public sealed record SetRepairPayload(string ExternalPcId, bool On);

/// <summary>Contract §4.7 get_status payload.</summary>
public sealed record GetStatusPayload(string ExternalPcId);
