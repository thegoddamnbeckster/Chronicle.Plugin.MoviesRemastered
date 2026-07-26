using FluentAssertions;
using Xunit;

namespace Chronicle.Plugin.MoviesRemastered.Tests;

public class MoviesRemasteredScraperSearchTests
{
    private static MoviesRemasteredScraper Scraper() => new();

    private const string SearchHtml = """
        <html><body>
        <DIV class="result-card d-flex">
          <DIV><A HREF=movieinfo.php?id=12179><IMG SRC=https://moviesremastered.com/images/12179-posterart.jpeg?cb=1785036872></A>
            <DIV class=column><DIV><i class="fa-sharp fa-solid fa-star"></i> N/A <img src=Staroutline.png></DIV></DIV>
          </DIV>
          <DIV>
            <B style='font-size:1.2em;'><A HREF=/movieinfo.php?id=12179>Snow: Part I</A></B><BR>
            <B>Original Title: </B><A HREF=/searchresults.php?searchtype=OriginalTitle&searchterm=x>Game of Thrones (TV Series)(2011)</A><BR>
            <B>Faneditor: </B><A HREF=/user/Spartan47>Spartan47</A><BR>
            <B>Franchise:</B> <span style='color:var(--text-dim)'>Game of Thrones</span><BR>
            <B>Fanedit Type:</B> <span style='color:var(--text-dim)'>TV-to-Movie</span><BR>
            <B>Fanedit Release Date: </B><span style='color:var(--text-dim)'>25th July 2026</span><BR>
            <B>Fanedit Runtime:</B> <span style='color:var(--text-dim)'>218</span><BR>
            <B>Synopsis:</B> <span style='color:var(--text-dim)'>As the Seven Kingdoms are consumed by political conflict.</span><BR>
          </DIV>
        </DIV>
        </body></html>
        """;

    [Fact]
    public void ParseSearchResults_ExtractsTitleAndId()
    {
        var results = Scraper().ParseSearchResults(SearchHtml);

        results.Should().HaveCount(1);
        results[0].Title.Should().Be("Snow: Part I");
        results[0].Url.Should().Be("https://www.moviesremastered.com/movieinfo.php?id=12179");
    }

    [Fact]
    public void ParseSearchResults_ExtractsLabeledFields()
    {
        var r = Scraper().ParseSearchResults(SearchHtml)[0];

        r.OriginalTitle.Should().Be("Game of Thrones (TV Series)(2011)");
        r.Faneditor.Should().Be("Spartan47");
        r.Franchise.Should().Be("Game of Thrones");
        r.FanEditType.Should().Be("TV-to-Movie");
        r.Year.Should().Be(2026);
        r.RuntimeMinutes.Should().Be(218);
        r.Synopsis.Should().StartWith("As the Seven Kingdoms");
    }

    [Fact]
    public void ParseSearchResults_RatingIsNull_WhenNA()
    {
        var r = Scraper().ParseSearchResults(SearchHtml)[0];
        r.Rating.Should().BeNull();
    }

    [Fact]
    public void ParseSearchResults_HandlesNoResults_Gracefully()
    {
        Scraper().ParseSearchResults("<html><body>No results</body></html>").Should().BeEmpty();
    }

    [Theory]
    [InlineData("3h:38m:0s", 218)]
    [InlineData("0h:45m:0s", 45)]
    [InlineData("9h:1m:0s", 541)]
    public void ParseHmsRuntimeToMinutes_ParsesCorrectly(string input, int expectedMinutes)
    {
        MoviesRemasteredScraper.ParseHmsRuntimeToMinutes(input).Should().Be(expectedMinutes);
    }
}

public class MoviesRemasteredScraperDetailTests
{
    private static MoviesRemasteredScraper Scraper() => new();

