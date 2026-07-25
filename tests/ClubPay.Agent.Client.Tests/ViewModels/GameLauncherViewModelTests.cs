using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ClubPay.Agent.Client.Services;
using ClubPay.Agent.Client.ViewModels;
using ClubPay.Agent.Core.Models;
using ClubPay.Agent.Core.Services;

namespace ClubPay.Agent.Client.Tests.ViewModels;

public class GameLauncherViewModelTests
{
    private static GameLauncherViewModel BuildSut(
        Mock<IProcessCleanupService> processCleanup,
        ILogger<GameLauncherViewModel>? logger = null)
    {
        var config = new ConfigurationBuilder().Build();
        var agent = new Mock<IAgentService>();
        agent.SetupGet(a => a.ClubName).Returns("Test Club");
        agent.SetupGet(a => a.PcId).Returns("PC-1");

        var activeSession = new ActiveSessionViewModel(
            new QrCodeService(NullLogger<QrCodeService>.Instance), agent.Object);

        return new GameLauncherViewModel(
            config, agent.Object, activeSession, processCleanup.Object,
            logger ?? NullLogger<GameLauncherViewModel>.Instance);
    }

    [Fact]
    public async Task LaunchApp_ExeDoesNotExist_LogsWarningAndShowsLaunchError()
    {
        var processCleanup = new Mock<IProcessCleanupService>();
        processCleanup.Setup(p => p.SnapshotProcessIds()).Returns(new HashSet<int>());
        var logger = new Mock<ILogger<GameLauncherViewModel>>();
        var sut = BuildSut(processCleanup, logger.Object);
        var app = new LauncherApp("Ghost Game", @"Z:\does-not-exist-clubpay-test\ghost.exe");

        await sut.LaunchApp(app);

        Assert.False(sut.IsAppRunning);
        Assert.True(sut.IsLaunchErrorVisible);
        Assert.Contains(app.Name, sut.LaunchErrorMessage);
        logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task LaunchApp_HandoffProcessExitsButForeignProcessRemains_KeepsIsAppRunningTrueUntilForeignGone()
    {
        var processCleanup = new Mock<IProcessCleanupService>();
        processCleanup.Setup(p => p.SnapshotProcessIds()).Returns(new HashSet<int>());
        processCleanup.SetupSequence(p => p.GetForeignProcessIds(It.IsAny<IReadOnlySet<int>>()))
            .Returns(new List<int> { 999999 })   // simulated handed-off game still running
            .Returns(new List<int>())            // 1st empty poll
            .Returns(new List<int>());           // 2nd empty poll -> declared closed

        var sut = BuildSut(processCleanup);
        // "cmd /c exit" stands in for a launcher (e.g. Steam.exe -applaunch) that hands off and
        // exits almost immediately while the real game keeps running under another PID.
        var app = new LauncherApp("Fake Steam Handoff", "cmd.exe", Args: "/c exit");

        var returnRequestedCount = 0;
        sut.ReturnRequested += () => returnRequestedCount++;

        var launchTask = sut.LaunchApp(app);

        await Task.Delay(TimeSpan.FromSeconds(1.5));
        Assert.True(sut.IsAppRunning);
        Assert.Equal(0, returnRequestedCount);

        await launchTask;

        Assert.False(sut.IsAppRunning);
        Assert.Equal(1, returnRequestedCount);
    }

    [Fact]
    public async Task KillRunningApp_WhilePolling_StopsPollingAndSetsIsAppRunningFalse()
    {
        var processCleanup = new Mock<IProcessCleanupService>();
        processCleanup.Setup(p => p.SnapshotProcessIds()).Returns(new HashSet<int>());
        processCleanup.Setup(p => p.GetForeignProcessIds(It.IsAny<IReadOnlySet<int>>()))
            .Returns(new List<int> { 12345 }); // always "still running" without cancellation

        var sut = BuildSut(processCleanup);
        var app = new LauncherApp("Fake Handoff", "cmd.exe", Args: "/c exit");

        var launchTask = sut.LaunchApp(app);
        await Task.Delay(TimeSpan.FromSeconds(0.5));
        Assert.True(sut.IsAppRunning);

        sut.KillRunningApp();

        var winner = await Task.WhenAny(launchTask, Task.Delay(TimeSpan.FromSeconds(3)));
        Assert.Same(launchTask, winner);
        Assert.False(sut.IsAppRunning);
    }

    [Fact]
    public async Task LaunchApp_SameAppTileClickedWhileRunning_RefiresAppLaunchedWithoutRestarting()
    {
        var processCleanup = new Mock<IProcessCleanupService>();
        processCleanup.Setup(p => p.SnapshotProcessIds()).Returns(new HashSet<int>());
        processCleanup.Setup(p => p.GetForeignProcessIds(It.IsAny<IReadOnlySet<int>>())).Returns(new List<int>());

        var sut = BuildSut(processCleanup);
        var app = new LauncherApp("Long Runner", "cmd.exe", Args: "/c ping -n 100 127.0.0.1 > nul");

        var launchTask = sut.LaunchApp(app);
        await Task.Delay(TimeSpan.FromSeconds(0.5));
        Assert.True(sut.IsAppRunning);

        var appLaunchedCount = 0;
        sut.AppLaunched += _ => appLaunchedCount++;

        await sut.LaunchApp(app); // same tile clicked again — should not relaunch

        Assert.Equal(1, appLaunchedCount);
        Assert.True(sut.IsAppRunning);
        Assert.Same(app, sut.RunningApp);

        sut.KillRunningApp();
        await Task.WhenAny(launchTask, Task.Delay(TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task LaunchApp_DifferentAppClickedWhileRunning_ShowsBusyNotice()
    {
        var processCleanup = new Mock<IProcessCleanupService>();
        processCleanup.Setup(p => p.SnapshotProcessIds()).Returns(new HashSet<int>());
        processCleanup.Setup(p => p.GetForeignProcessIds(It.IsAny<IReadOnlySet<int>>())).Returns(new List<int>());

        var sut = BuildSut(processCleanup);
        var runningApp = new LauncherApp("Long Runner", "cmd.exe", Args: "/c ping -n 100 127.0.0.1 > nul");
        var otherApp = new LauncherApp("Other App", @"Z:\does-not-exist-clubpay-test\other.exe");

        var launchTask = sut.LaunchApp(runningApp);
        await Task.Delay(TimeSpan.FromSeconds(0.5));
        Assert.True(sut.IsAppRunning);

        await sut.LaunchApp(otherApp); // different tile clicked while busy — must not launch it

        Assert.True(sut.IsLaunchErrorVisible);
        Assert.Contains(runningApp.Name, sut.LaunchErrorMessage);
        Assert.Same(runningApp, sut.RunningApp);

        sut.KillRunningApp();
        await Task.WhenAny(launchTask, Task.Delay(TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public void ReturnToLauncher_InvokesReturnRequested()
    {
        var processCleanup = new Mock<IProcessCleanupService>();
        processCleanup.Setup(p => p.GetForeignProcessIds(It.IsAny<IReadOnlySet<int>>())).Returns(new List<int>());
        var sut = BuildSut(processCleanup);

        var invoked = false;
        sut.ReturnRequested += () => invoked = true;

        sut.ReturnToLauncherCommand.Execute(null);

        Assert.True(invoked);
    }
}
