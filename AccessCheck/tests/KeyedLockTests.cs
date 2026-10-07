using System.Collections.Concurrent;
using Xunit;

namespace Jellyfin.Plugin.AccessCheck.Tests;

public class KeyedLockTests
{
    private static readonly TimeSpan Long = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task HoldersOfOneKeyNeverOverlapButKeysRunInParallel()
    {
        var locks = new KeyedLock();
        var inside = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var overlaps = 0;
        var concurrent = 0;
        var maxConcurrent = 0;

        await Task.WhenAll(Enumerable.Range(0, 2000).Select(i => Task.Run(
            async () =>
            {
                // Mixed case: the same user however it is typed.
                var key = (i % 2 == 0 ? "user" : "USER") + (i % 20);
                using var handle = await locks.TryAcquireAsync(key, Long);
                Assert.NotNull(handle);

                if (inside.AddOrUpdate(key, 1, (_, n) => n + 1) > 1)
                    Interlocked.Increment(ref overlaps);
                var now = Interlocked.Increment(ref concurrent);
                int seen;
                while ((seen = maxConcurrent) < now && Interlocked.CompareExchange(ref maxConcurrent, now, seen) != seen)
                {
                }

                await Task.Yield();

                Interlocked.Decrement(ref concurrent);
                inside.AddOrUpdate(key, 0, (_, n) => n - 1);
            },
            TestContext.Current.CancellationToken)));

        Assert.Equal(0, overlaps);
        Assert.True(maxConcurrent > 1, $"keys never ran in parallel (max {maxConcurrent})");
        Assert.Equal(0, locks.Count);
    }

    [Fact]
    public async Task DistinctKeysDoNotAccumulate()
    {
        var locks = new KeyedLock();

        for (var i = 0; i < 10_000; i++)
            (await locks.TryAcquireAsync(Guid.NewGuid().ToString(), Long))!.Dispose();

        Assert.Equal(0, locks.Count);
    }

    [Fact]
    public async Task ATimedOutWaiterLeavesNothingBehind()
    {
        var locks = new KeyedLock();

        var held = await locks.TryAcquireAsync("x", Long);
        var timedOut = await locks.TryAcquireAsync("X", TimeSpan.FromMilliseconds(50));

        Assert.Null(timedOut);
        Assert.Equal(1, locks.Count);

        held!.Dispose();
        Assert.Equal(0, locks.Count);

        using var again = await locks.TryAcquireAsync("x", TimeSpan.FromMilliseconds(50));
        Assert.NotNull(again);
    }

    [Fact]
    public async Task DisposingTwiceReleasesOnce()
    {
        var locks = new KeyedLock();

        var first = await locks.TryAcquireAsync("x", Long);
        first!.Dispose();
        var second = await locks.TryAcquireAsync("x", Long);
        first.Dispose();

        // Still held by the second handle.
        Assert.Null(await locks.TryAcquireAsync("x", TimeSpan.FromMilliseconds(50)));
        second!.Dispose();
        Assert.Equal(0, locks.Count);
    }
}
