namespace ClubPay.Agent.Core.Services;

public interface IProcessCleanupService
{
    /// <summary>Save PIDs of currently running processes as the "safe baseline".</summary>
    void SnapshotBaseline();

    /// <summary>Kill all processes started after the last SnapshotBaseline call.</summary>
    void KillSessionProcesses();

    /// <summary>Stateless snapshot of currently running PIDs, independent of SnapshotBaseline's
    /// stored state. Callers keep their own baseline and pass it to GetForeignProcessIds later.</summary>
    IReadOnlySet<int> SnapshotProcessIds();

    /// <summary>PIDs currently running that are not in <paramref name="baseline"/> and are not a
    /// known-safe system process (same filter KillSessionProcesses uses). Non-destructive query.</summary>
    IReadOnlyCollection<int> GetForeignProcessIds(IReadOnlySet<int> baseline);
}
