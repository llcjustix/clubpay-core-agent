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

    /// <summary>Legacy LAN HTTP endpoint still served by ClubPay.Agent.Admin's AgentEndpointServer
    /// (pending-session polling) — kept as-is since Admin is out of scope for this phase; the Client no
    /// longer calls this (see Constants.ControllerChannel for the new WebSocket transport).</summary>
    public static class Controller
    {
        public const int Port = 7474;
        public const string SecretHeader = "X-Agent-Secret";
        public const string PendingSessionPath = "/api/agents/{0}/pending-session";  // {0} = pcId
        public const int StartupCheckMaxRetries = 3;
    }

    /// <summary>Controller channel (outbound WebSocket) — agent-initiated per contract §1.</summary>
    public static class ControllerChannel
    {
        public static class MessageType
        {
            public const string Command = "command";
            public const string CommandResult = "command_result";
            public const string Event = "event";
        }

        public static class CommandName
        {
            public const string StartSession = "start_session";
            public const string ExtendSession = "extend_session";
            public const string EndSession = "end_session";
            public const string Lock = "lock";
            public const string Unlock = "unlock";
            public const string Wake = "wake";
            public const string Sleep = "sleep";
            public const string SetRepair = "set_repair";
            public const string GetStatus = "get_status";
            public const string ApplyConfig = "apply_config"; // deferred — payment/tariff config, not handled
        }

        public static class EventName
        {
            public const string AgentOnline = "agent_online";
            public const string AgentOffline = "agent_offline"; // never sent by the agent itself — documented only
            public const string PcStateChanged = "pc_state_changed";
            public const string SessionStarted = "session_started";
            public const string SessionExtended = "session_extended";
            public const string SessionEnded = "session_ended";
            public const string CommandFailed = "command_failed";
            public const string TimeLow = "time_low";
            public const string Heartbeat = "heartbeat";
        }

        public const int HeartbeatIntervalSeconds = 30;
        public const int ReconnectBaseDelaySeconds = 1;
        public const int ReconnectMaxDelaySeconds = 30;
        public const int ReconnectMaxJitterMs = 500;
        public const int SendTimeoutSeconds = 10;   // matches Controller-side 10s ack expectation
        public const int MaxSendRetries = 3;        // outbox flush attempts only, never a live command reply
    }
}
