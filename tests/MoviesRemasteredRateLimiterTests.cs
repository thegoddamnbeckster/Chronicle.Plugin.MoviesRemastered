using FluentAssertions;
using System.Diagnostics;
using Xunit;

namespace Chronicle.Plugin.MoviesRemastered.Tests;

public class MoviesRemasteredRateLimiterTests
{
    [Fact]
    public async Task ThrottleAsync_EnforcesMinimumDelay()
    {
        var limiter = new MoviesRemasteredRateLimiter(delayMs: 200);
        var sw = Stopwatch.StartNew();

        await limiter.ThrottleAsync(CancellationToken.None);
        await limiter.ThrottleAsync(CancellationToken.None);

        sw.Elapsed.TotalMilliseconds.Should().BeGreaterThan(150);
    }

    [Fact]
    public void Constructor_ClampsDelayToFloor()
    {
        var limiter = new MoviesRemasteredRateLimiter(delayMs: 100);
        limiter.DelayMs.Should().Be(1000);
    }

    [Fact]
    public async Task ThrottleAsync_RespectsCancellation()
    {
        var limiter = new MoviesRemasteredRateLimiter(delayMs: 5000);
        await limiter.ThrottleAsync(CancellationToken.None);

        using var cts = new CancellationTokenSource(50);
        var act = () => limiter.ThrottleAsync(cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
