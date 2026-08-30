namespace ClubPay.Agent.Client.Services;

/// <summary>
/// Expands the small set of identity placeholders supported by a shared
/// diskless Windows image. Each client still has a unique Windows hostname,
/// so it can safely share the executable while keeping a separate ClubPay
/// identity and state directory.
/// </summary>
internal static class MachineNameTemplate
{
    public static string? Expand(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        var machineName = Environment.MachineName.Trim();
        return value
            .Replace("{MACHINE_NAME_LOWER}", machineName.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase)
            .Replace("{MACHINE_NAME_UPPER}", machineName.ToUpperInvariant(), StringComparison.OrdinalIgnoreCase)
            .Replace("{MACHINE_NAME}", machineName, StringComparison.OrdinalIgnoreCase)
            .Trim();
    }
}
