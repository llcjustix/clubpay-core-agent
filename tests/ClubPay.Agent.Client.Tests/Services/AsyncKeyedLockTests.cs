using ClubPay.Agent.Client.Services;

namespace ClubPay.Agent.Client.Tests.Services;

public class AsyncKeyedLockTests
{
    [Fact]
    public async Task AcquireAsync_SameKey_SerializesCallers()
    {
        var sut = new AsyncKeyedLock();
        var releaser1 = await sut.AcquireAsync("key");

        var secondAcquire = sut.AcquireAsync("key");
        await Task.Delay(50);
        Assert.False(secondAcquire.IsCompleted);

        releaser1.Dispose();
        var releaser2 = await secondAcquire.WaitAsync(TimeSpan.FromSeconds(5));
        releaser2.Dispose();
    }

    [Fact]
    public async Task AcquireAsync_DifferentKeys_DoNotBlockEachOther()
    {
        var sut = new AsyncKeyedLock();
        using var releaserA = await sut.AcquireAsync("a");

        var acquireB = sut.AcquireAsync("b");
        var completed = await Task.WhenAny(acquireB, Task.Delay(TimeSpan.FromSeconds(2)));

        Assert.Same(acquireB, completed);
        (await acquireB).Dispose();
    }

    [Fact]
    public async Task AcquireAsync_WhenCancelledWhileWaiting_DoesNotLeakEntry()
    {
        var sut = new AsyncKeyedLock();
        var holder = await sut.AcquireAsync("key");

        using var cts = new CancellationTokenSource();
        var waiter = sut.AcquireAsync("key", cts.Token);
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => waiter);

        holder.Dispose();

        // If the cancelled waiter had leaked its Entry's refcount, this would deadlock — bounded by
        // the test timeout below rather than hanging forever.
        var thirdAcquire = sut.AcquireAsync("key");
        var completed = await Task.WhenAny(thirdAcquire, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Same(thirdAcquire, completed);
        (await thirdAcquire).Dispose();
    }
}
