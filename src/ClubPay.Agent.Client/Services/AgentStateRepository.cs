using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ClubPay.Agent.Core;
using ClubPay.Agent.Core.Contracts;
using ClubPay.Agent.Core.Models;
using ClubPay.Agent.Core.Services;

namespace ClubPay.Agent.Client.Services;

/// <summary>
/// Backs ISessionStore, IGrantIdempotencyStore and IControllerOutbox from a single JSON file, written
/// atomically (write to .tmp then File.Move) so a crash never leaves a half-written state visible. All
/// three concerns share one file deliberately — a session-save and its grant-id record must never be
/// observed independently of each other after a crash.
/// </summary>
public sealed class AgentStateRepository : ISessionStore, IGrantIdempotencyStore, IControllerOutbox, IDisposable
{
    private static readonly TimeSpan GrantRetention = TimeSpan.FromDays(30);

    private readonly string _filePath;
    private readonly ILogger<AgentStateRepository> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _sendSignal = new(0);

    // Heartbeat/pc_state_changed reflect current state, not history — while offline they must not
    // accumulate on disk (contract §9: money/session events take priority), so they live only in
    // memory, keyed by event name (latest wins) and never touch _state/WriteToDiskAsync at all.
    private readonly Dictionary<string, EventEnvelope> _telemetryPending = new();

    private PersistedState _state = new(null, [], []);
    private bool _loaded;

    public Session? Current { get; private set; }

    public AgentStateRepository(IConfiguration config, ILogger<AgentStateRepository> logger)
    {
        _logger = logger;

        var dataDir = config["Agent:DataDirectory"];
        if (string.IsNullOrWhiteSpace(dataDir))
        {
            dataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "ClubPay", "Agent", "state");
        }

        Directory.CreateDirectory(dataDir);
        _filePath = Path.Combine(dataDir, "agent-state.json");
    }

    public async Task<Session?> LoadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            _state = await ReadFromDiskAsync(ct);
            _loaded = true;
            Current = _state.Session;
            return Current;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(Session session, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await EnsureLoadedAsync(ct);
            _state = _state with { Session = session };
            Current = session;
            await WriteToDiskAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await EnsureLoadedAsync(ct);
            _state = _state with { Session = null };
            Current = null;
            await WriteToDiskAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> HasAppliedAsync(string grantId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await EnsureLoadedAsync(ct);
            return _state.AppliedGrantIds.Exists(g => g.GrantId == grantId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RecordAppliedAsync(string grantId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await EnsureLoadedAsync(ct);

            var cutoff = DateTime.UtcNow - GrantRetention;
            var pruned = _state.AppliedGrantIds.FindAll(g => g.AppliedAtUtc >= cutoff);
            pruned.Add(new AppliedGrant(grantId, DateTime.UtcNow));

            _state = _state with { AppliedGrantIds = pruned };
            await WriteToDiskAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task PublishEventAsync(string eventName, object payload, CancellationToken ct = default)
    {
        var evt = new EventEnvelope(
            Constants.ControllerChannel.MessageType.Event, eventName, "ev_" + Guid.NewGuid().ToString("N"), DateTime.UtcNow, payload);

        var isTelemetry = eventName is Constants.ControllerChannel.EventName.Heartbeat
            or Constants.ControllerChannel.EventName.PcStateChanged;

        await _gate.WaitAsync(ct);
        try
        {
            if (isTelemetry)
            {
                // In-memory only — never persisted, so offline telemetry never touches disk at all.
                _telemetryPending[eventName] = evt;
            }
            else
            {
                await EnsureLoadedAsync(ct);

                var outbox = new List<EventEnvelope>(_state.Outbox) { evt };
                if (outbox.Count > Constants.ControllerChannel.MaxOutboxSize)
                {
                    _logger.LogWarning(
                        "Outbox hajmi limitdan oshdi: {Count}/{Max} — pul/sessiya eventlari yo'qotilmaydi, tekshiring",
                        outbox.Count, Constants.ControllerChannel.MaxOutboxSize);
                }

                _state = _state with { Outbox = outbox };
                await WriteToDiskAsync(ct);
            }
        }
        finally
        {
            _gate.Release();
        }

        _sendSignal.Release();
    }

    public async Task EnqueueAsync(EventEnvelope evt, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await EnsureLoadedAsync(ct);
            var outbox = new List<EventEnvelope>(_state.Outbox) { evt };
            _state = _state with { Outbox = outbox };
            await WriteToDiskAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<EventEnvelope>> GetPendingAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await EnsureLoadedAsync(ct);
            var pending = new List<EventEnvelope>(_state.Outbox);
            pending.AddRange(_telemetryPending.Values);
            return pending;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task MarkSentAsync(string eventId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await EnsureLoadedAsync(ct);

            if (_state.Outbox.Exists(e => e.EventId == eventId))
            {
                var outbox = _state.Outbox.FindAll(e => e.EventId != eventId);
                _state = _state with { Outbox = outbox };
                await WriteToDiskAsync(ct);
                return;
            }

            var telemetryKey = _telemetryPending.FirstOrDefault(kv => kv.Value.EventId == eventId).Key;
            if (telemetryKey is not null)
                _telemetryPending.Remove(telemetryKey);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task WaitForPendingAsync(TimeSpan timeout, CancellationToken ct = default) =>
        _sendSignal.WaitAsync(timeout, ct);

    public void Dispose()
    {
        _gate.Dispose();
        _sendSignal.Dispose();
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_loaded)
            return;

        _state = await ReadFromDiskAsync(ct);
        _loaded = true;
        Current = _state.Session;
    }

    private async Task<PersistedState> ReadFromDiskAsync(CancellationToken ct)
    {
        if (!File.Exists(_filePath))
            return new PersistedState(null, [], []);

        try
        {
            await using var stream = File.OpenRead(_filePath);
            var loaded = await JsonSerializer.DeserializeAsync<PersistedState>(stream, ControllerJsonOptions.Default, ct);
            return loaded ?? new PersistedState(null, [], []);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Agent state fayli o'qib bo'lmadi ({Path}) — bo'sh holatdan boshlanadi", _filePath);
            return new PersistedState(null, [], []);
        }
    }

    private async Task WriteToDiskAsync(CancellationToken ct)
    {
        var tmpPath = _filePath + ".tmp";
        try
        {
            await using (var stream = File.Create(tmpPath))
            {
                await JsonSerializer.SerializeAsync(stream, _state, ControllerJsonOptions.Default, ct);
            }

            File.Move(tmpPath, _filePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent state faylini yozib bo'lmadi ({Path})", _filePath);
        }
    }

    private sealed record PersistedState(Session? Session, List<AppliedGrant> AppliedGrantIds, List<EventEnvelope> Outbox);

    private sealed record AppliedGrant(string GrantId, DateTime AppliedAtUtc);
}
