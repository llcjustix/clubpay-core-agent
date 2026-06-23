using System.Diagnostics;
using ClubPay.Agent.Core.Services;

namespace ClubPay.Agent.Client.Services;

public sealed class ProcessCleanupService : IProcessCleanupService
{
    private HashSet<int> _baseline = [];
    private readonly int _selfPid  = Environment.ProcessId;

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

    public void SnapshotBaseline()
    {
        _baseline = Process.GetProcesses()
            .Select(p => { try { return p.Id; } catch { return -1; } })
            .Where(id => id > 0)
            .ToHashSet();
    }

    public void KillSessionProcesses()
    {
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (proc.Id == _selfPid)           continue; // never kill self
                if (_baseline.Contains(proc.Id))   continue; // was running before session
                if (_safeNames.Contains(proc.ProcessName)) continue; // system process

                proc.Kill(entireProcessTree: true);
            }
            catch { /* process may have already exited or be protected */ }
            finally
            {
                proc.Dispose();
            }
        }
    }
}
