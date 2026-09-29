using System.Net;
using Chronicle.Plugin.MoviesRemastered.Models;
using FluentAssertions;
using Xunit;

namespace Chronicle.Plugin.MoviesRemastered.Tests;

public class MoviesRemasteredSyncIndexTaskTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("mrdb-sync-task-tests-").FullName;
    private string IndexPath => Path.Combine(_dir, "search-index.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string DetailHtml(string title, string releaseDate) => $$"""
        <html><head>
        <script type="application/ld+json">
        {"@type":"Movie","name":"{{title}}"}
        </script>
        </head><body>
        <div class=column>
          <B>Faneditor: </B><A HREF=x>Someone</A><BR>
          <B>Fanedit Release Date: </B>{{releaseDate}}<BR>
        </div>
        </body></html>
        """;

    private MoviesRemasteredSyncIndexTask MakeTask(RoutingHttpHandler handler)
    {
        var task = new MoviesRemasteredSyncIndexTask();
        task.ConfigureForTesting(new HttpClient(handler), new MoviesRemasteredRateLimiter(1), IndexPath);
        return task;
    }

    [Fact]
    public async Task RunAsync_FirstRun_CrawlsEverySitemapId()
    {
        var handler = new RoutingHttpHandler
        {
            ["sitemap.xml"] = _ => Ok("""
                <url><loc>https://www.moviesremastered.com/movieinfo.php?id=1</loc></url>
                <url><loc>https://www.moviesremastered.com/movieinfo.php?id=2</loc></url>
                """),
            ["movieinfo.php?id=1"] = _ => Ok(DetailHtml("Snow: Part I", "25th July 2026")),
            ["movieinfo.php?id=2"] = _ => Ok(DetailHtml("Other Edit", "1st January 2020")),
        };

        await MakeTask(handler).RunAsync(CancellationToken.None);

        var index = MoviesRemasteredSearchIndexStore.Load(IndexPath);
        index.Entries.Should().HaveCount(2);
        index.Entries.Should().ContainSingle(e => e.MrdbId == 1 && e.Title == "Snow: Part I" && e.Year == 2026);
        index.Entries.Should().ContainSingle(e => e.MrdbId == 2 && e.Title == "Other Edit" && e.Year == 2020);
        index.LastSitemapFetchAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RunAsync_SkipsIdsAlreadyInIndex_OnlyCrawlsNewOnes()
    {
        MoviesRemasteredSearchIndexStore.Save(IndexPath, new MoviesRemasteredSearchIndexData
        {
            Entries = [new MoviesRemasteredIndexEntry { MrdbId = 1, Title = "Already Indexed", Year = 2019 }],
        });

        var handler = new RoutingHttpHandler
        {
            ["sitemap.xml"] = _ => Ok("""
                <url><loc>https://www.moviesremastered.com/movieinfo.php?id=1</loc></url>
                <url><loc>https://www.moviesremastered.com/movieinfo.php?id=2</loc></url>
                """),
            ["movieinfo.php?id=2"] = _ => Ok(DetailHtml("New Edit", "1st January 2020")),
        };

        await MakeTask(handler).RunAsync(CancellationToken.None);

        handler.RequestedPaths.Should().NotContain(p => p.Contains("id=1"));
        var index = MoviesRemasteredSearchIndexStore.Load(IndexPath);
        index.Entries.Should().HaveCount(2);
        index.Entries.Should().ContainSingle(e => e.MrdbId == 1 && e.Title == "Already Indexed");
        index.Entries.Should().ContainSingle(e => e.MrdbId == 2 && e.Title == "New Edit");
    }

    [Fact]
    public async Task RunAsync_PrunesIdsNoLongerInSitemap()
    {
        MoviesRemasteredSearchIndexStore.Save(IndexPath, new MoviesRemasteredSearchIndexData
        {
            Entries =
            [
                new MoviesRemasteredIndexEntry { MrdbId = 1, Title = "Still Here" },
                new MoviesRemasteredIndexEntry { MrdbId = 2, Title = "Removed From Site" },
            ],
        });

        var handler = new RoutingHttpHandler
        {
            ["sitemap.xml"] = _ => Ok("""<url><loc>https://www.moviesremastered.com/movieinfo.php?id=1</loc></url>"""),
        };

        await MakeTask(handler).RunAsync(CancellationToken.None);

        var index = MoviesRemasteredSearchIndexStore.Load(IndexPath);
        index.Entries.Should().ContainSingle().Which.MrdbId.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_NotFoundDetailPage_SkippedWithoutThrowing()
    {
        var handler = new RoutingHttpHandler
        {
            ["sitemap.xml"] = _ => Ok("""<url><loc>https://www.moviesremastered.com/movieinfo.php?id=1</loc></url>"""),
            ["movieinfo.php?id=1"] = _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };

        var act = () => MakeTask(handler).RunAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        MoviesRemasteredSearchIndexStore.Load(IndexPath).Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_SitemapFetchFails_LeavesExistingIndexUntouched()
    {
        MoviesRemasteredSearchIndexStore.Save(IndexPath, new MoviesRemasteredSearchIndexData
        {
            Entries = [new MoviesRemasteredIndexEntry { MrdbId = 1, Title = "Untouched" }],
        });

        var handler = new RoutingHttpHandler
        {
            ["sitemap.xml"] = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
        };

        await MakeTask(handler).RunAsync(CancellationToken.None);

        var index = MoviesRemasteredSearchIndexStore.Load(IndexPath);
        index.Entries.Should().ContainSingle().Which.Title.Should().Be("Untouched");
    }

    [Fact]
    public async Task RunAsync_CancelledMidCrawl_PersistsProgressMadeSoFar()
    {
        // Three ids so the cancellation signal (raised during id=2's own request, after id=1
        // has already been fully processed) is only observed by the loop's own
        // ct.ThrowIfCancellationRequested() check before id=3 -- never during id=1 or id=2's own
        // response handling, which would otherwise make this test depend on exactly how/whether
        // HttpContent.ReadAsStringAsync reacts to an already-cancelled token.
        var cts = new CancellationTokenSource();
        var handler = new RoutingHttpHandler
        {
            ["sitemap.xml"] = _ => Ok("""
                <url><loc>https://www.moviesremastered.com/movieinfo.php?id=1</loc></url>
                <url><loc>https://www.moviesremastered.com/movieinfo.php?id=2</loc></url>
                <url><loc>https://www.moviesremastered.com/movieinfo.php?id=3</loc></url>
                """),
            ["movieinfo.php?id=1"] = _ => Ok(DetailHtml("Snow: Part I", "25th July 2026")),
            // id=2 is a 404 (harmlessly skipped, matching RunAsync_NotFoundDetailPage_SkippedWithoutThrowing)
            // regardless of cancellation -- the signal it raises here is only ever observed on
            // id=3's turn.
            ["movieinfo.php?id=2"] = _ =>
            {
                cts.Cancel();
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            },
        };

        var act = () => MakeTask(handler).RunAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        handler.RequestedPaths.Should().NotContain(p => p.Contains("id=3"));
        var index = MoviesRemasteredSearchIndexStore.Load(IndexPath);
        index.Entries.Should().ContainSingle().Which.MrdbId.Should().Be(1);
        // Cancelled runs don't claim a fully up-to-date sitemap pass -- see RunAsync's own doc.
        index.LastSitemapFetchAt.Should().BeNull();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static HttpResponseMessage Ok(string content) =>
        new(HttpStatusCode.OK) { Content = new StringContent(content) };
}

/// <summary>
/// Routes requests to a canned response by matching a substring of the request URL
/// (e.g. "sitemap.xml", "movieinfo.php?id=2") against registered keys, in indexer form so
/// tests can write <c>new RoutingHttpHandler { ["sitemap.xml"] = ... }</c>.
/// </summary>
internal sealed class RoutingHttpHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _routes = [];
    public List<string> RequestedPaths { get; } = [];

    public Func<HttpRequestMessage, HttpResponseMessage> this[string urlContains]
    {
        set => _routes[urlContains] = value;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var url = request.RequestUri!.ToString();
        RequestedPaths.Add(url);

        foreach (var (key, factory) in _routes)
            if (url.Contains(key, StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(factory(request));

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
