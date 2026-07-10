using ClubPay.Agent.Core.Contracts;
using ClubPay.Agent.Core.Contracts.Enums;
using ClubPay.Agent.Core.Models;

namespace ClubPay.Agent.Core.Tests.Contracts;

public class PcStateMapperTests
{
    [Fact]
    public void ToWireState_WhenRepairMode_ReturnsRepairRegardlessOfOtherFlags()
    {
        var result = PcStateMapper.ToWireState(
            AgentState.Active, isManagerLocked: true, isRepairMode: true, isAsleep: true, isConnected: false);

        Assert.Equal(PcState.Repair, result);
    }

    [Fact]
    public void ToWireState_WhenDisconnected_ReturnsOffline()
    {
        var result = PcStateMapper.ToWireState(
            AgentState.Locked, isManagerLocked: false, isRepairMode: false, isAsleep: false, isConnected: false);

        Assert.Equal(PcState.Offline, result);
    }

    [Fact]
    public void ToWireState_WhenAsleep_ReturnsSleeping()
    {
        var result = PcStateMapper.ToWireState(
            AgentState.Locked, isManagerLocked: false, isRepairMode: false, isAsleep: true, isConnected: true);

        Assert.Equal(PcState.Sleeping, result);
    }

    [Fact]
    public void ToWireState_WhenManagerLockedAndOtherwiseLocked_ReturnsAttention()
    {
        var result = PcStateMapper.ToWireState(
            AgentState.Locked, isManagerLocked: true, isRepairMode: false, isAsleep: false, isConnected: true);

        Assert.Equal(PcState.Attention, result);
    }

    [Fact]
    public void ToWireState_WhenManagerLockedDuringActiveSession_SessionStateWins()
    {
        var result = PcStateMapper.ToWireState(
            AgentState.Active, isManagerLocked: true, isRepairMode: false, isAsleep: false, isConnected: true);

        Assert.Equal(PcState.Occupied, result);
    }

    [Theory]
    [InlineData(AgentState.Locked, PcState.Free)]
    [InlineData(AgentState.Active, PcState.Occupied)]
    [InlineData(AgentState.Frozen, PcState.Frozen)]
    public void ToWireState_WhenNormalOperation_MapsSessionStateDirectly(AgentState state, PcState expected)
    {
        var result = PcStateMapper.ToWireState(
            state, isManagerLocked: false, isRepairMode: false, isAsleep: false, isConnected: true);

        Assert.Equal(expected, result);
    }
}
