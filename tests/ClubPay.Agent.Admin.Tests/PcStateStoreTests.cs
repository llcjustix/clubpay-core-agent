using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ClubPay.Agent.Admin.Services.Controller;
using ClubPay.Agent.Core;
using ClubPay.Agent.Core.Contracts;
using ClubPay.Agent.Core.Contracts.Enums;
using ClubPay.Agent.Core.Contracts.Events;
using ClubPay.Agent.Core.Models;

namespace ClubPay.Agent.Admin.Tests;

public class PcStateStoreTests
{
    private const string ExternalPcId = "club12-pc01";

    private static PcStateStore BuildStore()
    {
        var registry = new FakeRegistry(new PcRegistryEntry(ExternalPcId, "PC-01", ZoneType.Standard, "token"));
        return new PcStateStore(registry, NullLogger<PcStateStore>.Instance);
    }

    private static EventEnvelope Envelope(string name, object payload) => new(
        Constants.ControllerChannel.MessageType.Event,
        name,
        "ev_" + Guid.NewGuid().ToString("N")[..8],
        DateTime.UtcNow,
        JsonSerializer.SerializeToElement(payload, ControllerJsonOptions.Default));

    [Fact]
    public void InitialState_IsOfflineAndDisconnected()
    {
        var store = BuildStore();

        var state = store.Get(ExternalPcId);

        Assert.NotNull(state);
        Assert.False(state!.IsConnected);
        Assert.Equal(PcState.Offline, state.PcState);
    }

    [Fact]
    public void MarkConnected_SetsConnectedAndFree()
    {
        var store = BuildStore();

        store.MarkConnected(ExternalPcId);

        var state = store.Get(ExternalPcId)!;
        Assert.True(state.IsConnected);
        Assert.Equal(PcState.Free, state.PcState);
    }

    [Fact]
    public void MarkDisconnected_SetsOfflineRegardlessOfPriorState()
    {
        var store = BuildStore();
        store.MarkConnected(ExternalPcId);
        store.ApplyEvent(ExternalPcId, Envelope(
            Constants.ControllerChannel.EventName.SessionStarted,
            new SessionStartedEvent("core-1", ExternalPcId, "grant-1", null, DateTime.UtcNow)));

        store.MarkDisconnected(ExternalPcId);

        var state = store.Get(ExternalPcId)!;
        Assert.False(state.IsConnected);
        Assert.Equal(PcState.Offline, state.PcState);
    }

    [Fact]
    public void ApplyEvent_SessionStarted_SetsOccupiedAndCoreSessionId()
    {
        var store = BuildStore();
        store.MarkConnected(ExternalPcId);

        store.ApplyEvent(ExternalPcId, Envelope(
            Constants.ControllerChannel.EventName.SessionStarted,
            new SessionStartedEvent("core-42", ExternalPcId, "grant-1", "order-1", DateTime.UtcNow)));

        var state = store.Get(ExternalPcId)!;
        Assert.Equal(PcState.Occupied, state.PcState);
        Assert.Equal(SessionState.Active, state.SessionState);
        Assert.Equal("core-42", state.CoreSessionId);
    }

    [Fact]
    public void ApplyEvent_SessionEnded_ReturnsPcToFree()
    {
        var store = BuildStore();
        store.MarkConnected(ExternalPcId);
        store.ApplyEvent(ExternalPcId, Envelope(
            Constants.ControllerChannel.EventName.SessionStarted,
            new SessionStartedEvent("core-42", ExternalPcId, "grant-1", null, DateTime.UtcNow)));

        store.ApplyEvent(ExternalPcId, Envelope(
            Constants.ControllerChannel.EventName.SessionEnded,
            new SessionEndedEvent("core-42", ExternalPcId, EndReason.Manager, 100, 0)));

        var state = store.Get(ExternalPcId)!;
        Assert.Equal(PcState.Free, state.PcState);
        Assert.Equal(SessionState.Ended, state.SessionState);
    }

    [Fact]
    public void ApplyEvent_TimeLow_UpdatesRemainingSeconds()
    {
        var store = BuildStore();
        store.MarkConnected(ExternalPcId);

        store.ApplyEvent(ExternalPcId, Envelope(
            Constants.ControllerChannel.EventName.TimeLow,
            new TimeLowEvent("core-42", 300, 300)));

        Assert.Equal(300, store.Get(ExternalPcId)!.RemainingSeconds);
    }

    [Fact]
    public void ApplyEvent_UnknownExternalPcId_IsIgnored()
    {
        var store = BuildStore();

        store.ApplyEvent("not-registered", Envelope(
            Constants.ControllerChannel.EventName.AgentOnline, new AgentOnlineEvent("not-registered")));

        Assert.Null(store.Get("not-registered"));
    }

    [Fact]
    public void Changed_IsRaisedWithAffectedExternalPcId()
    {
        var store = BuildStore();
        string? raisedFor = null;
        store.Changed += id => raisedFor = id;

        store.MarkConnected(ExternalPcId);

        Assert.Equal(ExternalPcId, raisedFor);
    }

    private sealed class FakeRegistry(params PcRegistryEntry[] entries) : IPcRegistry
    {
        public IReadOnlyList<PcRegistryEntry> All { get; } = entries;

        public bool TryGet(string externalPcId, out PcRegistryEntry entry)
        {
            var found = All.FirstOrDefault(e => e.ExternalPcId == externalPcId);
            entry = found!;
            return found is not null;
        }

        public bool ValidateToken(string externalPcId, string? token) =>
            TryGet(externalPcId, out var entry) && entry.AgentToken == token;
    }
}
