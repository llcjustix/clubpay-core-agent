namespace ClubPay.Agent.Core.Models;

/// <summary>ТЗ §11 "pul o'g'irlanishidan himoya": har bir naqd sessiya majburiy audit yozuvi bilan —
/// menejer ID, PC, davomiylik, summa (long tiyin), vaqt (UTC), sabab kodi.</summary>
public sealed record CashAuditEntry(
    string ManagerId,
    string PcId,
    int DurationSeconds,
    long AmountTiyin,
    DateTime AtUtc,
    string ReasonCode);
