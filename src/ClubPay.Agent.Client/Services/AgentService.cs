using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ClubPay.Agent.Core;
using ClubPay.Agent.Core.Models;
using ClubPay.Agent.Core.Services;

namespace ClubPay.Agent.Client.Services;

public sealed class AgentService : IAgentService
{
    public string PcId { get; }
    public string ExternalPcId { get; }
    public string? ClubId { get; }
    public ZoneType Zone { get; }
    public string ClubName { get; }
    public string WifiSsid { get; }
    public string WifiPassword { get; }
    public string PaymentBaseUrl { get; }

    public AgentService(IConfiguration config, ILogger<AgentService> logger)
    {
        PcId = config["Agent:PcId"] ?? "PC-01";
        ClubId = config["Controller:ClubId"];
        ClubName = config["Agent:ClubName"] ?? "NEXUS ARENA";
        WifiSsid = config["Agent:WifiSsid"] ?? "ClubPay-Guest";
        WifiPassword = config["Agent:WifiPassword"] ?? string.Empty;
        Zone = Enum.TryParse<ZoneType>(config["Agent:Zone"], out var z) ? z : ZoneType.Standard;
        PaymentBaseUrl = config["Qr:PaymentBaseUrl"] ?? Constants.Qr.PaymentBaseUrl;

        var externalPcId = config["Controller:ExternalPcId"];
        if (string.IsNullOrWhiteSpace(externalPcId))
        {
            logger.LogWarning("Controller:ExternalPcId is not configured — falling back to lowercased PcId");
            externalPcId = PcId.ToLowerInvariant();
        }
        ExternalPcId = externalPcId;
    }

    public Task SleepAsync(CancellationToken ct = default)
    {
        NativeMethods.SetSuspendState(false, false, false);
        return Task.CompletedTask;
    }

    public void KeepAwake(bool keepAwake)
    {
        NativeMethods.SetThreadExecutionState(keepAwake
            ? NativeMethods.ExecutionState.Continuous | NativeMethods.ExecutionState.SystemRequired
            : NativeMethods.ExecutionState.Continuous);
    }
}

internal static class NativeMethods
{
    [Flags]
    internal enum ExecutionState : uint
    {
        Continuous = 0x80000000,
        SystemRequired = 0x00000001,
    }

    [System.Runtime.InteropServices.DllImport("powrprof.dll", SetLastError = true)]
    internal static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    internal static extern ExecutionState SetThreadExecutionState(ExecutionState esFlags);
}
