using ClubPay.Agent.Admin.Services.Controller;

namespace ClubPay.Agent.Admin.Tests.Services;

public sealed class EventIdempotencyStoreTests
{
    [Fact]
    public void TryMarkProcessed_FirstCall_ReturnsTrue()
    {
        var store = new EventIdempotencyStore();

        Assert.True(store.TryMarkProcessed("ev_1"));
    }

    [Fact]
    public void TryMarkProcessed_SameIdTwice_SecondReturnsFalse()
    {
        var store = new EventIdempotencyStore();

        Assert.True(store.TryMarkProcessed("ev_1"));
        Assert.False(store.TryMarkProcessed("ev_1"));
    }

    [Fact]
    public void TryMarkProcessed_DifferentIds_BothReturnTrue()
    {
        var store = new EventIdempotencyStore();

        Assert.True(store.TryMarkProcessed("ev_1"));
        Assert.True(store.TryMarkProcessed("ev_2"));
    }
}
