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
        ILogger<GameLauncherViewModel>? logger = null,
        IConfiguration? config = null)
    {
        processCleanup
            .Setup(p => p.GetProcessIdsStartedAfterBaseline(
                It.IsAny<IReadOnlySet<int>>(), It.IsAny<IReadOnlySet<string>>(), It.IsAny<string?>()))
            .Returns(Array.Empty<int>());

        var agent = new Mock<IAgentService>();
        agent.SetupGet(a => a.ClubName).Returns("Test Club");
        agent.SetupGet(a => a.PcId).Returns("PC-1");

        var activeSession = new ActiveSessionViewModel(
            new QrCodeService(NullLogger<QrCodeService>.Instance), agent.Object);

        return new GameLauncherViewModel(
            config ?? new ConfigurationBuilder().Build(), agent.Object, activeSession, processCleanup.Object,
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
        processCleanup.SetupSequence(p => p.GetProcessIdsStartedAfterBaseline(
                It.IsAny<IReadOnlySet<int>>(), It.IsAny<IReadOnlySet<string>>(), It.IsAny<string?>()))
            .Returns(new List<int> { 999999 })   // simulated handed-off game still running
            .Returns(new List<int>())            // 1st empty poll
            .Returns(new List<int>());           // 2nd empty poll -> declared closed

        var sut = BuildSut(processCleanup);
        // "cmd /c exit" stands in for a launcher (e.g. Steam.exe -applaunch) that hands off and
        // exits almost immediately while the real game keeps running under another PID.
        var app = new LauncherApp(
            "Fake Steam Handoff", "cmd.exe", Args: "/c exit",
            ProcessNames: new HashSet<string>(["handed-off-game"]));

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
        processCleanup.Setup(p => p.GetProcessIdsStartedAfterBaseline(
                It.IsAny<IReadOnlySet<int>>(), It.IsAny<IReadOnlySet<string>>(), It.IsAny<string?>()))
            .Returns(new List<int> { 12345 }); // always "still running" without cancellation

        var sut = BuildSut(processCleanup);
        var app = new LauncherApp("Fake Handoff", "cmd.exe", Args: "/c exit");

        var returnRequestedCount = 0;
        sut.ReturnRequested += () => returnRequestedCount++;

        var launchTask = sut.LaunchApp(app);
        await Task.Delay(TimeSpan.FromSeconds(0.5));
        Assert.True(sut.IsAppRunning);

        sut.KillRunningApp();

        var winner = await Task.WhenAny(launchTask, Task.Delay(TimeSpan.FromSeconds(3)));
        Assert.Same(launchTask, winner);
        Assert.False(sut.IsAppRunning);
        // Regression guard for the kiosk-bypass bug: killing the app must not let the interrupted
        // LaunchApp task pop the launcher shell back up over whatever the Locked-state UI already
        // showed — ReturnRequested is only for a genuinely closed app.
        Assert.Equal(0, returnRequestedCount);
    }

    [Fact]
    public async Task LaunchApp_SupersededByNewLaunchAfterKill_DoesNotInvokeReturnRequestedForSupersededCall()
    {
        var processCleanup = new Mock<IProcessCleanupService>();
        processCleanup.Setup(p => p.SnapshotProcessIds()).Returns(new HashSet<int>());
        processCleanup.Setup(p => p.GetProcessIdsStartedAfterBaseline(
                It.IsAny<IReadOnlySet<int>>(), It.IsAny<IReadOnlySet<string>>(), It.IsAny<string?>()))
            .Returns(new List<int> { 12345 }); // neither app converges naturally on its own

        var sut = BuildSut(processCleanup);
        var appA = new LauncherApp("App A", "cmd.exe", Args: "/c exit");
        var appB = new LauncherApp("App B", "cmd.exe", Args: "/c exit");

        var returnRequestedCount = 0;
        sut.ReturnRequested += () => returnRequestedCount++;

        var launchTaskA = sut.LaunchApp(appA);
        await Task.Delay(TimeSpan.FromSeconds(0.5));
        Assert.True(sut.IsAppRunning);

        sut.KillRunningApp();
        var launchTaskB = sut.LaunchApp(appB);

        var winner = await Task.WhenAny(launchTaskA, Task.Delay(TimeSpan.FromSeconds(3)));
        Assert.Same(launchTaskA, winner);
        Assert.Equal(0, returnRequestedCount);

        sut.KillRunningApp();
        await Task.WhenAny(launchTaskB, Task.Delay(TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task LaunchApp_CalledAgainAfterKill_DoesNotThrow()
    {
        var processCleanup = new Mock<IProcessCleanupService>();
        processCleanup.Setup(p => p.SnapshotProcessIds()).Returns(new HashSet<int>());
        processCleanup.Setup(p => p.GetProcessIdsStartedAfterBaseline(
                It.IsAny<IReadOnlySet<int>>(), It.IsAny<IReadOnlySet<string>>(), It.IsAny<string?>()))
            .Returns(new List<int> { 12345 });

        var sut = BuildSut(processCleanup);
        var appA = new LauncherApp("App A", "cmd.exe", Args: "/c exit");
        var appB = new LauncherApp("App B", "cmd.exe", Args: "/c exit");

        var launchTaskA = sut.LaunchApp(appA);
        await Task.Delay(TimeSpan.FromSeconds(0.5));
        sut.KillRunningApp();
        await Task.WhenAny(launchTaskA, Task.Delay(TimeSpan.FromSeconds(3)));

        // Reusing _pollCts right after it was cancelled+disposed by KillRunningApp must not throw
        // (ObjectDisposedException would mean the Cancel-then-Dispose ordering is unsafe).
        var exception = await Record.ExceptionAsync(async () =>
        {
            var launchTaskB = sut.LaunchApp(appB);
            await Task.Delay(TimeSpan.FromSeconds(0.5));
            sut.KillRunningApp();
            await Task.WhenAny(launchTaskB, Task.Delay(TimeSpan.FromSeconds(3)));
        });

        Assert.Null(exception);
    }

    [Fact]
    public async Task KillRunningApp_CalledTwiceInARow_DoesNotThrow()
    {
        var processCleanup = new Mock<IProcessCleanupService>();
        processCleanup.Setup(p => p.SnapshotProcessIds()).Returns(new HashSet<int>());
        processCleanup.Setup(p => p.GetProcessIdsStartedAfterBaseline(
                It.IsAny<IReadOnlySet<int>>(), It.IsAny<IReadOnlySet<string>>(), It.IsAny<string?>()))
            .Returns(new List<int> { 12345 });

        var sut = BuildSut(processCleanup);
        var app = new LauncherApp("App", "cmd.exe", Args: "/c exit");

        var launchTask = sut.LaunchApp(app);
        await Task.Delay(TimeSpan.FromSeconds(0.5));

        var exception = Record.Exception(() =>
        {
            sut.KillRunningApp();
            sut.KillRunningApp(); // second call must see a null _pollCts/_errorCts and no-op safely
        });

        Assert.Null(exception);
        await Task.WhenAny(launchTask, Task.Delay(TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task LaunchApp_SameAppTileClickedWhileRunning_RefiresAppLaunchedWithoutRestarting()
    {
        var processCleanup = new Mock<IProcessCleanupService>();
        processCleanup.Setup(p => p.SnapshotProcessIds()).Returns(new HashSet<int>());
        processCleanup.Setup(p => p.GetProcessIdsStartedAfterBaseline(
                It.IsAny<IReadOnlySet<int>>(), It.IsAny<IReadOnlySet<string>>(), It.IsAny<string?>())).Returns([]);

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
        processCleanup.Setup(p => p.GetProcessIdsStartedAfterBaseline(
                It.IsAny<IReadOnlySet<int>>(), It.IsAny<IReadOnlySet<string>>(), It.IsAny<string?>())).Returns([]);

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
        processCleanup.Setup(p => p.GetProcessIdsStartedAfterBaseline(
                It.IsAny<IReadOnlySet<int>>(), It.IsAny<IReadOnlySet<string>>(), It.IsAny<string?>())).Returns([]);
        var sut = BuildSut(processCleanup);

        var invoked = false;
        sut.ReturnRequested += () => invoked = true;

        sut.ReturnToLauncherCommand.Execute(null);

        Assert.True(invoked);
    }

    [Fact]
    public async Task LaunchApp_BrowserAppAlreadyRunning_AdoptsExistingProcessWithoutRestarting()
    {
        var processCleanup = new Mock<IProcessCleanupService>();
        processCleanup.Setup(p => p.SnapshotProcessIds()).Returns(new HashSet<int> { 1, 2, 3 });
        var sut = BuildSut(processCleanup);
        // Registered after BuildSut() so these more specific matchers win over BuildSut's generic
        // It.IsAny fallback setup — Moq resolves overlapping setups by "last one registered wins".
        processCleanup
            .Setup(p => p.GetProcessIdsStartedAfterBaseline(
                It.Is<IReadOnlySet<int>>(b => b.Count == 0), It.IsAny<IReadOnlySet<string>>(), It.IsAny<string?>()))
            .Returns(new List<int> { 777 }); // already-running instance found on the adopt-check
        processCleanup
            .Setup(p => p.GetProcessIdsStartedAfterBaseline(
                It.Is<IReadOnlySet<int>>(b => b.Count > 0), It.IsAny<IReadOnlySet<string>>(), It.IsAny<string?>()))
            .Returns([]);

        var app = new LauncherApp(
            "Chrome", @"C:\does-not-exist-clubpay-test\chrome.exe", Type: LauncherAppType.Browser,
            ProcessNames: new HashSet<string>(["chrome"]));

        var launchTask = sut.LaunchApp(app);
        await Task.Delay(TimeSpan.FromSeconds(0.3));

        // If adoption hadn't triggered, LaunchApp would have fallen through to Process.Start on a
        // bogus ExePath, thrown, and shown a launch error instead of ever reaching IsAppRunning=true.
        Assert.True(sut.IsAppRunning);
        Assert.Same(app, sut.RunningApp);
        Assert.False(sut.IsLaunchErrorVisible);

        sut.KillRunningApp();
        await Task.WhenAny(launchTask, Task.Delay(TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task LaunchApp_BrowserAppAlreadyRunning_ClosingAdoptedProcessInvokesReturnRequested()
    {
        var processCleanup = new Mock<IProcessCleanupService>();
        processCleanup.Setup(p => p.SnapshotProcessIds()).Returns(new HashSet<int> { 1, 2, 3 });
        var sut = BuildSut(processCleanup);
        // Registered after BuildSut() so these more specific matchers win over BuildSut's generic
        // It.IsAny fallback setup — Moq resolves overlapping setups by "last one registered wins".
        processCleanup
            .Setup(p => p.GetProcessIdsStartedAfterBaseline(
                It.Is<IReadOnlySet<int>>(b => b.Count == 0), It.IsAny<IReadOnlySet<string>>(), It.IsAny<string?>()))
            .Returns(new List<int> { 777 });
        processCleanup
            .Setup(p => p.GetProcessIdsStartedAfterBaseline(
                It.Is<IReadOnlySet<int>>(b => b.Count > 0), It.IsAny<IReadOnlySet<string>>(), It.IsAny<string?>()))
            .Returns([]); // no further handoff processes appear post-adoption

        var app = new LauncherApp(
            "Chrome", @"C:\does-not-exist-clubpay-test\chrome.exe", Type: LauncherAppType.Browser,
            ProcessNames: new HashSet<string>(["chrome"]));

        var returnRequestedCount = 0;
        sut.ReturnRequested += () => returnRequestedCount++;

        var launchTask = sut.LaunchApp(app);
        await Task.Delay(TimeSpan.FromSeconds(0.5));
        Assert.True(sut.IsAppRunning);

        // PID 777 doesn't correspond to a real process, so IsProcessRunning(777) is false from the
        // first poll — the adopted instance is declared closed within RequiredConsecutiveEmptyPolls.
        await launchTask;

        Assert.False(sut.IsAppRunning);
        Assert.Equal(1, returnRequestedCount);
    }

    [Fact]
    public async Task LaunchApp_BrowserAppNotCurrentlyRunning_FallsThroughToNormalLaunch()
    {
        var processCleanup = new Mock<IProcessCleanupService>();
        processCleanup.Setup(p => p.SnapshotProcessIds()).Returns(new HashSet<int>());
        processCleanup
            .Setup(p => p.GetProcessIdsStartedAfterBaseline(
                It.IsAny<IReadOnlySet<int>>(), It.IsAny<IReadOnlySet<string>>(), It.IsAny<string?>()))
            .Returns([]); // nothing already running, and no handoff children appear either

        var sut = BuildSut(processCleanup);
        var app = new LauncherApp(
            "Ghost Browser", @"Z:\does-not-exist-clubpay-test\ghost.exe",
            Type: LauncherAppType.Browser, ProcessNames: new HashSet<string>(["ghost"]));

        await sut.LaunchApp(app);

        Assert.False(sut.IsAppRunning);
        Assert.True(sut.IsLaunchErrorVisible); // proves the normal Process.Start error path was taken
        Assert.Contains(app.Name, sut.LaunchErrorMessage);
    }

    [Fact]
    public void Constructor_DuplicateId_SkipsSecondProfileAndLogsWarning()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Launcher:Apps:0:Id"] = "dup",
                ["Launcher:Apps:0:Name"] = "First",
                ["Launcher:Apps:0:ExePath"] = @"C:\a.exe",
                ["Launcher:Apps:1:Id"] = "dup",
                ["Launcher:Apps:1:Name"] = "Second",
                ["Launcher:Apps:1:ExePath"] = @"C:\b.exe",
            })
            .Build();
        var processCleanup = new Mock<IProcessCleanupService>();
        var logger = new Mock<ILogger<GameLauncherViewModel>>();

        var sut = BuildSut(processCleanup, logger.Object, config);

        Assert.Single(sut.Apps);
        Assert.Equal("First", sut.Apps[0].Name);
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
    public void Constructor_TwoBlankIdProfilesWithSameName_SkipsSecondProfileAndLogsWarning()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Launcher:Apps:0:Name"] = "Same Name",
                ["Launcher:Apps:0:ExePath"] = @"C:\a.exe",
                ["Launcher:Apps:1:Name"] = "Same Name",
                ["Launcher:Apps:1:ExePath"] = @"C:\b.exe",
            })
            .Build();
        var processCleanup = new Mock<IProcessCleanupService>();

        var sut = BuildSut(processCleanup, config: config);

        Assert.Single(sut.Apps);
        Assert.Equal(@"C:\a.exe", sut.Apps[0].ExePath);
    }

    [Fact]
    public void Constructor_UniqueIds_AddsAllProfiles()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Launcher:Apps:0:Id"] = "one",
                ["Launcher:Apps:0:Name"] = "First",
                ["Launcher:Apps:0:ExePath"] = @"C:\a.exe",
                ["Launcher:Apps:1:Id"] = "two",
                ["Launcher:Apps:1:Name"] = "Second",
                ["Launcher:Apps:1:ExePath"] = @"C:\b.exe",
            })
            .Build();
        var processCleanup = new Mock<IProcessCleanupService>();

        var sut = BuildSut(processCleanup, config: config);

        Assert.Equal(2, sut.Apps.Count);
    }

    [Fact]
    public void Constructor_SteamGameProfileWithoutProcessNames_IsIgnoredAndLogsWarning()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Launcher:Apps:0:Id"] = "cs2",
                ["Launcher:Apps:0:Name"] = "CS2",
                ["Launcher:Apps:0:Type"] = "SteamGame",
                ["Launcher:Apps:0:ExePath"] = @"C:\Steam\Steam.exe",
                ["Launcher:Apps:0:SteamAppId"] = "730",
            })
            .Build();
        var processCleanup = new Mock<IProcessCleanupService>();
        var logger = new Mock<ILogger<GameLauncherViewModel>>();

        var sut = BuildSut(processCleanup, logger.Object, config);

        Assert.Empty(sut.Apps);
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
    public void Constructor_SteamGameProfileWithProcessNames_IsAdded()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Launcher:Apps:0:Id"] = "cs2",
                ["Launcher:Apps:0:Name"] = "CS2",
                ["Launcher:Apps:0:Type"] = "SteamGame",
                ["Launcher:Apps:0:ExePath"] = @"C:\Steam\Steam.exe",
                ["Launcher:Apps:0:SteamAppId"] = "730",
                ["Launcher:Apps:0:ProcessNames:0"] = "cs2",
            })
            .Build();
        var processCleanup = new Mock<IProcessCleanupService>();

        var sut = BuildSut(processCleanup, config: config);

        Assert.Single(sut.Apps);
    }
}
