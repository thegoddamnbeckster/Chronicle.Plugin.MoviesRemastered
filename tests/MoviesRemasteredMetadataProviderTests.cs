using Chronicle.Plugin.MoviesRemastered.Models;
using Chronicle.Plugins.Models;
using FluentAssertions;
using Xunit;

namespace Chronicle.Plugin.MoviesRemastered.Tests;

public class MoviesRemasteredMetadataProviderIdentityTests
{
    [Fact]
    public void GetSupportedMediaTypes_ReturnsFaneditsOnly()
    {
        var provider = new MoviesRemasteredMetadataProvider();
        var types = provider.GetSupportedMediaTypes();

        types.Should().ContainSingle();
        types[0].MediaTypeName.Should().Be("fanedits");
    }

    [Fact]
    public void GetSettingsSchema_HasNoRequiredCredentials()
    {
        var provider = new MoviesRemasteredMetadataProvider();
        var schema = provider.GetSettingsSchema();

        schema.Settings.Should().NotContain(s => s.Required);
        schema.Settings.Should().NotContain(s => s.Key == "username" || s.Key == "password");
    }

    [Fact]
    public async Task SearchAsync_ThrowsInvalidOperation_WhenNotConfigured()
    {
        var provider = new MoviesRemasteredMetadataProvider();
        var act = () => provider.SearchAsync(new MediaSearchContext("Test"));
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetByIdAsync_ThrowsInvalidOperation_WhenNotConfigured()
    {
        var provider = new MoviesRemasteredMetadataProvider();
        var act = () => provider.GetByIdAsync("mrdb:1");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}

public class MoviesRemasteredScoreSearchResultTests
{
    [Fact]
    public void ScoreSearchResult_ExactTitleAndYear_ScoresAboveThreshold()
    {
        var ctx = new MediaSearchContext("Snow: Part I", Year: 2026);
        var r = new MoviesRemasteredSearchResult
        {
            Title = "Snow: Part I",
            Year  = 2026,
            Url   = "https://www.moviesremastered.com/movieinfo.php?id=12179",
        };

        var (score, reason) = MoviesRemasteredMetadataProvider.ScoreSearchResult(ctx, r);

        score.Should().BeGreaterThanOrEqualTo(50);
        reason.Should().Contain("exact title match");
    }

    [Fact]
    public void ScoreSearchResult_YearMismatch_ScoresLow()
    {
        var ctx = new MediaSearchContext("Snow: Part I", Year: 2010);
        var r = new MoviesRemasteredSearchResult { Title = "Snow: Part I", Year = 2026 };

        var (score, _) = MoviesRemasteredMetadataProvider.ScoreSearchResult(ctx, r);

        score.Should().Be(30); // 40 (exact title) - 10 (year mismatch > 1)
    }

    [Fact]
    public void ScoreSearchResult_CompletelyDifferentTitle_ScoresBelowThreshold()
    {
        var ctx = new MediaSearchContext("Snow: Part I");
        var r = new MoviesRemasteredSearchResult { Title = "The Matrix Resurrections - The Binary Cut" };

        var (score, _) = MoviesRemasteredMetadataProvider.ScoreSearchResult(ctx, r);

        score.Should().BeLessThan(50);
    }
}

public class MoviesRemasteredResolveUrlTests
{
    [Fact]
    public void ResolveUrl_RejectsNonMrdbHost()
    {
        var act = () => MoviesRemasteredMetadataProvider.ResolveUrl("https://evil.example.com/x");
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("mrdb:12179", "https://www.moviesremastered.com/movieinfo.php?id=12179")]
    [InlineData("12179", "https://www.moviesremastered.com/movieinfo.php?id=12179")]
    [InlineData("https://www.moviesremastered.com/movieinfo.php?id=12179", "https://www.moviesremastered.com/movieinfo.php?id=12179")]
    public void ResolveUrl_HandlesAllInputFormats(string input, string expected)
    {
        MoviesRemasteredMetadataProvider.ResolveUrl(input).Should().Be(expected);
    }
}
