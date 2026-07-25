using System.Diagnostics;
using ClubPay.Agent.Core.Services;

namespace ClubPay.Agent.Client.Services;

public sealed class ProcessCleanupService : IProcessCleanupService
{
    private HashSet<int> _baseline = [];
    private readonly int _selfPid = Environment.ProcessId;

    // Processes that should never be killed even if started after baseline
    private static readonly HashSet<string> _safeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "dwm", "winlogon", "csrss", "lsass", "svchost",
        "services", "smss", "wininit", "fontdrvhost", "sihost",
        "taskhostw", "ctfmon", "rundll32", "conhost", "dllhost",
        "SearchIndexer", "RuntimeBroker", "ShellExperienceHost",
        "StartMenuExperienceHost", "ApplicationFrameHost",
        "SystemSettings", "TextInputHost"
    };

    public void SnapshotBaseline() => _baseline = SnapshotProcessIds().ToHashSet();

    public void KillSessionProcesses()
    {
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (!IsForeign(proc, _baseline)) continue;
                proc.Kill(entireProcessTree: true);
            }
            catch { /* process may have already exited or be protected */ }
            finally
            {
                proc.Dispose();
            }
        }
    }

    public IReadOnlySet<int> SnapshotProcessIds() =>
        Process.GetProcesses()
            .Select(p => { try { return p.Id; } catch { return -1; } })
            .Where(id => id > 0)
            .ToHashSet();

    public IReadOnlyCollection<int> GetForeignProcessIds(IReadOnlySet<int> baseline)
    {
        var result = new List<int>();
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (IsForeign(proc, baseline)) result.Add(proc.Id);
            }
            catch { /* process may have already exited or be protected */ }
            finally
            {
                proc.Dispose();
            }
        }
        return result;
    }

    private bool IsForeign(Process proc, IReadOnlySet<int> baseline)
    {
        if (proc.Id == _selfPid) return false; // never treat self as foreign
        if (baseline.Contains(proc.Id)) return false; // was running before baseline
        if (_safeNames.Contains(proc.ProcessName)) return false; // system process
        return true;
    }
}
