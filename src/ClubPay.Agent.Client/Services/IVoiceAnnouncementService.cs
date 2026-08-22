namespace ClubPay.Agent.Client.Services;

/// <summary>Announces remaining session time locally on the Windows PC.</summary>
public interface IVoiceAnnouncementService
{
    Task AnnounceRemainingTimeAsync(int remainingSeconds, CancellationToken ct = default);
}
