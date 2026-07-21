namespace ClubPay.Agent.Core.Tests;

public class QrUrlBuilderTests
{
    [Fact]
    public void BuildLockScreenUrl_WhenGivenBaseUrlAndPcId_AppendsEscapedPcIdAsPathSegment()
    {
        var url = QrUrlBuilder.BuildLockScreenUrl("https://clubpay.justix.uz/qr", "pc 001");

        Assert.Equal("https://clubpay.justix.uz/qr/pc%20001", url);
    }

    [Fact]
    public void BuildSessionUrl_WhenCoreSessionIdPresent_UsesCoreSessionIdInNFormat()
    {
        var coreId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var localId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var url = QrUrlBuilder.BuildSessionUrl("https://clubpay.justix.uz/qr", "pc-001", coreId, localId);

        Assert.Equal($"https://clubpay.justix.uz/qr?pc=pc-001&session={coreId:N}", url);
    }

    [Fact]
    public void BuildSessionUrl_WhenCoreSessionIdAbsent_FallsBackToLocalSessionId()
    {
        var localId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var url = QrUrlBuilder.BuildSessionUrl("https://clubpay.justix.uz/qr", "pc-001", null, localId);

        Assert.Equal($"https://clubpay.justix.uz/qr?pc=pc-001&session={localId:N}", url);
    }

    [Fact]
    public void BuildSessionUrl_WhenBothIdsAbsent_OmitsSessionParam()
    {
        var url = QrUrlBuilder.BuildSessionUrl("https://clubpay.justix.uz/qr", "pc-001", null, null);

        Assert.Equal("https://clubpay.justix.uz/qr?pc=pc-001", url);
    }

    [Fact]
    public void BuildSessionUrl_AlwaysIncludesEscapedExternalPcId()
    {
        var url = QrUrlBuilder.BuildSessionUrl("https://clubpay.justix.uz/qr", "pc 001", null, null);

        Assert.Equal("https://clubpay.justix.uz/qr?pc=pc%20001", url);
    }
}
