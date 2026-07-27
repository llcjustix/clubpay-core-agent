using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ClubPay.Agent.Core;
using ClubPay.Agent.Core.Contracts;
using ClubPay.Agent.Core.Models;
using ClubPay.Agent.Core.Services;

namespace ClubPay.Agent.Client.Services;

/// <summary>
/// Local, durable state for one Agent. SQLite keeps the Agent offline-capable without making it an
/// authority: sessions, replay keys and durable outbox events survive restarts on this PC only.
/// </summary>
public sealed class AgentStateRepository : ISessionStore, IGrantIdempotencyStore, IControllerOutbox, IAtomicAgentStateStore, IDisposable
{
    private static readonly TimeSpan GrantRetention = TimeSpan.FromDays(30);
    private readonly string _databasePath;
    private readonly string _legacyJsonPath;
    private readonly ILogger<AgentStateRepository> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _sendSignal = new(0);
    private readonly Dictionary<string, EventEnvelope> _telemetryPending = new();
    private bool _initialized;
    private bool _loaded;

    public Session? Current { get; private set; }

    public AgentStateRepository(IConfiguration config, ILogger<AgentStateRepository> logger)
    {
        _logger = logger;
        var dataDir = config["Agent:DataDirectory"];
        if (string.IsNullOrWhiteSpace(dataDir))
            dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ClubPay", "Agent", "state");
        Directory.CreateDirectory(dataDir);
        _databasePath = Path.Combine(dataDir, "agent-state.db");
        _legacyJsonPath = Path.Combine(dataDir, "agent-state.json");
    }

