namespace ClubPay.Agent.Admin.Services.Controller;

/// <summary>Single coarse lock over a Dictionary + insertion-order Queue, not a lock-free
/// ConcurrentDictionary/ConcurrentQueue pair — concurrent lock-free pruning can otherwise evict a
/// still-valid entry that a different thread just (re)inserted. Matches AgentStateRepository's own
/// single-gate style rather than introducing a new concurrency pattern. Prune-on-write, no background
/// sweeper, same as AgentStateRepository's command_results/applied_ids pruning.</summary>
public sealed class EventIdempotencyStore : IEventIdempotencyStore
{
    private static readonly TimeSpan Retention = TimeSpan.FromHours(24);
    private const int MaxEntries = 50_000; // defensive cap independent of TTL, guards a runaway retry
                                            // storm outrunning the 24h window before it can prune

    private readonly object _gate = new();
    private readonly Dictionary<string, DateTime> _processedAtUtc = new();
    private readonly Queue<string> _insertionOrder = new();

    public bool TryMarkProcessed(string eventId)
    {
        var now = DateTime.UtcNow;
        lock (_gate)
        {
            PruneExpired(now);

            if (_processedAtUtc.ContainsKey(eventId))
                return false;

            _processedAtUtc[eventId] = now;
            _insertionOrder.Enqueue(eventId);

            while (_processedAtUtc.Count > MaxEntries && _insertionOrder.TryDequeue(out var oldest))
                _processedAtUtc.Remove(oldest);

            return true;
        }
    }

    private void PruneExpired(DateTime now)
    {
        while (_insertionOrder.TryPeek(out var oldest) &&
               _processedAtUtc.TryGetValue(oldest, out var processedAt) &&
               now - processedAt > Retention)
        {
            _insertionOrder.Dequeue();
            _processedAtUtc.Remove(oldest);
        }
    }
}
