namespace ClubPay.Agent.Client.Services;

/// <summary>
/// Per-key async mutual exclusion with automatic cleanup: an Entry exists only while at least one caller
/// holds or is waiting on its key, so this never grows unbounded over a long-running kiosk process. Used
/// by CommandDispatcherService to serialize the "find cached result → execute → record" sequence for a
/// given command_id — AgentStateRepository's own internal gate only serializes each individual store
/// call, not that whole sequence, so two concurrent DispatchAsync calls for the same command_id could
/// otherwise both miss the cache and execute the command.
/// </summary>
internal sealed class AsyncKeyedLock
{
    private sealed class Entry
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int RefCount;
    }

    private readonly Dictionary<string, Entry> _entries = [];

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken ct = default)
    {
        Entry entry;
        lock (_entries)
        {
            if (!_entries.TryGetValue(key, out entry!))
            {
                entry = new Entry();
                _entries[key] = entry;
            }
            entry.RefCount++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(ct);
        }
        catch
        {
            // Never acquired the semaphore — release only the refcount, not the semaphore itself, or a
            // cancelled waiter permanently leaks this Entry (and deadlocks future callers of `key`).
            DecrementRefCount(key, entry);
            throw;
        }

        return new Releaser(this, key, entry);
    }

    private void DecrementRefCount(string key, Entry entry)
    {
        lock (_entries)
        {
            if (--entry.RefCount == 0)
                _entries.Remove(key);
        }
    }

    private void ReleaseHeld(string key, Entry entry)
    {
        entry.Semaphore.Release();
        DecrementRefCount(key, entry);
    }

    private sealed class Releaser(AsyncKeyedLock owner, string key, Entry entry) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            owner.ReleaseHeld(key, entry);
        }
    }
}
