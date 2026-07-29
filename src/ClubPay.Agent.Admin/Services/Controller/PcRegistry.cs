using Microsoft.Extensions.Configuration;
using ClubPay.Agent.Core.Models;

namespace ClubPay.Agent.Admin.Services.Controller;

/// <summary>Static PC roster for the ControllerHub — MVP/pilot scope (ТЗ §9 "yakka kontroller"):
/// one club, one appsettings.json list. Superadmin-driven provisioning is V2+.</summary>
public sealed record PcRegistryEntry(string ExternalPcId, string PcId, ZoneType Zone, string AgentToken);

public interface IPcRegistry
{
    IReadOnlyList<PcRegistryEntry> All { get; }
    bool TryGet(string externalPcId, out PcRegistryEntry entry);
    bool ValidateToken(string externalPcId, string? token);
}

public sealed class PcRegistry : IPcRegistry
{
    private sealed class ConfigEntry
    {
        public string ExternalPcId { get; set; } = string.Empty;
        public string PcId { get; set; } = string.Empty;
        public string Zone { get; set; } = nameof(ZoneType.Standard);
        public string AgentToken { get; set; } = string.Empty;
    }

    private readonly Dictionary<string, PcRegistryEntry> _byExternalPcId;

    public PcRegistry(IConfiguration config)
    {
        var configEntries = config.GetSection("Controller:Pcs").Get<List<ConfigEntry>>() ?? [];
        _byExternalPcId = configEntries
            .Where(e => !string.IsNullOrWhiteSpace(e.ExternalPcId))
            .Select(e => new PcRegistryEntry(
                e.ExternalPcId,
                e.PcId,
                Enum.TryParse<ZoneType>(e.Zone, ignoreCase: true, out var zone) ? zone : ZoneType.Standard,
                e.AgentToken))
            .ToDictionary(e => e.ExternalPcId, e => e);
    }

    public IReadOnlyList<PcRegistryEntry> All => _byExternalPcId.Values.ToList();

    public bool TryGet(string externalPcId, out PcRegistryEntry entry) =>
        _byExternalPcId.TryGetValue(externalPcId, out entry!);

    public bool ValidateToken(string externalPcId, string? token) =>
        TryGet(externalPcId, out var entry) &&
        !string.IsNullOrEmpty(token) &&
        entry.AgentToken == token;
}
