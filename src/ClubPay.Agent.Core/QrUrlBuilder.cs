namespace ClubPay.Agent.Core;

public static class QrUrlBuilder
{
    public static string BuildLockScreenUrl(string baseUrl, string externalPcId) =>
        $"{baseUrl.TrimEnd('/')}/{Uri.EscapeDataString(externalPcId)}";

    public static string BuildSessionUrl(string baseUrl, string externalPcId, Guid? coreSessionId, Guid? localSessionId)
    {
        var sessionId = coreSessionId ?? localSessionId;
        var query = $"pc={Uri.EscapeDataString(externalPcId)}";
        if (sessionId is { } id)
            query += $"&session={id:N}";
        return $"{baseUrl.TrimEnd('/')}?{query}";
    }
}
