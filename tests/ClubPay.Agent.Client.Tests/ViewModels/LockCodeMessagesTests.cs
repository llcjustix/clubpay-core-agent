using ClubPay.Agent.Client.ViewModels;
using ClubPay.Agent.Core.Contracts.Enums;

namespace ClubPay.Agent.Client.Tests.ViewModels;

public class LockCodeMessagesTests
{
    [Theory]
    [InlineData(LockCodeRejectionReason.InvalidCode)]
    [InlineData(LockCodeRejectionReason.Expired)]
    [InlineData(LockCodeRejectionReason.WrongPc)]
    [InlineData(LockCodeRejectionReason.AlreadyUsed)]
    [InlineData(LockCodeRejectionReason.PcBusy)]
    [InlineData(LockCodeRejectionReason.ServiceUnavailable)]
    public void For_EveryRejectionReason_ReturnsNonEmptyNonTechnicalMessage(LockCodeRejectionReason reason)
    {
        var message = LockCodeMessages.For(reason);

        Assert.False(string.IsNullOrWhiteSpace(message));
        Assert.DoesNotContain("Exception", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(nameof(reason), message);
    }

    [Fact]
    public void For_NullReason_FallsBackToInvalidCodeMessage()
    {
        Assert.Equal(LockCodeMessages.For(LockCodeRejectionReason.InvalidCode), LockCodeMessages.For(null));
    }
}
