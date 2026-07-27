namespace ClubPay.Agent.Core;

public static class QrUrlBuilder
{
    public static string BuildLockScreenUrl(string baseUrl, string externalPcId) =>
        $"{baseUrl.TrimEnd('/')}/{Uri.EscapeDataString(externalPcId)}";
}
