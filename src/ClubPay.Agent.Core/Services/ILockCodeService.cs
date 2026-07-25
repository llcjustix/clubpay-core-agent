using ClubPay.Agent.Core.Models;

namespace ClubPay.Agent.Core.Services;

/// <summary>ТЗ §7: "единое поле для клиентского ваучера и мастер-кода менеджера (агент распознаёт
/// тип)" — takes whatever was typed into the LockScreen's single input field, detects whether it is a
/// voucher or a manager master code, and routes it to the matching redeemer. This keeps every bit of
/// detection/ordering logic out of the ViewModel (CLAUDE.md: no business logic in ViewModels).</summary>
public interface ILockCodeService
{
    Task<LockCodeResult> SubmitAsync(string code, CancellationToken ct = default);
}