    private const string DetailHtml = """
        <html><head>
        <title>Snow: Part I | MRDb Fanedits</title>
        <meta property="og:title" content="Snow: Part I | MRDb Fanedits">
        <meta property="og:description" content="As the Seven Kingdoms are consumed by political conflict and the struggle for the Iron Throne, Jon Snow leaves Winterfell.">
        <meta property="og:image" content="https://moviesremastered.com/images/12179-posterart.jpeg">
        <script type="application/ld+json">
        {"@context":"https://schema.org","@type":"Movie","name":"Snow: Part I","description":"As the Seven Kingdoms are consumed by political conflict.","image":"https://moviesremastered.com/images/12179-posterart.jpeg","url":"https://www.moviesremastered.com/movieinfo.php?id=12179"}
        </script>
        </head><body>
        <div class="stats-container">
          <div class="stats-item"><B>MRDb Rating</B><br><i class="fa-solid fa-star"></i> No votes</div>
          <div class="stats-item"><B>Views</B><br><IMG SRC="views icon.png">&nbsp95</div>
          <div class="stats-item"><B>Reviews</B><br><B id=reviewcount>0</B></div>
          <div class="stats-item"><B>Favorite</B><br><SPAN ID=favcnt>3</SPAN></div>
        </div>
        <div class=column>
          <B>Faneditor: </B><A HREF=Spartan47>Spartan47</A>&nbsp&nbsp<BR>
          <B>Fanedit Type: </B>TV-to-Movie<BR>
          <B>Fanedit Release Date: </B>25th July 2026<BR>
          <B>Fanedit Runtime: </B>3h:38m:0s<BR>
          <B>Time Cut: </B>5h:23m:0s<BR>
          <B>Time Added: </B>0h:0m:0s<BR>
          <B>Franchise: </B><A HREF=searchresults.php?searchtype=Franchise&franchise=Game+of+Thrones>Game of Thrones</A><BR>
          <B>Genre: </B><A HREF=x?genre=Adventure>Adventure</A> • <A HREF=x?genre=Drama>Drama</A><BR>
          <B>Original Title: </B><A HREF=x>Game of Thrones (TV Series)(2011)</A><BR>
          <B>Original Release Date: </B>3rd January 2011<BR>
          <B>Original Runtime: </B>9h:1m:0s<BR>
          <HR>
          <B>Certificate: </B>18<BR>
          <B>Source: </B>4K<BR>
          <B>Resolution: </B>4k<BR>
          <B>Sound Mix: </B>5.1. Channels<BR>
          <B>Language: </B>English<BR>
          <B>Subtitles: </B>English • Spanish<BR>
        </div>
        <DIV><H3 style="color:red;">Synopsis:</H3>As the Seven Kingdoms are consumed by political conflict and the struggle for the Iron Throne, Jon Snow leaves Winterfell.<BR><BR></DIV>
        <HR>
        <DIV><H3 style="color:red;">Intentions:</H3>To combine Jon and Bran's storylines from season 1 and 2.<BR><BR></DIV>
        <HR>
        <DIV><H3 style="color:red;">Change List:</H3>Combined Jon Snow's storyline with Bran Stark's journey.<BR></DIV>
        </body></html>
        """;

    [Fact]
    public void ParseDetailPage_ExtractsTitle_WithoutSiteSuffix()
    {
        var entry = Scraper().ParseDetailPage(DetailHtml, "https://www.moviesremastered.com/movieinfo.php?id=12179");
        entry.Title.Should().Be("Snow: Part I");
    }

    [Fact]
    public void ParseDetailPage_ExtractsPosterUrl_FromOgImage()
    {
        var entry = Scraper().ParseDetailPage(DetailHtml, "https://www.moviesremastered.com/movieinfo.php?id=12179");
        entry.PosterUrl.Should().Be("https://moviesremastered.com/images/12179-posterart.jpeg");
    }

    [Fact]
    public void ParseDetailPage_ExtractsSynopsisSection_FullText()
    {
        var entry = Scraper().ParseDetailPage(DetailHtml, "https://www.moviesremastered.com/movieinfo.php?id=12179");
        entry.Overview.Should().Contain("Jon Snow leaves Winterfell");
    }

    [Fact]
    public void ParseDetailPage_ExtractsIntentionsAndChangeList()
    {
        var entry = Scraper().ParseDetailPage(DetailHtml, "x");
        entry.Intentions.Should().Contain("Bran's storylines");
        entry.ChangeList.Should().Contain("Bran Stark's journey");
    }

    [Fact]
    public void ParseDetailPage_ExtractsLabeledFields()
    {
        var entry = Scraper().ParseDetailPage(DetailHtml, "x");

        entry.FaneditorUsername.Should().Be("Spartan47");
        entry.FanEditType.Should().Be("TV-to-Movie");
        entry.Franchise.Should().Be("Game of Thrones");
        entry.OriginalTitle.Should().Be("Game of Thrones (TV Series)(2011)");
        entry.Certificate.Should().Be("18");
        entry.Genres.Should().BeEquivalentTo(["Adventure", "Drama"]);
        entry.Subtitles.Should().BeEquivalentTo(["English", "Spanish"]);
    }

    [Fact]
    public void ParseDetailPage_ParsesHmsRuntimes()
    {
        var entry = Scraper().ParseDetailPage(DetailHtml, "x");
        entry.RuntimeMinutes.Should().Be(218);          // 3h:38m
        entry.OriginalRuntimeMinutes.Should().Be(541);  // 9h:1m
    }

    [Fact]
    public void ParseDetailPage_RatingNull_WhenNoVotes()
    {
        var entry = Scraper().ParseDetailPage(DetailHtml, "x");
        entry.Rating.Should().BeNull();
    }

    [Fact]
    public void ParseDetailPage_ExtractsStats()
    {
        var entry = Scraper().ParseDetailPage(DetailHtml, "x");
        entry.Views.Should().Be(95);
        entry.ReviewCount.Should().Be(0);
        entry.FavoriteCount.Should().Be(3);
    }

    [Fact]
    public void ParseDetailPage_ExtractsYear_FromReleaseDate()
    {
        var entry = Scraper().ParseDetailPage(DetailHtml, "x");
        entry.Year.Should().Be(2026);
    }

    [Fact]
    public void ParseDetailPage_HandlesMissingFields_Gracefully()
    {
        var entry = Scraper().ParseDetailPage("<html><body></body></html>", "x");
        entry.Should().NotBeNull();
        entry.Title.Should().BeEmpty();
        entry.Overview.Should().BeNull();
    }
}
