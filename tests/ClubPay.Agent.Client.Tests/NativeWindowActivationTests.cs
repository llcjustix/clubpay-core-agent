using ClubPay.Agent.Client.Views;

namespace ClubPay.Agent.Client.Tests;

public sealed class NativeWindowActivationTests
{
    [Fact]
    public void WithNoActivate_AddsFlagWithoutChangingExistingStyles()
    {
        const int existingStyle = 0x00040000;

        var result = NativeWindowActivation.WithNoActivate(existingStyle, enabled: true);

        Assert.Equal(existingStyle | NativeWindowActivation.WsExNoActivate, result);
    }

    [Fact]
    public void WithNoActivate_RemovesOnlyNoActivateFlag()
    {
        const int existingStyle = 0x00040000;
        var style = existingStyle | NativeWindowActivation.WsExNoActivate;

        var result = NativeWindowActivation.WithNoActivate(style, enabled: false);

        Assert.Equal(existingStyle, result);
    }
}
