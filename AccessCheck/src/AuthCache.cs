using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Jellyfin.Database.Implementations.Entities;

namespace Jellyfin.Plugin.AccessCheck;

public class AuthCache
{
    private record Entry(string PasswordHash, User User, DateTimeOffset ExpiresAt);

    private readonly ConcurrentDictionary<string, Entry> _cache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider _time;

    public AuthCache(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    public User? Get(string username, string password)
    {
        if (!_cache.TryGetValue(username, out var entry))
            return null;
        if (entry.ExpiresAt <= _time.GetUtcNow() || entry.PasswordHash != Hash(password))
            return null;
        return entry.User;
    }

    public void Set(string username, string password, User user, int ttlSeconds) =>
        _cache[username] = new Entry(Hash(password), user, _time.GetUtcNow().AddSeconds(ttlSeconds));

    public void Invalidate(string username) => _cache.TryRemove(username, out _);

    private static string Hash(string password) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password)));
}
