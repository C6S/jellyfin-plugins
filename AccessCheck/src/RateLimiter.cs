using System.Collections.Concurrent;

namespace Jellyfin.Plugin.AccessCheck;

public class RateLimiter : IDisposable
{
    private record Entry(int FailureCount, DateTimeOffset BlockedUntil);

    private readonly ConcurrentDictionary<string, Entry> _state =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<int> _getMaxSeconds;
    private readonly TimeProvider _time;
    private readonly ITimer _cleanupTimer;

    public RateLimiter(Func<int> getMaxSeconds, TimeProvider? time = null)
    {
        _getMaxSeconds = getMaxSeconds;
        _time = time ?? TimeProvider.System;
        _cleanupTimer = _time.CreateTimer(_ => Cleanup(), null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    private void Cleanup()
    {
        var cutoff = _time.GetUtcNow().AddSeconds(-_getMaxSeconds());
        foreach (var key in _state.Keys)
        {
            if (_state.TryGetValue(key, out var entry) && entry.BlockedUntil < cutoff)
                _state.TryRemove(new KeyValuePair<string, Entry>(key, entry));
        }
    }

    public bool IsBlocked(string username) =>
        _state.TryGetValue(username, out var entry) && entry.BlockedUntil > _time.GetUtcNow();

    public void RecordFailure(string username, int baseSeconds, int maxSeconds, double exponent)
    {
        var now = _time.GetUtcNow();
        _state.AddOrUpdate(
            username,
            _ => new Entry(1, now.AddSeconds(baseSeconds)),
            (_, prev) =>
            {
                // Forget earlier failures after maxSeconds of quiet. The quiet
                // starts when the block ends: measured from the last failure,
                // serving a capped block would itself count as quiet, and every
                // attempt after one would start over at baseSeconds.
                if ((now - prev.BlockedUntil).TotalSeconds > maxSeconds)
                    return new Entry(1, now.AddSeconds(baseSeconds));

                var count = prev.FailureCount + 1;
                var delay = Math.Min(baseSeconds * Math.Pow(exponent, count - 1), maxSeconds);
                return new Entry(count, now.AddSeconds(delay));
            });
    }

    public void RecordSuccess(string username) => _state.TryRemove(username, out _);

    // Usernames currently tracked, for tests.
    internal int Count => _state.Count;

    public void Dispose() => _cleanupTimer.Dispose();
}
