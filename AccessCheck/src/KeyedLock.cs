namespace Jellyfin.Plugin.AccessCheck;

// One lock per key, kept only while someone holds or waits for it. Keys come
// from the request (usernames), so locks that outlived their requests would
// let arbitrary usernames pile up forever.
public sealed class KeyedLock
{
    private sealed class Entry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        // Holders plus waiters; guarded by the _entries lock.
        public int Users { get; set; }
    }

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    // Keys currently held or waited on, for tests.
    internal int Count
    {
        get
        {
            lock (_entries)
                return _entries.Count;
        }
    }

    // Returns a handle that releases the lock when disposed, or null if the
    // lock wasn't acquired within the timeout.
    public async Task<IDisposable?> TryAcquireAsync(string key, TimeSpan timeout)
    {
        Entry entry;
        lock (_entries)
        {
            if (!_entries.TryGetValue(key, out entry!))
                _entries[key] = entry = new Entry();
            entry.Users++;
        }

        var acquired = false;
        try
        {
            acquired = await entry.Semaphore.WaitAsync(timeout).ConfigureAwait(false);
        }
        finally
        {
            if (!acquired)
                Leave(key, entry);
        }

        return acquired ? new Handle(this, key, entry) : null;
    }

    private void Leave(string key, Entry entry)
    {
        lock (_entries)
        {
            if (--entry.Users > 0)
                return;
            _entries.Remove(key);
        }

        entry.Semaphore.Dispose();
    }

    private sealed class Handle(KeyedLock owner, string key, Entry entry) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            entry.Semaphore.Release();
            owner.Leave(key, entry);
        }
    }
}
