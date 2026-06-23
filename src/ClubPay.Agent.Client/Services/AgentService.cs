using Microsoft.Extensions.Configuration;
using ClubPay.Agent.Core.Models;
using ClubPay.Agent.Core.Services;

namespace ClubPay.Agent.Client.Services;

public sealed class AgentService : IAgentService
{
    public string PcId { get; }
    public ZoneType Zone { get; }
    public string ClubName { get; }
    public string WifiSsid { get; }

    private readonly ISessionStore _store;

    public AgentService(ISessionStore store, IConfiguration config)
    {
        _store = store;
        PcId = config["Agent:PcId"] ?? "PC-01";
        ClubName = config["Agent:ClubName"] ?? "NEXUS ARENA";
        WifiSsid = config["Agent:WifiSsid"] ?? "ClubPay-Guest";
        Zone = Enum.TryParse<ZoneType>(config["Agent:Zone"], out var z) ? z : ZoneType.Standard;
    }

    public Task<bool> StartSessionAsync(Session session, CancellationToken ct = default)
    {
        _store.Save(session);
        return Task.FromResult(true);
    }

    public Task EndSessionAsync(CancellationToken ct = default)
    {
        _store.Clear();
        return Task.CompletedTask;
    }

    public Task ExtendSessionAsync(int additionalSeconds, CancellationToken ct = default)
    {
        if (_store.Current is { } s)
            _store.Save(s with { GrantedSeconds = s.GrantedSeconds + additionalSeconds });
        return Task.CompletedTask;
    }

    public Task SleepAsync(CancellationToken ct = default)
    {
        // Trigger S3 sleep via Win32 SetSuspendState
        NativeMethods.SetSuspendState(false, false, false);
        return Task.CompletedTask;
    }
}

internal static class NativeMethods
{
    [System.Runtime.InteropServices.DllImport("powrprof.dll", SetLastError = true)]
    internal static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);
}
