using ClubPay.Agent.Core.Models;

namespace ClubPay.Agent.Core.Services;

/// <summary>Append-only audit trail for manager-initiated cash payments (ТЗ §11, CLAUDE.md
/// "naqd to'lov — audit log majburiy"). Never overwrites or deletes prior entries.</summary>
public interface ICashAuditService
{
    Task RecordAsync(CashAuditEntry entry, CancellationToken ct = default);
}
