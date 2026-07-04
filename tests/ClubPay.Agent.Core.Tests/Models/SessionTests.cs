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
}
