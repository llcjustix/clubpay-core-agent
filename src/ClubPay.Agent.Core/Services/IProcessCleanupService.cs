namespace ClubPay.Agent.Core.Services;

public interface IProcessCleanupService
{
    /// <summary>Save PIDs of currently running processes as the "safe baseline".</summary>
    void SnapshotBaseline();

    /// <summary>Kill all processes started after the last SnapshotBaseline call.</summary>
    void KillSessionProcesses();
}
