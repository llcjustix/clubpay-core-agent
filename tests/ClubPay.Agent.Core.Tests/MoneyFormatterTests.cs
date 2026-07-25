using ClubPay.Agent.Core;
using Xunit;

namespace ClubPay.Agent.Core.Tests;

public class MoneyFormatterTests
{
    [Fact]
    public void Format_ZeroTiyin_ReturnsZeroSom()
    {
        Assert.Equal("0 so'm", MoneyFormatter.Format(0));
    }

    [Fact]
    public void Format_FifteenThousandSom_ReturnsSpaceGroupedString()
    {
        Assert.Equal("15 000 so'm", MoneyFormatter.Format(1_500_000));
    }

    [Fact]
    public void Format_SubSomRemainder_TruncatesTowardZero()
    {
        Assert.Equal("1 so'm", MoneyFormatter.Format(199));
    }

    [Fact]
    public void Format_MillionSom_GroupsEveryThreeDigits()
    {
        Assert.Equal("1 234 567 so'm", MoneyFormatter.Format(123_456_700));
    }

    [Fact]
    public void FormatSom_DelegatesToMoneyFormatter()
    {
        Assert.Equal(MoneyFormatter.Format(2_800_000), Constants.Money.FormatSom(2_800_000));
    }
}
