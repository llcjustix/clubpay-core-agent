namespace ClubPay.Agent.Core.Services;

public interface IProcessCleanupService
{
    /// <summary>Save PIDs of currently running processes as the "safe baseline".</summary>
    void SnapshotBaseline();

    /// <summary>Legacy recovery cleanup. Does nothing until <see cref="SnapshotBaseline"/> was called.
    /// New launcher code must use <see cref="KillProcesses"/> with explicitly owned process IDs.</summary>
    void KillSessionProcesses();

    /// <summary>Stateless snapshot of currently running PIDs, independent of SnapshotBaseline's
    /// stored state. Callers keep their own baseline and pass it to GetForeignProcessIds later.</summary>
    IReadOnlySet<int> SnapshotProcessIds();

    /// <summary>PIDs currently running that are not in <paramref name="baseline"/> and are not a
    /// known-safe system process (same filter KillSessionProcesses uses). Non-destructive query.</summary>
    IReadOnlyCollection<int> GetForeignProcessIds(IReadOnlySet<int> baseline);

    /// <summary>Returns only processes started after <paramref name="baseline"/> whose executable
    /// process name is explicitly in <paramref name="allowedProcessNames"/>. When
    /// <paramref name="expectedModuleDirectory"/> is given, a candidate must also have its main
    /// module loaded from that directory — this narrows name-only matches (e.g. "chrome") so an
    /// unrelated same-named process started elsewhere (another install, an admin's own window)
    /// is not treated as owned by this launch.</summary>
    IReadOnlyCollection<int> GetProcessIdsStartedAfterBaseline(
        IReadOnlySet<int> baseline,
        IReadOnlySet<string> allowedProcessNames,
        string? expectedModuleDirectory = null);

    /// <summary>Best-effort termination of processes explicitly owned by the current launcher.</summary>
    void KillProcesses(IReadOnlyCollection<int> processIds);
}
