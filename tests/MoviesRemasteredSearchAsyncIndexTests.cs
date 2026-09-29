using Chronicle.Plugin.MoviesRemastered.Models;
using Chronicle.Plugins.Models;
using FluentAssertions;
using Xunit;

namespace Chronicle.Plugin.MoviesRemastered.Tests;

/// <summary>
/// SearchAsync's only job now is to match MediaSearchContext against the on-disk index built
/// by MoviesRemasteredSyncIndexTask -- no network call, ever (see the class's own doc for why).
/// </summary>
public class MoviesRemasteredSearchAsyncIndexTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("mrdb-search-tests-").FullName;
    private string IndexPath => Path.Combine(_dir, "search-index.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private MoviesRemasteredMetadataProvider MakeProvider(params MoviesRemasteredIndexEntry[] entries)
    {
        if (entries.Length > 0)
            MoviesRemasteredSearchIndexStore.Save(IndexPath, new MoviesRemasteredSearchIndexData { Entries = [.. entries] });

        var provider = new MoviesRemasteredMetadataProvider();
        provider.ConfigureForTesting(new HttpClient(), new MoviesRemasteredRateLimiter(1), IndexPath);
        return provider;
    }

    [Fact]
    public async Task SearchAsync_NoIndexFileYet_ReturnsEmpty_DoesNotThrow()
    {
        var provider = MakeProvider(); // no entries -> Save is skipped -> file doesn't exist

        var candidates = await provider.SearchAsync(new MediaSearchContext("Snow: Part I"));

        candidates.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_MatchesIndexedTitle_AboveThreshold()
    {
        var provider = MakeProvider(
            new MoviesRemasteredIndexEntry { MrdbId = 12179, Title = "Snow: Part I", Year = 2026 });

        var candidates = await provider.SearchAsync(new MediaSearchContext("Snow: Part I", Year: 2026));

        candidates.Should().ContainSingle();
        candidates[0].Metadata.ExternalId.Should().Be("mrdb:12179");
        candidates[0].Metadata.Title.Should().Be("Snow: Part I");
        candidates[0].Score.Should().BeGreaterThanOrEqualTo(50);
    }

    [Fact]
    public async Task SearchAsync_NoMatchingTitle_ReturnsEmpty()
    {
        var provider = MakeProvider(
            new MoviesRemasteredIndexEntry { MrdbId = 1, Title = "The Matrix Resurrections - The Binary Cut" });

        var candidates = await provider.SearchAsync(new MediaSearchContext("Snow: Part I"));

        candidates.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_MoreThanTenMatches_ReturnsTopTenByScore()
    {
        var entries = Enumerable.Range(1, 15)
            .Select(i => new MoviesRemasteredIndexEntry { MrdbId = i, Title = "Snow: Part I", Year = 2026 })
            .ToArray();
        var provider = MakeProvider(entries);

        var candidates = await provider.SearchAsync(new MediaSearchContext("Snow: Part I", Year: 2026));

        candidates.Should().HaveCount(10);
        candidates.Should().BeInDescendingOrder(c => c.Score);
    }

    [Fact]
    public async Task SearchAsync_ThrowsInvalidOperation_WhenNotConfigured()
    {
        var provider = new MoviesRemasteredMetadataProvider();
        var act = () => provider.SearchAsync(new MediaSearchContext("Test"));
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
