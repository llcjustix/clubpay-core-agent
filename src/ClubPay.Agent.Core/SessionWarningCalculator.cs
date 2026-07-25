namespace ClubPay.Agent.Core;

public static class SessionWarningCalculator
{
    public static bool IsBannerVisible(int remainingSeconds) =>
        remainingSeconds is > 0 and <= Constants.Timer.WarnAt1Min;

    public static bool IsAnyWarningVisible(int remainingSeconds) =>
        remainingSeconds is > 0 and <= Constants.Timer.WarnAt10Min;

    /// <summary>A negative previousRemainingSeconds means "no prior tick" (fresh sync/recovery) and
    /// counts as above-threshold, so a session that starts or resumes with ≤5 min still gets its one
    /// toast.</summary>
    public static bool ShouldShowToast(int previousRemainingSeconds, int currentRemainingSeconds) =>
        (previousRemainingSeconds > Constants.Timer.WarnAt5Min || previousRemainingSeconds < 0)
        && currentRemainingSeconds <= Constants.Timer.WarnAt5Min
        && currentRemainingSeconds > 0;
}
