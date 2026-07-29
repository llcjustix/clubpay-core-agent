using ClubPay.Agent.Admin.Services.Controller;
using ClubPay.Agent.Core.Contracts.Enums;
using ClubPay.Agent.Core.Models;

namespace ClubPay.Agent.Admin.Tests;

public class PcCardMapperTests
{
    private static PcLiveState State(PcState pcState, int? remainingSeconds = null) => new()
    {
        ExternalPcId = "club12-pc01",
        PcId = "PC-01",
        Zone = ZoneType.Standard,
        PcState = pcState,
        RemainingSeconds = remainingSeconds,
    };

    [Theory]
    [InlineData(PcState.Offline, PcStatus.Offline)]
    [InlineData(PcState.Free, PcStatus.Free)]
    [InlineData(PcState.Occupied, PcStatus.Active)]
    [InlineData(PcState.Frozen, PcStatus.Frozen)]
    [InlineData(PcState.Sleeping, PcStatus.Sleeping)]
    [InlineData(PcState.Repair, PcStatus.Repair)]
    [InlineData(PcState.Attention, PcStatus.Attention)]
    public void Apply_MapsWirePcStateToUiStatus(PcState wireState, PcStatus expected)
    {
        var card = new PcCard { PcId = "PC-01", ExternalPcId = "club12-pc01", Zone = ZoneType.Standard };

        PcCardMapper.Apply(card, State(wireState));

        Assert.Equal(expected, card.Status);
    }

    [Fact]
    public void Apply_Occupied_WithRemainingSeconds_FormatsCountdown()
    {
        var card = new PcCard { PcId = "PC-01", ExternalPcId = "club12-pc01", Zone = ZoneType.Standard };

        PcCardMapper.Apply(card, State(PcState.Occupied, remainingSeconds: 3720)); // 1h02m

        Assert.Equal("01:02 qoldi", card.StatusText);
    }

    [Fact]
    public void Apply_Frozen_WithoutRemainingSeconds_ShowsWaitingForPayment()
    {
        var card = new PcCard { PcId = "PC-01", ExternalPcId = "club12-pc01", Zone = ZoneType.Standard };

        PcCardMapper.Apply(card, State(PcState.Frozen));

        Assert.Equal("To'lov kutilmoqda", card.StatusText);
    }

    [Fact]
    public void Apply_Free_ShowsBoshLabel()
    {
        var card = new PcCard { PcId = "PC-01", ExternalPcId = "club12-pc01", Zone = ZoneType.Standard };

        PcCardMapper.Apply(card, State(PcState.Free));

        Assert.Equal("Bo'sh", card.StatusText);
    }
}
