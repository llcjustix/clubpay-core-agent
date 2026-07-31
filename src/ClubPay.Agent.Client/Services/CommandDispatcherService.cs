using System.Text.Json;
using Microsoft.Extensions.Logging;
using ClubPay.Agent.Core;
using ClubPay.Agent.Core.Contracts;
using ClubPay.Agent.Core.Contracts.Enums;
using ClubPay.Agent.Core.Contracts.Events;
using ClubPay.Agent.Core.Contracts.Payloads;
using ClubPay.Agent.Core.Exceptions;
using ClubPay.Agent.Core.Services;

namespace ClubPay.Agent.Client.Services;

/// <summary>
/// Routes an incoming CommandEnvelope to ISessionCoordinator and always returns a well-formed
/// CommandResultEnvelope — the single place command names are matched, guaranteeing every command
/// gets answered even on an unknown name or an unexpected internal error. Wraps execution of the 7
/// state-mutating commands with command_id-keyed idempotency (contract): a repeated command_id with the
/// same payload replays the cached result instead of re-running the command; a repeated command_id with
/// a different name/payload returns ErrorCode.Conflict. grant_id idempotency (a separate, pre-existing
/// mechanism inside ISessionCoordinator for start_session/extend_session) is untouched by this.
/// </summary>
public sealed class CommandDispatcherService(
    ISessionCoordinator coordinator,
    ICommandValidator validator,
    IControllerChannel channel,
    IAgentService agent,
    ICommandIdempotencyStore resultStore,
    ILogger<CommandDispatcherService> logger) : ICommandDispatcher
{
    private static readonly HashSet<string> IdempotencyTrackedCommands = new(StringComparer.Ordinal)
    {
        Constants.ControllerChannel.CommandName.StartSession,
        Constants.ControllerChannel.CommandName.ExtendSession,
        Constants.ControllerChannel.CommandName.EndSession,
        Constants.ControllerChannel.CommandName.Lock,
        Constants.ControllerChannel.CommandName.Unlock,
        Constants.ControllerChannel.CommandName.Sleep,
        Constants.ControllerChannel.CommandName.SetRepair,
    };

    private readonly AsyncKeyedLock _commandLocks = new();

    public async Task<CommandResultEnvelope> DispatchAsync(CommandEnvelope command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.CommandId) || !IdempotencyTrackedCommands.Contains(command.Name))
            return await ExecuteAsync(command, ct);

        using var _ = await _commandLocks.AcquireAsync(command.CommandId, ct);

        var cached = await TryFindCachedResultAsync(command, ct);
        if (cached is not null)
            return cached;

        var result = await ExecuteAsync(command, ct);
        await TryRecordResultAsync(command, result, ct);
        return result;
    }

    private async Task<CommandResultEnvelope?> TryFindCachedResultAsync(CommandEnvelope command, CancellationToken ct)
    {
        StoredCommandResult? existing;
        try
        {
            existing = await resultStore.FindCommandResultAsync(command.CommandId, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "command_id={CommandId} idempotentlik yozuvini o'qib bo'lmadi — qayta bajariladi", command.CommandId);
            return null; // fail open: treat as a cache-miss rather than blocking the command
        }
        if (existing is null)
            return null;

        if (existing.CommandName == command.Name && PayloadEquals(existing.Payload, command.Payload))
            return existing.Result;

        await PublishCommandFailedAsync(command, ErrorCode.Conflict, ct);
        return Error(command, ErrorCode.Conflict, "command_id already used for a different command or payload");
    }

    private async Task TryRecordResultAsync(CommandEnvelope command, CommandResultEnvelope result, CancellationToken ct)
    {
        // Don't freeze an unexpected bug into a permanent cached reply — a retry should get a fresh
        // attempt, not the same internal_error forever.
        if (result.Status == "error" && result.ErrorCode == ErrorCode.InternalError)
            return;

        try
        {
            await resultStore.RecordCommandResultAsync(command.CommandId, command.Name, command.Payload, result, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "command_id={CommandId} natijasini saqlab bo'lmadi — keyingi urinish qayta bajariladi", command.CommandId);
        }
    }

    private static bool PayloadEquals(JsonElement stored, object? incoming) =>
        incoming is JsonElement element ? JsonElement.DeepEquals(stored, element) : stored.ValueKind == JsonValueKind.Null;

    private async Task<CommandResultEnvelope> ExecuteAsync(CommandEnvelope command, CancellationToken ct)
    {
        try
        {
            logger.LogInformation("Buyruq qabul qilindi: {Name} ({CommandId})", command.Name, command.CommandId);
            validator.ValidateCommandId(command.CommandId);

            return command.Name switch
            {
                Constants.ControllerChannel.CommandName.StartSession => await HandleStartAsync(command, ct),
                Constants.ControllerChannel.CommandName.ExtendSession => await HandleExtendAsync(command, ct),
                Constants.ControllerChannel.CommandName.EndSession => await HandleEndAsync(command, ct),
                Constants.ControllerChannel.CommandName.Lock => await HandleLockAsync(command, ct),
                Constants.ControllerChannel.CommandName.Unlock => await HandleUnlockAsync(command, ct),
                Constants.ControllerChannel.CommandName.Wake => await HandleWakeAsync(command, ct),
                Constants.ControllerChannel.CommandName.Sleep => await HandleSleepAsync(command, ct),
                Constants.ControllerChannel.CommandName.SetRepair => await HandleSetRepairAsync(command, ct),
                Constants.ControllerChannel.CommandName.GetStatus => HandleGetStatus(command),
                Constants.ControllerChannel.CommandName.ApplyConfig =>
                    Error(command, ErrorCode.InvalidState, "apply_config is not supported in this phase"),
                _ => Error(command, ErrorCode.InvalidState, $"unknown command '{command.Name}'"),
            };
        }
        catch (SessionCommandException ex)
        {
            await PublishCommandFailedAsync(command, ex.ErrorCode, ct);
            return Error(command, ex.ErrorCode, ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Command {Name} ({CommandId}) failed", command.Name, command.CommandId);
            await PublishCommandFailedAsync(command, ErrorCode.InternalError, ct);
            return Error(command, ErrorCode.InternalError, "internal error");
        }
    }

    private Task PublishCommandFailedAsync(CommandEnvelope command, ErrorCode errorCode, CancellationToken ct) =>
        channel.PublishEventAsync(Constants.ControllerChannel.EventName.CommandFailed,
            new CommandFailedEvent(command.CommandId, agent.ExternalPcId, errorCode), ct);

    private async Task<CommandResultEnvelope> HandleStartAsync(CommandEnvelope command, CancellationToken ct)
    {
        var payload = Deserialize<StartSessionPayload>(command);
        validator.ValidateStartSession(payload);
        if (string.IsNullOrWhiteSpace(payload.ExtendUrl))
            logger.LogError("Integratsiya xatosi: start_session buyrug'ida extend_url bo'sh keldi ({CommandId})", command.CommandId);

        var (result, isDuplicate) = await coordinator.StartSessionAsync(payload, ct);
        return isDuplicate ? Ok(command, result, ErrorCode.Duplicate) : Ok(command, result);
    }

    private async Task<CommandResultEnvelope> HandleExtendAsync(CommandEnvelope command, CancellationToken ct)
    {
        var payload = Deserialize<ExtendSessionPayload>(command);
        validator.ValidateExtendSession(payload);
        var (result, isDuplicate) = await coordinator.ExtendSessionAsync(payload, ct);
        return isDuplicate ? Ok(command, result, ErrorCode.Duplicate) : Ok(command, result);
    }

    private async Task<CommandResultEnvelope> HandleEndAsync(CommandEnvelope command, CancellationToken ct)
    {
        var payload = Deserialize<EndSessionPayload>(command);
        validator.ValidateEndSession(payload);
        var result = await coordinator.EndSessionAsync(payload, ct);
        return Ok(command, result);
    }

    private async Task<CommandResultEnvelope> HandleLockAsync(CommandEnvelope command, CancellationToken ct)
    {
        var payload = Deserialize<LockPayload>(command);
        validator.ValidateLock(payload);
        await coordinator.LockAsync(payload.Reason, ct);
        return Ok(command, new EmptyResult());
    }

    private async Task<CommandResultEnvelope> HandleUnlockAsync(CommandEnvelope command, CancellationToken ct)
    {
        var payload = Deserialize<UnlockPayload>(command);
        validator.ValidateUnlock(payload);
        await coordinator.UnlockAsync(payload.Reason, ct);
        return Ok(command, new EmptyResult());
    }

    private async Task<CommandResultEnvelope> HandleWakeAsync(CommandEnvelope command, CancellationToken ct)
    {
        var result = await coordinator.WakeAckAsync(ct);
        return Ok(command, result);
    }

    private async Task<CommandResultEnvelope> HandleSleepAsync(CommandEnvelope command, CancellationToken ct)
    {
        var payload = Deserialize<SleepPayload>(command);
        validator.ValidateSleep(payload);
        await coordinator.SleepAsync(ct);
        return Ok(command, new EmptyResult());
    }

    private async Task<CommandResultEnvelope> HandleSetRepairAsync(CommandEnvelope command, CancellationToken ct)
    {
        var payload = Deserialize<SetRepairPayload>(command);
        validator.ValidateSetRepair(payload);
        await coordinator.SetRepairModeAsync(payload.On, ct);
        return Ok(command, new EmptyResult());
    }

    private CommandResultEnvelope HandleGetStatus(CommandEnvelope command)
    {
        var result = coordinator.GetStatus();
        return Ok(command, result);
    }

    private static T Deserialize<T>(CommandEnvelope command)
    {
        if (command.Payload is not JsonElement element)
            throw new SessionCommandException(ErrorCode.InvalidState, "missing payload");

        T? payload;
        try
        {
            payload = element.Deserialize<T>(ControllerJsonOptions.Default);
        }
        catch (JsonException)
        {
            throw new SessionCommandException(ErrorCode.InvalidState, "invalid payload");
        }

        if (payload is null)
            throw new SessionCommandException(ErrorCode.InvalidState, "invalid payload");

        return payload;
    }

    private static CommandResultEnvelope Ok(CommandEnvelope command, object payload, ErrorCode? errorCode = null) =>
        new(Constants.ControllerChannel.MessageType.CommandResult, command.CommandId, "ok", payload, errorCode);

    private static CommandResultEnvelope Error(CommandEnvelope command, ErrorCode code, string message) =>
        new(Constants.ControllerChannel.MessageType.CommandResult, command.CommandId, "error", null, code, message);
}
