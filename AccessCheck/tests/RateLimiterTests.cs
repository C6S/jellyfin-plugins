using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Jellyfin.Plugin.AccessCheck.Tests;

public class RateLimiterTests
{
    private const int Base = 2;
    private const int Max = 10;
    private const double Exponent = 2;

    private readonly FakeTimeProvider _time = new();
    private readonly RateLimiter _limiter;

    public RateLimiterTests() => _limiter = new RateLimiter(() => Max, _time);

    private void Fail(string username = "alice") => _limiter.RecordFailure(username, Base, Max, Exponent);

    private void AssertBlockedFor(double seconds, string username = "alice")
    {
        _time.Advance(TimeSpan.FromSeconds(seconds) - TimeSpan.FromMilliseconds(1));
        Assert.True(_limiter.IsBlocked(username), $"unblocked before {seconds}s");
        _time.Advance(TimeSpan.FromMilliseconds(2));
        Assert.False(_limiter.IsBlocked(username), $"still blocked after {seconds}s");
    }

    [Fact]
    public void AnUnknownUserIsNotBlocked() => Assert.False(_limiter.IsBlocked("alice"));

    [Fact]
    public void TheBlockGrowsExponentiallyUpToTheCap()
    {
        Fail();
        AssertBlockedFor(2);
        Fail();
        AssertBlockedFor(4);
        Fail();
        AssertBlockedFor(8);
        Fail();
        AssertBlockedFor(10);
        Fail();
        AssertBlockedFor(10);
    }

    [Fact]
    public void FailuresAreForgottenAfterTheCapOfQuietFollowingTheBlock()
    {
        Fail();
        Fail();
        Fail();
        AssertBlockedFor(8);
        _time.Advance(TimeSpan.FromSeconds(Max));

        Fail();
        AssertBlockedFor(Base);
    }

    [Fact]
    public void ShorterQuietAfterTheBlockKeepsEscalating()
    {
        Fail();
        Fail();
        AssertBlockedFor(4);
        _time.Advance(TimeSpan.FromSeconds(Max - 1));

        Fail();
        AssertBlockedFor(8);
    }

    [Fact]
    public void ASuccessClearsTheHistory()
    {
        Fail();
        Fail();
        _limiter.RecordSuccess("alice");

        Assert.False(_limiter.IsBlocked("alice"));
        Fail();
        AssertBlockedFor(Base);
    }

    [Fact]
    public void UsernamesAreCaseInsensitiveAndIndependent()
    {
        Fail("alice");

        Assert.True(_limiter.IsBlocked("ALICE"));
        Assert.False(_limiter.IsBlocked("bob"));
    }

    [Fact]
    public void CleanupDropsExpiredEntries()
    {
        Fail("alice");
        Fail("bob");
        Assert.Equal(2, _limiter.Count);

        // The cleanup timer runs every 5 minutes.
        _time.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal(0, _limiter.Count);
    }
}
