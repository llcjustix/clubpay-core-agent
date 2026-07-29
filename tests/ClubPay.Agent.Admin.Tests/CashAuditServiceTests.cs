using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ClubPay.Agent.Admin.Services;
using ClubPay.Agent.Core.Models;

namespace ClubPay.Agent.Admin.Tests;

public class CashAuditServiceTests : IDisposable
{
    private readonly string _logPath = Path.Combine(Path.GetTempPath(), $"clubpay-cash-audit-test-{Guid.NewGuid():N}.jsonl");

    private CashAuditService Build()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Admin:CashAuditLogPath"] = _logPath })
            .Build();
        return new CashAuditService(config, NullLogger<CashAuditService>.Instance);
    }

    [Fact]
    public async Task RecordAsync_WritesOneJsonLinePerEntry()
    {
        var service = Build();
        var entry = new CashAuditEntry("manager-1", "PC-01", 7200, 2_800_000, DateTime.UtcNow, "Internet yo'q");

        await service.RecordAsync(entry);

        var lines = await File.ReadAllLinesAsync(_logPath);
        var written = Assert.Single(lines);
        var parsed = JsonSerializer.Deserialize<CashAuditEntry>(written)!;
        Assert.Equal(entry.ManagerId, parsed.ManagerId);
        Assert.Equal(entry.PcId, parsed.PcId);
        Assert.Equal(entry.DurationSeconds, parsed.DurationSeconds);
        Assert.Equal(entry.AmountTiyin, parsed.AmountTiyin);
        Assert.Equal(entry.ReasonCode, parsed.ReasonCode);
    }

    [Fact]
    public async Task RecordAsync_MultipleEntries_AppendsRatherThanOverwrites()
    {
        var service = Build();

        await service.RecordAsync(new CashAuditEntry("m1", "PC-01", 1800, 500_000, DateTime.UtcNow, "Boshqa"));
        await service.RecordAsync(new CashAuditEntry("m2", "PC-02", 3600, 1_500_000, DateTime.UtcNow, "Mijoz iltimosi"));

        var lines = await File.ReadAllLinesAsync(_logPath);
        Assert.Equal(2, lines.Length);
    }

    public void Dispose()
    {
        if (File.Exists(_logPath))
            File.Delete(_logPath);
    }
}
