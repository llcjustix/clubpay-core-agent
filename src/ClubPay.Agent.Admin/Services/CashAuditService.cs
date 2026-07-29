using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ClubPay.Agent.Core.Models;
using ClubPay.Agent.Core.Services;

namespace ClubPay.Agent.Admin.Services;

/// <summary>Append-only JSON-lines cash audit log — one line per naqd sessiya, never rewritten.
/// A local file (not a database) is enough for the MVP/pilot scope this Admin targets; the
/// structured ILogger line gives the same data to whatever log aggregation is already in place.</summary>
public sealed class CashAuditService : ICashAuditService
{
    private readonly string _logFilePath;
    private readonly ILogger<CashAuditService> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public CashAuditService(IConfiguration config, ILogger<CashAuditService> logger)
    {
        _logger = logger;

        var configuredPath = config["Admin:CashAuditLogPath"];
        _logFilePath = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "ClubPay", "Admin", "cash-audit.jsonl")
            : configuredPath;
    }

    public async Task RecordAsync(CashAuditEntry entry, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Naqd to'lov audit: manager={ManagerId} pc={PcId} davomiylik={DurationSeconds}s summa={AmountTiyin} sabab={ReasonCode}",
            entry.ManagerId, entry.PcId, entry.DurationSeconds, entry.AmountTiyin, entry.ReasonCode);

        var line = JsonSerializer.Serialize(entry) + Environment.NewLine;

        await _writeLock.WaitAsync(ct);
        try
        {
            var directory = Path.GetDirectoryName(_logFilePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            await File.AppendAllTextAsync(_logFilePath, line, ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