    public async Task<Session?> LoadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await EnsureInitializedAsync(ct);
            _loaded = true;
            Current = await ReadSessionAsync(ct);
            return Current;
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(Session session, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await EnsureLoadedAsync(ct);
            await using var connection = await OpenConnectionAsync(ct);
            await UpsertSessionAsync(connection, null, session, ct);
            Current = session;
        }
        finally { _gate.Release(); }
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await EnsureLoadedAsync(ct);
            await using var connection = await OpenConnectionAsync(ct);
            await DeleteSessionAsync(connection, null, ct);
            Current = null;
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> HasAppliedAsync(string grantId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await EnsureLoadedAsync(ct);
            await using var connection = await OpenConnectionAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT EXISTS(SELECT 1 FROM applied_ids WHERE id = $id);";
            command.Parameters.AddWithValue("$id", grantId);
            return Convert.ToInt64(await command.ExecuteScalarAsync(ct)) != 0;
        }
        finally { _gate.Release(); }
    }

    public async Task RecordAppliedAsync(string grantId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await EnsureLoadedAsync(ct);
            await using var connection = await OpenConnectionAsync(ct);
            await using var transaction = connection.BeginTransaction();
            await InsertAppliedIdAsync(connection, transaction, grantId, DateTime.UtcNow, ct);
            await PruneAppliedIdsAsync(connection, transaction, ct);
            await transaction.CommitAsync(ct);
        }
        finally { _gate.Release(); }
    }

    public async Task PublishEventAsync(string eventName, object payload, CancellationToken ct = default)
    {
        var evt = new EventEnvelope(Constants.ControllerChannel.MessageType.Event, eventName, "ev_" + Guid.NewGuid().ToString("N"), DateTime.UtcNow, payload);
        await _gate.WaitAsync(ct);
        try
        {
            if (IsTelemetry(eventName))
                _telemetryPending[eventName] = evt;
            else
            {
                await EnsureLoadedAsync(ct);
                if (await CountOutboxAsync(ct) >= Constants.ControllerChannel.MaxOutboxSize)
                    _logger.LogWarning("Outbox hajmi limitdan oshdi: {Count}/{Max} — pul/sessiya eventlari yo'qotilmaydi, tekshiring", await CountOutboxAsync(ct) + 1, Constants.ControllerChannel.MaxOutboxSize);
                await using var connection = await OpenConnectionAsync(ct);
                await InsertOutboxAsync(connection, null, evt, ct);
            }
        }
        finally { _gate.Release(); }
        _sendSignal.Release();
    }

    public async Task EnqueueAsync(EventEnvelope evt, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await EnsureLoadedAsync(ct);
            await using var connection = await OpenConnectionAsync(ct);
            await InsertOutboxAsync(connection, null, evt, ct);
        }
        finally { _gate.Release(); }
        _sendSignal.Release();
    }

    public async Task<IReadOnlyList<EventEnvelope>> GetPendingAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await EnsureLoadedAsync(ct);
            var pending = await ReadOutboxAsync(ct);
            pending.AddRange(_telemetryPending.Values);
            return pending;
        }
        finally { _gate.Release(); }
    }

    public async Task MarkSentAsync(string eventId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await EnsureLoadedAsync(ct);
            await using var connection = await OpenConnectionAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM outbox WHERE event_id = $eventId;";
            command.Parameters.AddWithValue("$eventId", eventId);
            if (await command.ExecuteNonQueryAsync(ct) == 0)
            {
                var key = _telemetryPending.FirstOrDefault(x => x.Value.EventId == eventId).Key;
                if (key is not null) _telemetryPending.Remove(key);
            }
        }
        finally { _gate.Release(); }
    }

    public Task WaitForPendingAsync(TimeSpan timeout, CancellationToken ct = default) => _sendSignal.WaitAsync(timeout, ct);

    public Task CommitStartAsync(Session session, string grantId, EventEnvelope sessionStarted, CancellationToken ct = default) => CommitGrantAsync(session, grantId, sessionStarted, ct);
    public Task CommitExtendAsync(Session session, string grantId, EventEnvelope sessionExtended, CancellationToken ct = default) => CommitGrantAsync(session, grantId, sessionExtended, ct);

    public async Task CommitEndAsync(EventEnvelope sessionEnded, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await EnsureLoadedAsync(ct);
            await using var connection = await OpenConnectionAsync(ct);
            await using var transaction = connection.BeginTransaction();
            await DeleteSessionAsync(connection, transaction, ct);
            await InsertOutboxAsync(connection, transaction, sessionEnded, ct);
            await transaction.CommitAsync(ct);
            Current = null;
        }
        finally { _gate.Release(); }
        _sendSignal.Release();
    }

    private async Task CommitGrantAsync(Session session, string grantId, EventEnvelope evt, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await EnsureLoadedAsync(ct);
            await using var connection = await OpenConnectionAsync(ct);
            await using var transaction = connection.BeginTransaction();
            await UpsertSessionAsync(connection, transaction, session, ct);
            await InsertAppliedIdAsync(connection, transaction, grantId, DateTime.UtcNow, ct);
            await PruneAppliedIdsAsync(connection, transaction, ct);
            await InsertOutboxAsync(connection, transaction, evt, ct);
            await transaction.CommitAsync(ct);
            Current = session;
        }
        finally { _gate.Release(); }
        _sendSignal.Release();
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_loaded) return;
        await EnsureInitializedAsync(ct);
        _loaded = true;
        Current = await ReadSessionAsync(ct);
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return;
        await using var connection = await OpenConnectionAsync(ct);
        var version = await GetSchemaVersionAsync(connection, ct);
        if (version > 1)
            throw new InvalidOperationException($"Agent state DB schema {version} bu agent versiyasidan yangi");
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                PRAGMA journal_mode=WAL;
                PRAGMA synchronous=FULL;
                PRAGMA foreign_keys=ON;
                CREATE TABLE IF NOT EXISTS current_session (id INTEGER PRIMARY KEY CHECK(id = 1), payload_json TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS applied_ids (id TEXT PRIMARY KEY, applied_at_utc TEXT NOT NULL);
                CREATE INDEX IF NOT EXISTS ix_applied_ids_applied_at ON applied_ids(applied_at_utc);
                CREATE TABLE IF NOT EXISTS outbox (sequence INTEGER PRIMARY KEY AUTOINCREMENT, event_id TEXT NOT NULL UNIQUE, payload_json TEXT NOT NULL);
                """;
            await command.ExecuteNonQueryAsync(ct);
        }
        if (version == 0)
        {
            await using var versionCommand = connection.CreateCommand();
            versionCommand.CommandText = "PRAGMA user_version = 1;";
            await versionCommand.ExecuteNonQueryAsync(ct);
        }
        if (await IsDatabaseEmptyAsync(connection, ct) && File.Exists(_legacyJsonPath))
            await ImportLegacyJsonAsync(connection, ct);
        _initialized = true;
    }

    private async Task ImportLegacyJsonAsync(SqliteConnection connection, CancellationToken ct)
    {
        PersistedState? state;
        try
        {
            await using var stream = File.OpenRead(_legacyJsonPath);
            state = await JsonSerializer.DeserializeAsync<PersistedState>(stream, ControllerJsonOptions.Default, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Eski Agent state JSON migratsiya qilinmadi ({Path}); fayl saqlandi", _legacyJsonPath);
            return;
        }
        if (state is null) return;
        await using var transaction = connection.BeginTransaction();
        if (state.Session is not null) await UpsertSessionAsync(connection, transaction, state.Session, ct);
        foreach (var grant in state.AppliedGrantIds) await InsertAppliedIdAsync(connection, transaction, grant.GrantId, grant.AppliedAtUtc, ct);
        foreach (var evt in state.Outbox) await InsertOutboxAsync(connection, transaction, evt, ct);
        await transaction.CommitAsync(ct);
        File.Move(_legacyJsonPath, _legacyJsonPath + ".migrated-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ".bak");
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _databasePath, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString());
        await connection.OpenAsync(ct);
        return connection;
    }

    private static async Task<bool> IsDatabaseEmptyAsync(SqliteConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT NOT EXISTS(SELECT 1 FROM current_session) AND NOT EXISTS(SELECT 1 FROM applied_ids) AND NOT EXISTS(SELECT 1 FROM outbox);";
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct)) != 0;
    }

    private static async Task<long> GetSchemaVersionAsync(SqliteConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct));
    }

    private async Task<Session?> ReadSessionAsync(CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload_json FROM current_session WHERE id = 1;";
        var payload = await command.ExecuteScalarAsync(ct) as string;
        return payload is null ? null : JsonSerializer.Deserialize<Session>(payload, ControllerJsonOptions.Default);
    }

    private async Task<List<EventEnvelope>> ReadOutboxAsync(CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload_json FROM outbox ORDER BY sequence;";
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<EventEnvelope>();
        while (await reader.ReadAsync(ct)) result.Add(JsonSerializer.Deserialize<EventEnvelope>(reader.GetString(0), ControllerJsonOptions.Default)!);
        return result;
    }

    private async Task<long> CountOutboxAsync(CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM outbox;";
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct));
    }

    private static async Task UpsertSessionAsync(SqliteConnection connection, SqliteTransaction? transaction, Session session, CancellationToken ct)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT INTO current_session(id, payload_json) VALUES(1, $payload) ON CONFLICT(id) DO UPDATE SET payload_json = excluded.payload_json;";
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(session, ControllerJsonOptions.Default));
        await command.ExecuteNonQueryAsync(ct);
    }
    private static async Task DeleteSessionAsync(SqliteConnection connection, SqliteTransaction? transaction, CancellationToken ct)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "DELETE FROM current_session WHERE id = 1;"; await command.ExecuteNonQueryAsync(ct);
    }
    private static async Task InsertOutboxAsync(SqliteConnection connection, SqliteTransaction? transaction, EventEnvelope evt, CancellationToken ct)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT INTO outbox(event_id, payload_json) VALUES($eventId, $payload);";
        command.Parameters.AddWithValue("$eventId", evt.EventId); command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(evt, ControllerJsonOptions.Default)); await command.ExecuteNonQueryAsync(ct);
    }
    private static async Task InsertAppliedIdAsync(SqliteConnection connection, SqliteTransaction transaction, string id, DateTime at, CancellationToken ct)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT OR IGNORE INTO applied_ids(id, applied_at_utc) VALUES($id, $at);";
        command.Parameters.AddWithValue("$id", id); command.Parameters.AddWithValue("$at", at.ToString("O")); await command.ExecuteNonQueryAsync(ct);
    }
    private static async Task PruneAppliedIdsAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken ct)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "DELETE FROM applied_ids WHERE applied_at_utc < $cutoff;";
        command.Parameters.AddWithValue("$cutoff", (DateTime.UtcNow - GrantRetention).ToString("O")); await command.ExecuteNonQueryAsync(ct);
    }
    private static bool IsTelemetry(string name) => name is Constants.ControllerChannel.EventName.Heartbeat or Constants.ControllerChannel.EventName.PcStateChanged;
    public void Dispose() { _gate.Dispose(); _sendSignal.Dispose(); }
    private sealed record PersistedState(Session? Session, List<AppliedGrant> AppliedGrantIds, List<EventEnvelope> Outbox);
    private sealed record AppliedGrant(string GrantId, DateTime AppliedAtUtc);
}
