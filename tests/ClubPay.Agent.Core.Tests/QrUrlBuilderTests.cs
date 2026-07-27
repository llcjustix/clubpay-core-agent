namespace ClubPay.Agent.Core.Tests;

public class QrUrlBuilderTests
{
    [Fact]
    public void BuildLockScreenUrl_WhenGivenBaseUrlAndPcId_AppendsEscapedPcIdAsPathSegment()
    {
        var url = QrUrlBuilder.BuildLockScreenUrl("https://clubpay.justix.uz/qr", "pc 001");

        Assert.Equal("https://clubpay.justix.uz/qr/pc%20001", url);
    }
}
