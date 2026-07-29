namespace ClubPay.Agent.Core.Services;

/// <summary>Verifies the manager PIN gating a cash payment (ТЗ §11) against a configured hash —
/// a placeholder for the real admin-identity service until one exists.</summary>
public interface IManagerPinService
{
    bool Verify(string pin);
}
