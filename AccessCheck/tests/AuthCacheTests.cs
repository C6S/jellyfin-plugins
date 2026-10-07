using Jellyfin.Database.Implementations.Entities;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Jellyfin.Plugin.AccessCheck.Tests;

public class AuthCacheTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly AuthCache _cache;
    private readonly User _alice = new("alice", "auth", "reset");

    public AuthCacheTests()
    {
        _cache = new AuthCache(_time);
        _cache.Set("alice", "secret", _alice, 600);
    }

    [Fact]
    public void ReturnsTheUserForTheRightPassword() => Assert.Same(_alice, _cache.Get("alice", "secret"));

    [Fact]
    public void UsernamesAreCaseInsensitive() => Assert.Same(_alice, _cache.Get("ALICE", "secret"));

    [Theory]
    [InlineData("Secret")]
    [InlineData("secret ")]
    [InlineData("")]
    public void AWrongPasswordMisses(string password) => Assert.Null(_cache.Get("alice", password));

    [Fact]
    public void EntriesExpire()
    {
        _time.Advance(TimeSpan.FromSeconds(599));
        Assert.NotNull(_cache.Get("alice", "secret"));

        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(_cache.Get("alice", "secret"));
    }

    [Fact]
    public void InvalidateRemovesTheEntry()
    {
        _cache.Invalidate("Alice");

        Assert.Null(_cache.Get("alice", "secret"));
    }
}
