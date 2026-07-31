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
        public const int ToastDurationSeconds = 6;  // 5-daq тост avtomatik yopilguncha
    }

    public static class Money
    {
        public const long TiyinPerSom = 100;
        public static string FormatSom(long tiyin) => MoneyFormatter.Format(tiyin);
    }

    public static class PcId
    {
        public const string Prefix = "PC-";
        public static string Format(int number) => $"{Prefix}{number}";
    }

    public static class Voucher
    {
        public const int DefaultTtlDays = 30;
    }

    public static class Wifi
    {
        public const string DefaultSsid = "ClubPay-Guest";
        public const string DefaultPassword = "";
    }

    public static class Qr
    {
        public const string PaymentBaseUrl = "https://clubpay.justix.uz/qr";
    }

    /// <summary>Controller channel (outbound WebSocket) — agent-initiated per contract §1.</summary>
    public static class ControllerChannel
    {
        public static class MessageType
        {
            public const string Command = "command";
            public const string CommandResult = "command_result";
            public const string Event = "event";
            public const string EventAck = "event_ack";
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
            public const string ManagerUnlock = "manager_unlock"; // ТЗ §7/§11 audit — contract extension
        }

        public const int HeartbeatIntervalSeconds = 30;
        public const int ReconnectBaseDelaySeconds = 1;
        public const int ReconnectMaxDelaySeconds = 30;
        public const int ReconnectMaxJitterMs = 500;
        public const int SendTimeoutSeconds = 10;   // matches Controller-side 10s ack expectation
        public const int MaxSendRetries = 3;        // outbox flush attempts only, never a live command reply

        // Telemetry (heartbeat/pc_state_changed) is coalesced to 1 pending instance per type, so this
        // limit in practice only guards against an unusual pileup of money/session events (contract §9:
        // those are never auto-dropped, only warned about).
        public const int MaxOutboxSize = 500;

        /// <summary>Telemetry is coalesced in-memory and never awaits an event_ack — losing/superseding
        /// it is harmless since the next one lands within HeartbeatIntervalSeconds. Every other event
        /// name is durable (money/session) and goes through the ack-and-retry path.</summary>
        public static bool IsTelemetryEvent(string name) =>
            name is EventName.Heartbeat or EventName.PcStateChanged;
    }

    /// <summary>Bounds enforced by ICommandValidator on incoming controller command payloads — not
    /// wire-contract values, purely a defensive ceiling against malformed/malicious duration fields.</summary>
    public static class SessionCommand
    {
        public const int MaxGrantedSeconds = 86_400;  // 24 soat
        public const int MaxAddedSeconds = 86_400;    // 24 soat
    }
}
