using System.Text.Json;
using Microsoft.Extensions.Logging;
using ClubPay.Agent.Core;
using ClubPay.Agent.Core.Contracts;
using ClubPay.Agent.Core.Contracts.Enums;
using ClubPay.Agent.Core.Contracts.Payloads;
using ClubPay.Agent.Core.Exceptions;
using ClubPay.Agent.Core.Services;

namespace ClubPay.Agent.Client.Services;

/// <summary>
/// Routes an incoming CommandEnvelope to ISessionCoordinator and always returns a well-formed
/// CommandResultEnvelope — the single place command names are matched, guaranteeing every command
/// gets answered even on an unknown name or an unexpected internal error.
/// </summary>
public sealed class CommandDispatcherService(
    ISessionCoordinator coordinator,
    ILogger<CommandDispatcherService> logger) : ICommandDispatcher
{
    public async Task<CommandResultEnvelope> DispatchAsync(CommandEnvelope command, CancellationToken ct = default)
    {
        try
        {
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
            return Error(command, ex.ErrorCode, ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Command {Name} ({CommandId}) failed", command.Name, command.CommandId);
            return Error(command, ErrorCode.InternalError, "internal error");
        }
    }

    private async Task<CommandResultEnvelope> HandleStartAsync(CommandEnvelope command, CancellationToken ct)
    {
        var payload = Deserialize<StartSessionPayload>(command);
        var (result, isDuplicate) = await coordinator.StartSessionAsync(payload, ct);
        return isDuplicate ? Ok(command, result, ErrorCode.Duplicate) : Ok(command, result);
    }

    private async Task<CommandResultEnvelope> HandleExtendAsync(CommandEnvelope command, CancellationToken ct)
    {
        var payload = Deserialize<ExtendSessionPayload>(command);
        var (result, isDuplicate) = await coordinator.ExtendSessionAsync(payload, ct);
        return isDuplicate ? Ok(command, result, ErrorCode.Duplicate) : Ok(command, result);
    }

    private async Task<CommandResultEnvelope> HandleEndAsync(CommandEnvelope command, CancellationToken ct)
    {
        var payload = Deserialize<EndSessionPayload>(command);
        var result = await coordinator.EndSessionAsync(payload, ct);
        return Ok(command, result);
    }

    private async Task<CommandResultEnvelope> HandleLockAsync(CommandEnvelope command, CancellationToken ct)
    {
        var payload = Deserialize<LockPayload>(command);
        await coordinator.LockAsync(payload.Reason, ct);
        return Ok(command, new EmptyResult());
    }

    private async Task<CommandResultEnvelope> HandleUnlockAsync(CommandEnvelope command, CancellationToken ct)
    {
        var payload = Deserialize<UnlockPayload>(command);
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
        await coordinator.SleepAsync(ct);
        return Ok(command, new EmptyResult());
    }

    private async Task<CommandResultEnvelope> HandleSetRepairAsync(CommandEnvelope command, CancellationToken ct)
    {
        var payload = Deserialize<SetRepairPayload>(command);
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

        var payload = element.Deserialize<T>(ControllerJsonOptions.Default);
        if (payload is null)
            throw new SessionCommandException(ErrorCode.InvalidState, "invalid payload");

        return payload;
    }

    private static CommandResultEnvelope Ok(CommandEnvelope command, object payload, ErrorCode? errorCode = null) =>
        new(Constants.ControllerChannel.MessageType.CommandResult, command.CommandId, "ok", payload, errorCode);

    private static CommandResultEnvelope Error(CommandEnvelope command, ErrorCode code, string message) =>
        new(Constants.ControllerChannel.MessageType.CommandResult, command.CommandId, "error", null, code, message);
}
