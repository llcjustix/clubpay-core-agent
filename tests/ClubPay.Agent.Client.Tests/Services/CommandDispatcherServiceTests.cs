using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ClubPay.Agent.Client.Services;
using ClubPay.Agent.Core;
using ClubPay.Agent.Core.Contracts;
using ClubPay.Agent.Core.Contracts.Enums;
using ClubPay.Agent.Core.Contracts.Events;
using ClubPay.Agent.Core.Contracts.Payloads;
using ClubPay.Agent.Core.Exceptions;
using ClubPay.Agent.Core.Services;

namespace ClubPay.Agent.Client.Tests.Services;

public class CommandDispatcherServiceTests
{
    private sealed class Mocks
    {
        public Mock<ISessionCoordinator> Coordinator { get; } = new();
        public Mock<IControllerChannel> Channel { get; } = new();
        public Mock<IAgentService> Agent { get; } = new();

        public Mocks()
        {
            Channel.Setup(c => c.PublishEventAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Agent.SetupGet(a => a.ExternalPcId).Returns("club12-pc07");
        }

        public CommandDispatcherService BuildSut() => new(Coordinator.Object, Channel.Object, Agent.Object,
            NullLogger<CommandDispatcherService>.Instance);
    }

    private static CommandEnvelope MakeGetStatusCommand(string commandId = "cmd_1") =>
        new(Constants.ControllerChannel.MessageType.Command, Constants.ControllerChannel.CommandName.GetStatus, commandId, DateTime.UtcNow, null);

    [Fact]
    public async Task DispatchAsync_WhenSessionCommandExceptionThrown_PublishesCommandFailedWithMappedErrorCode()
    {
        var m = new Mocks();
        m.Coordinator.Setup(c => c.GetStatus()).Throws(new SessionCommandException(ErrorCode.PcBusy, "busy"));
        var sut = m.BuildSut();
        var command = MakeGetStatusCommand();

        var result = await sut.DispatchAsync(command);

        Assert.Equal("error", result.Status);
        Assert.Equal(ErrorCode.PcBusy, result.ErrorCode);
        m.Channel.Verify(c => c.PublishEventAsync(
            "command_failed",
            It.Is<object>(o => ((CommandFailedEvent)o).CommandId == command.CommandId
                && ((CommandFailedEvent)o).ExternalPcId == "club12-pc07"
                && ((CommandFailedEvent)o).ErrorCode == ErrorCode.PcBusy),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchAsync_WhenUnexpectedExceptionThrown_PublishesCommandFailedWithInternalError()
    {
        var m = new Mocks();
        m.Coordinator.Setup(c => c.GetStatus()).Throws(new InvalidOperationException("boom"));
        var sut = m.BuildSut();
        var command = MakeGetStatusCommand();

        var result = await sut.DispatchAsync(command);

        Assert.Equal("error", result.Status);
        Assert.Equal(ErrorCode.InternalError, result.ErrorCode);
        m.Channel.Verify(c => c.PublishEventAsync(
            "command_failed",
            It.Is<object>(o => ((CommandFailedEvent)o).ErrorCode == ErrorCode.InternalError),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchAsync_WhenSuccessful_DoesNotPublishCommandFailed()
    {
        var m = new Mocks();
        m.Coordinator.Setup(c => c.GetStatus()).Returns(new GetStatusResult(PcState.Free, null, null, null));
        var sut = m.BuildSut();

        var result = await sut.DispatchAsync(MakeGetStatusCommand());

        Assert.Equal("ok", result.Status);
        m.Channel.Verify(c => c.PublishEventAsync(
            It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
