using System.Diagnostics;
using System.IO;
using ClubPay.Agent.Core.Services;

namespace ClubPay.Agent.Client.Services;

public sealed class ProcessCleanupService : IProcessCleanupService
{
    private HashSet<int> _baseline = [];
    private bool _hasBaseline;
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

    public void SnapshotBaseline()
    {
        _baseline = SnapshotProcessIds().ToHashSet();
        _hasBaseline = true;
    }

    public void KillSessionProcesses()
    {
        // A persisted session can expire while the agent is stopped. In that case this process
        // instance has no trustworthy ownership baseline, so killing by PID-diff would terminate
        // unrelated user applications. Current launcher-owned processes are terminated explicitly.
        if (!_hasBaseline)
            return;

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

    public IReadOnlyCollection<int> GetProcessIdsStartedAfterBaseline(
        IReadOnlySet<int> baseline,
        IReadOnlySet<string> allowedProcessNames,
        string? expectedModuleDirectory = null)
    {
        if (allowedProcessNames.Count == 0)
            return [];

        var result = new List<int>();
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (proc.Id != _selfPid
                    && !baseline.Contains(proc.Id)
                    && allowedProcessNames.Contains(proc.ProcessName)
                    && (string.IsNullOrEmpty(expectedModuleDirectory) || ModuleDirectoryMatches(proc, expectedModuleDirectory)))
                    result.Add(proc.Id);
            }
            catch { /* process may have already exited or be protected */ }
            finally
            {
                proc.Dispose();
            }
        }
        return result;
    }

    private static bool ModuleDirectoryMatches(Process proc, string expectedDirectory)
    {
        try
        {
            var moduleDir = Path.GetDirectoryName(proc.MainModule?.FileName);
            return moduleDir is not null && moduleDir.Equals(expectedDirectory, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; /* elevated/protected process or exited between enumeration and access — fail closed */ }
    }

    public void KillProcesses(IReadOnlyCollection<int> processIds)
    {
        foreach (var pid in processIds.Distinct())
        {
            if (pid == _selfPid)
                continue;

            try
            {
                using var proc = Process.GetProcessById(pid);
                if (!proc.HasExited)
                    proc.Kill(entireProcessTree: true);
            }
            catch { /* process may have already exited or be protected */ }
        }
    }

    private bool IsForeign(Process proc, IReadOnlySet<int> baseline)
    {
        if (proc.Id == _selfPid) return false; // never treat self as foreign
        if (baseline.Contains(proc.Id)) return false; // was running before baseline
        if (_safeNames.Contains(proc.ProcessName)) return false; // system process
        return true;
    }
}
