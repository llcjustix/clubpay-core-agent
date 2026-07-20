namespace ClubPay.Agent.Core;

public static class SessionWarningCalculator
{
    public static bool IsBannerVisible(int remainingSeconds) =>
        remainingSeconds is > 0 and <= Constants.Timer.WarnAt1Min;

    public static bool IsAnyWarningVisible(int remainingSeconds) =>
        remainingSeconds is > 0 and <= Constants.Timer.WarnAt10Min;

    public static bool ShouldShowToast(int previousRemainingSeconds, int currentRemainingSeconds) =>
        previousRemainingSeconds > Constants.Timer.WarnAt5Min
        && currentRemainingSeconds <= Constants.Timer.WarnAt5Min
        && currentRemainingSeconds > 0;
}
