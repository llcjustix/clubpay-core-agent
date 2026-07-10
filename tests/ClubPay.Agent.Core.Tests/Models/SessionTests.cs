using ClubPay.Agent.Core.Models;

namespace ClubPay.Agent.Core.Tests.Models;

public class SessionTests
{
    [Fact]
    public void Constructor_WhenCoreSessionIdOmitted_DefaultsToNull()
    {
        var tariff = new Tariff(Guid.NewGuid(), "1 soat", ZoneType.Standard, 60, 1_500_000);
        var session = new Session(Guid.NewGuid(), "PC-12", tariff, DateTime.UtcNow, 3600);

        Assert.Null(session.CoreSessionId);
    }

    [Fact]
    public void Constructor_WhenGrantIdAndEndsAtUtcOmitted_DefaultToNull()
    {
        var tariff = new Tariff(Guid.NewGuid(), "1 soat", ZoneType.Standard, 60, 1_500_000);
        var session = new Session(Guid.NewGuid(), "PC-12", tariff, DateTime.UtcNow, 3600);

        Assert.Null(session.GrantId);
        Assert.Null(session.EndsAtUtc);
        Assert.Null(session.Zone);
    }

    [Fact]
    public void RemainingSeconds_WhenEndsAtUtcProvidedAndClockConsistent_MatchesGrantedMinusElapsed()
    {
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var tariff = new Tariff(Guid.NewGuid(), "1 soat", ZoneType.Standard, 60, 1_500_000);
        var session = new Session(
            Guid.NewGuid(), "PC-12", tariff, start, GrantedSeconds: 3600,
            GrantId: "grant_1001", EndsAtUtc: start.AddSeconds(3600), Zone: "Standard");

        var now = start.AddSeconds(1000);

        Assert.Equal(2600, session.RemainingSeconds(now));
    }
}
