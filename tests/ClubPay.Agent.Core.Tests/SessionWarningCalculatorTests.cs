namespace ClubPay.Agent.Core.Tests;

public class SessionWarningCalculatorTests
{
    [Fact]
    public void ShouldShowToast_WhenCrossingExactly300Boundary_ReturnsTrue()
    {
        Assert.True(SessionWarningCalculator.ShouldShowToast(previousRemainingSeconds: 301, currentRemainingSeconds: 300));
    }

    [Fact]
    public void ShouldShowToast_WhenAlreadyBelowThresholdBothTicks_ReturnsFalse()
    {
        Assert.False(SessionWarningCalculator.ShouldShowToast(previousRemainingSeconds: 300, currentRemainingSeconds: 299));
    }

    [Fact]
    public void ShouldShowToast_WhenSentinelPreviousValue_ReturnsFalse()
    {
        Assert.False(SessionWarningCalculator.ShouldShowToast(previousRemainingSeconds: -1, currentRemainingSeconds: 250));
    }

    [Fact]
    public void ShouldShowToast_WhenExtendedAboveThresholdThenDropsAgain_ReturnsTrue()
    {
        Assert.True(SessionWarningCalculator.ShouldShowToast(previousRemainingSeconds: 310, currentRemainingSeconds: 295));
    }

    [Fact]
    public void ShouldShowToast_WhenCurrentIsZero_ReturnsFalse()
    {
        Assert.False(SessionWarningCalculator.ShouldShowToast(previousRemainingSeconds: 301, currentRemainingSeconds: 0));
    }

    [Fact]
    public void IsBannerVisible_AtSixtySeconds_ReturnsTrue()
    {
        Assert.True(SessionWarningCalculator.IsBannerVisible(60));
    }

    [Fact]
    public void IsBannerVisible_AtSixtyOneSeconds_ReturnsFalse()
    {
        Assert.False(SessionWarningCalculator.IsBannerVisible(61));
    }

    [Fact]
    public void IsBannerVisible_AtZero_ReturnsFalse()
    {
        Assert.False(SessionWarningCalculator.IsBannerVisible(0));
    }

    [Fact]
    public void IsAnyWarningVisible_AtSixHundredSeconds_ReturnsTrue()
    {
        Assert.True(SessionWarningCalculator.IsAnyWarningVisible(600));
    }

    [Fact]
    public void IsAnyWarningVisible_AtSixHundredOneSeconds_ReturnsFalse()
    {
        Assert.False(SessionWarningCalculator.IsAnyWarningVisible(601));
    }

    [Fact]
    public void IsAnyWarningVisible_AtZero_ReturnsFalse()
    {
        Assert.False(SessionWarningCalculator.IsAnyWarningVisible(0));
    }
}
