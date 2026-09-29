using Chronicle.Plugin.MoviesRemastered.Models;
using FluentAssertions;
using Xunit;

namespace Chronicle.Plugin.MoviesRemastered.Tests;

public class MoviesRemasteredSearchIndexStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("mrdb-index-tests-").FullName;
    private string IndexPath => Path.Combine(_dir, "search-index.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Load_MissingFile_ReturnsEmptyIndex()
    {
        var data = MoviesRemasteredSearchIndexStore.Load(IndexPath);

        data.Entries.Should().BeEmpty();
        data.LastSitemapFetchAt.Should().BeNull();
    }

    [Fact]
    public void Load_CorruptFile_ReturnsEmptyIndex_DoesNotThrow()
    {
        File.WriteAllText(IndexPath, "{ not valid json");

        var act = () => MoviesRemasteredSearchIndexStore.Load(IndexPath);

        act.Should().NotThrow();
        act().Entries.Should().BeEmpty();
    }

    [Fact]
    public void SaveThenLoad_RoundTripsEntries()
    {
        var fetchedAt = new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero);
        var data = new MoviesRemasteredSearchIndexData
        {
            LastSitemapFetchAt = fetchedAt,
            Entries =
            [
                new MoviesRemasteredIndexEntry { MrdbId = 12179, Title = "Snow: Part I", Year = 2026, FetchedAt = fetchedAt },
                new MoviesRemasteredIndexEntry { MrdbId = 1, Title = "The Matrix Resurrections - The Binary Cut", Year = null, FetchedAt = fetchedAt },
            ],
        };

        MoviesRemasteredSearchIndexStore.Save(IndexPath, data);
        var loaded = MoviesRemasteredSearchIndexStore.Load(IndexPath);

        loaded.LastSitemapFetchAt.Should().Be(fetchedAt);
        loaded.Entries.Should().HaveCount(2);
        loaded.Entries.Should().ContainSingle(e => e.MrdbId == 12179 && e.Title == "Snow: Part I" && e.Year == 2026);
    }

    [Fact]
    public void Save_OverwritesPreviousContent_NoLeftoverTempFile()
    {
        MoviesRemasteredSearchIndexStore.Save(IndexPath, new MoviesRemasteredSearchIndexData
        {
            Entries = [new MoviesRemasteredIndexEntry { MrdbId = 1, Title = "First" }],
        });
        MoviesRemasteredSearchIndexStore.Save(IndexPath, new MoviesRemasteredSearchIndexData
        {
            Entries = [new MoviesRemasteredIndexEntry { MrdbId = 2, Title = "Second" }],
        });

        var loaded = MoviesRemasteredSearchIndexStore.Load(IndexPath);

        loaded.Entries.Should().ContainSingle().Which.Title.Should().Be("Second");
        File.Exists(IndexPath + ".tmp").Should().BeFalse();
    }
}
