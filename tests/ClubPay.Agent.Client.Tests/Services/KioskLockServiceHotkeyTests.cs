using ClubPay.Agent.Client.Services;
using ClubPay.Agent.Core.Services;

namespace ClubPay.Agent.Client.Tests.Services;

public class KioskLockServiceHotkeyTests
{
    private const uint VkF9 = 0x78;
    private const uint VkOther = 0x41; // 'A'
    private const int KeyDown = 0x0100;
    private const int KeyUp = 0x0101;

    [Fact]
    public void TryHandleShellHotkey_FullChordDownDuringSession_FiresOnceAndConsumesEvent()
    {
        var sut = new KioskLockService();
        int fired = 0;
        sut.ShellToggleRequested += () => fired++;

        bool consumed = sut.TryHandleShellHotkey(VkF9, KeyDown, ctrl: true, shft: true, KioskLockMode.Session);

        Assert.True(consumed);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void TryHandleShellHotkey_RepeatedKeyDownWhileHeld_DoesNotRefire()
    {
        var sut = new KioskLockService();
        int fired = 0;
        sut.ShellToggleRequested += () => fired++;

        sut.TryHandleShellHotkey(VkF9, KeyDown, ctrl: true, shft: true, KioskLockMode.Session);
        sut.TryHandleShellHotkey(VkF9, KeyDown, ctrl: true, shft: true, KioskLockMode.Session); // OS auto-repeat
        sut.TryHandleShellHotkey(VkF9, KeyDown, ctrl: true, shft: true, KioskLockMode.Session);

        Assert.Equal(1, fired);
    }

    [Fact]
    public void TryHandleShellHotkey_FullModeNotSession_DoesNotFireAndDoesNotConsume()
    {
        var sut = new KioskLockService();
        int fired = 0;
        sut.ShellToggleRequested += () => fired++;

        bool consumed = sut.TryHandleShellHotkey(VkF9, KeyDown, ctrl: true, shft: true, KioskLockMode.Full);

        Assert.False(consumed);
        Assert.Equal(0, fired);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void TryHandleShellHotkey_OnlyOneOrNoModifierHeld_DoesNotFireAndDoesNotConsume(bool ctrl, bool shft)
    {
        var sut = new KioskLockService();
        int fired = 0;
        sut.ShellToggleRequested += () => fired++;

        bool consumed = sut.TryHandleShellHotkey(VkF9, KeyDown, ctrl, shft, KioskLockMode.Session);

        Assert.False(consumed); // plain/partial F9 press must reach the game untouched
        Assert.Equal(0, fired);
    }

    [Fact]
    public void TryHandleShellHotkey_KeyUpAfterConsumedChord_RearmsForNextFire()
    {
        var sut = new KioskLockService();
        int fired = 0;
        sut.ShellToggleRequested += () => fired++;

        sut.TryHandleShellHotkey(VkF9, KeyDown, ctrl: true, shft: true, KioskLockMode.Session);
        bool upConsumed = sut.TryHandleShellHotkey(VkF9, KeyUp, ctrl: true, shft: true, KioskLockMode.Session);
        bool downConsumed = sut.TryHandleShellHotkey(VkF9, KeyDown, ctrl: true, shft: true, KioskLockMode.Session);

        Assert.True(upConsumed);
        Assert.True(downConsumed);
        Assert.Equal(2, fired);
    }

    [Fact]
    public void TryHandleShellHotkey_KeyUpForUnconsumedPlainF9_DoesNotConsume()
    {
        var sut = new KioskLockService();

        // Plain F9 down (no modifiers) was never consumed, so its matching up must pass through too.
        bool upConsumed = sut.TryHandleShellHotkey(VkF9, KeyUp, ctrl: false, shft: false, KioskLockMode.Session);

        Assert.False(upConsumed);
    }

    [Fact]
    public void TryHandleShellHotkey_UnrelatedVkCode_IgnoredEvenWithChordHeld()
    {
        var sut = new KioskLockService();
        int fired = 0;
        sut.ShellToggleRequested += () => fired++;

        bool consumed = sut.TryHandleShellHotkey(VkOther, KeyDown, ctrl: true, shft: true, KioskLockMode.Session);

        Assert.False(consumed);
        Assert.Equal(0, fired);
    }
}
