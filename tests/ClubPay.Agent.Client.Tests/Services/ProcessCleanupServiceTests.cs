using System.Diagnostics;
using System.IO;
using ClubPay.Agent.Client.Services;

namespace ClubPay.Agent.Client.Tests.Services;

public class ProcessCleanupServiceTests : IDisposable
{
    private readonly List<Process> _spawned = [];

    public void Dispose()
    {
        foreach (var proc in _spawned)
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); }
            catch { /* already gone */ }
            proc.Dispose();
        }
    }

    private Process SpawnDummy()
    {
        var proc = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 100 127.0.0.1 > nul")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        _spawned.Add(proc);
        return proc;
    }

    [Fact]
    public void GetForeignProcessIds_ProcessStartedAfterBaseline_IsReturned()
    {
        var sut = new ProcessCleanupService();
        var baseline = sut.SnapshotProcessIds();

        var dummy = SpawnDummy();

        var foreign = sut.GetForeignProcessIds(baseline);

        Assert.Contains(dummy.Id, foreign);
    }

    [Fact]
    public void GetForeignProcessIds_ProcessAlreadyInBaseline_IsExcluded()
    {
        var dummy = SpawnDummy();
        var sut = new ProcessCleanupService();
        var baseline = sut.SnapshotProcessIds(); // dummy already running -> counts as pre-existing

        var foreign = sut.GetForeignProcessIds(baseline);

        Assert.DoesNotContain(dummy.Id, foreign);
    }

    [Fact]
    public void GetForeignProcessIds_EmptyBaseline_NeverReturnsSelf()
    {
        var sut = new ProcessCleanupService();

        var foreign = sut.GetForeignProcessIds(new HashSet<int>());

        Assert.DoesNotContain(Environment.ProcessId, foreign);
    }

    [Fact]
    public void GetProcessIdsStartedAfterBaseline_ReturnsOnlyExplicitlyAllowedProcessName()
    {
        var sut = new ProcessCleanupService();
        var baseline = sut.SnapshotProcessIds();
        var dummy = SpawnDummy();

        var matching = sut.GetProcessIdsStartedAfterBaseline(baseline, new HashSet<string>(["cmd"]));
        var nonMatching = sut.GetProcessIdsStartedAfterBaseline(baseline, new HashSet<string>(["not-a-process"]));

        Assert.Contains(dummy.Id, matching);
        Assert.DoesNotContain(dummy.Id, nonMatching);
    }

    [Fact]
    public void GetProcessIdsStartedAfterBaseline_ExpectedModuleDirectoryMatches_ReturnsProcess()
    {
        var sut = new ProcessCleanupService();
        var baseline = sut.SnapshotProcessIds();
        var dummy = SpawnDummy();
        var moduleDir = Path.GetDirectoryName(dummy.MainModule!.FileName)!;

        var matching = sut.GetProcessIdsStartedAfterBaseline(baseline, new HashSet<string>(["cmd"]), moduleDir);

        Assert.Contains(dummy.Id, matching);
    }

    [Fact]
    public void GetProcessIdsStartedAfterBaseline_ExpectedModuleDirectoryDoesNotMatch_ExcludesProcessEvenIfNameMatches()
    {
        var sut = new ProcessCleanupService();
        var baseline = sut.SnapshotProcessIds();
        var dummy = SpawnDummy();

        var nonMatching = sut.GetProcessIdsStartedAfterBaseline(
            baseline, new HashSet<string>(["cmd"]), @"Z:\not-the-real-directory");

        Assert.DoesNotContain(dummy.Id, nonMatching);
    }

    [Fact]
    public void KillSessionProcesses_KillsProcessStartedAfterSnapshotBaseline()
    {
        var sut = new ProcessCleanupService();
        sut.SnapshotBaseline();
        var dummy = SpawnDummy();

        sut.KillSessionProcesses();

        dummy.WaitForExit(5000);
        Assert.True(dummy.HasExited);
    }

    [Fact]
    public void KillSessionProcesses_ProcessStartedBeforeSnapshotBaseline_IsNotKilled()
    {
        var dummy = SpawnDummy();
        var sut = new ProcessCleanupService();
        sut.SnapshotBaseline(); // dummy already running -> protected

        sut.KillSessionProcesses();

        Assert.False(dummy.HasExited);
    }

    [Fact]
    public void KillSessionProcesses_WithoutBaseline_DoesNotKillProcess()
    {
        var dummy = SpawnDummy();
        var sut = new ProcessCleanupService();

        sut.KillSessionProcesses();

        Assert.False(dummy.HasExited);
    }
}
