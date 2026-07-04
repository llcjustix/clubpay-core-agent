namespace ClubPay.Agent.Core;

public static class Constants
{
    public static class Timer
    {
        public const int WarnAt10Min = 600;
        public const int WarnAt5Min = 300;
        public const int WarnAt1Min = 60;
        public const int GracePeriod = 600;  // 10 min freeze grace
        public const int IdleSleep = 600;  // 10 min idle → S3
    }

    public static class Money
    {
        public const long TiyinPerSom = 100;
        public static long SomToTiyin(decimal som) => (long)(som * TiyinPerSom);
        public static decimal TiyinToSom(long tiyin) => tiyin / (decimal)TiyinPerSom;
        public static string FormatSom(long tiyin) => $"{TiyinToSom(tiyin):N0} so'm";
    }

    public static class PcId
    {
        public const string Prefix = "PC-";
        public static string Format(int number) => $"{Prefix}{number}";
    }

    public static class Voucher
    {
        public const int DefaultTtlDays = 30;
        public const int MinRemainingSeconds = 300;  // 5 min minimum to issue voucher
    }

    public static class Wifi
    {
        public const string DefaultSsid = "ClubPay-Guest";
        public const string DefaultPassword = "";
    }

    public static class Controller
    {
        public const int Port = 7474;
        public const string SecretHeader = "X-Agent-Secret";
        public const string PendingSessionPath = "/api/agents/{0}/pending-session";  // {0} = pcId
        public const int StartupCheckMaxRetries = 3;
    }

    public static class Billing
    {
        public const int ListenerPort = 7475;
        public const string EventsPath = "/api/core/events";
        public const int EventReportMaxRetries = 3;

        public static class EventType
        {
            public const string PcStatusChanged = "pc_status_changed";
            public const string SessionStarted = "session_started";
            public const string SessionEnded = "session_ended";
            public const string SessionFailed = "session_failed";
            public const string CommandFailed = "command_failed";
        }
    }
}
