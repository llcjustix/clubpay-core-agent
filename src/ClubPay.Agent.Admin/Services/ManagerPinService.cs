using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using ClubPay.Agent.Core.Services;

namespace ClubPay.Agent.Admin.Services;

/// <summary>Compares the entered PIN's SHA-256 hash against <c>Admin:ManagerPinHash</c> in config.
/// Placeholder until a real admin-identity/auth service exists — no hash configured means no PIN
/// can ever verify (fail closed, not fail open).</summary>
public sealed class ManagerPinService : IManagerPinService
{
    private readonly string? _configuredHash;

    public ManagerPinService(IConfiguration config)
    {
        _configuredHash = config["Admin:ManagerPinHash"];
    }

    public bool Verify(string pin)
    {
        if (string.IsNullOrWhiteSpace(_configuredHash) || string.IsNullOrEmpty(pin))
            return false;

        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(pin)));
        return string.Equals(hash, _configuredHash.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
