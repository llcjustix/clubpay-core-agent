using ClubPay.Agent.Client.Views;
using ClubPay.Agent.Client.ViewModels;

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

    [Fact]
    public void SteamLaunch_TracksSteamWebHelperForItsVisibleWindow()
    {
        var app = new ClubPay.Agent.Core.Models.LauncherApp("Steam", @"C:\Program Files (x86)\Steam\Steam.exe");

        var names = GameLauncherViewModel.RelatedProcessNames(app);

        Assert.Equal(["steam", "steamwebhelper"], names);
    }
}
