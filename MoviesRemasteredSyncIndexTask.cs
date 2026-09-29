using Chronicle.Plugin.MoviesRemastered.Models;
using Chronicle.Plugins;
using System.Net;

namespace Chronicle.Plugin.MoviesRemastered;

/// <summary>
/// Custom background task (<c>sync-search-index</c>) that builds the local title/year index
/// <see cref="MoviesRemasteredMetadataProvider.SearchAsync"/> matches against, since automatic
/// title search against moviesremastered.com's own /searchresults.php is permanently off (that
/// endpoint is disallowed by the site's robots.txt — see MoviesRemasteredMetadataProvider's own
/// doc). The site's /sitemap.xml IS explicitly allowed and lists every movieinfo.php?id=N detail
/// page it has, so this task:
///
///   1. Fetches and parses sitemap.xml for the current full set of ids.
///   2. Diffs that set against the ids already in the local index (its own file, not a separate
///      tracked list — see MoviesRemasteredSearchIndexData's own doc) to find new ones, and
///      drops any previously-indexed id no longer in the sitemap (a removed fan edit).
///   3. Crawls each new id's movieinfo.php page (through the same rate limiter every other
///      request in this plugin uses) and adds its title/year to the index.
///
/// A brand-new install has ~3,300 ids to crawl at the enforced 1 req/sec floor — roughly 55
/// minutes. That cost is exactly why this is its own opt-in task (manifest default_enabled:
/// false) rather than folded into "fetch-missing-metadata"/"resync-all-metadata": those run
/// per-item against whatever the enrichment queue already has, on a schedule meant for regular,
/// bounded runs — an hour-long one-time crawl has nothing in common with that shape. Once the
/// index exists, a later run only crawls whatever ids are new since last time, which is normally
/// a handful.
///
/// A per-id fetch/parse failure is swallowed and simply skipped -- the id stays out of the
/// index and gets retried automatically on the next run (it's still "new" from that run's point
/// of view), so there's no separate retry-tracking needed. Progress is saved periodically during
/// a long crawl (not only at the end) so a process restart mid-run loses at most a few entries'
/// worth of work, not the whole pass.
/// </summary>
public sealed class MoviesRemasteredSyncIndexTask : IPluginTask
{
    private const string BaseUrl = "https://www.moviesremastered.com";
    private const int SaveBatchSize = 25;

    private MoviesRemasteredRateLimiter? _limiter;
    private MoviesRemasteredScraper? _scraper;
    private HttpClient? _http;
    private string? _indexPath;

    public string TaskId => "sync-search-index";

    public void Configure(IReadOnlyDictionary<string, string> settings)
    {
        var delayMs = settings.TryGetValue("request_delay_ms", out var d) && int.TryParse(d, out var di) ? di : 1000;
        var ua = settings.GetValueOrDefault("user_agent",
            "Chronicle/1.0 (+https://github.com/thegoddamnbeckster/Chronicle)");
        var dataDir = settings.GetValueOrDefault(IPluginTask.DataDirectorySettingsKey)
            ?? throw new InvalidOperationException(
                $"MoviesRemasteredSyncIndexTask requires '{IPluginTask.DataDirectorySettingsKey}' in settings.");

        _limiter = new MoviesRemasteredRateLimiter(delayMs);
        _scraper = new MoviesRemasteredScraper();
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("User-Agent", ua);
        _indexPath = Path.Combine(dataDir, "search-index.json");
    }

    /// <summary>Test-only seam — bypasses Configure()'s real HttpClient resolution.</summary>
    internal void ConfigureForTesting(HttpClient http, MoviesRemasteredRateLimiter limiter, string indexPath)
    {
        _http      = http;
        _limiter   = limiter;
        _scraper   = new MoviesRemasteredScraper();
        _indexPath = indexPath;
    }

    private void EnsureConfigured()
    {
        if (_limiter is null)
            throw new InvalidOperationException("MoviesRemasteredSyncIndexTask is not configured. Call Configure() first.");
    }

    public async Task RunAsync(CancellationToken ct)
    {
        EnsureConfigured();

        var sitemapIds = await FetchSitemapIdsAsync(ct);
        if (sitemapIds.Count == 0)
            return; // don't touch the existing index on an empty/failed sitemap fetch

        var data = MoviesRemasteredSearchIndexStore.Load(_indexPath!);
        var entries = data.Entries.ToDictionary(e => e.MrdbId);
        var lastSitemapFetchAt = data.LastSitemapFetchAt;

        foreach (var staleId in entries.Keys.Where(id => !sitemapIds.Contains(id)).ToList())
            entries.Remove(staleId);

        var newIds = sitemapIds.Where(id => !entries.ContainsKey(id)).Order().ToList();

        var sinceLastSave = 0;
        try
        {
            foreach (var id in newIds)
            {
                ct.ThrowIfCancellationRequested();

                var entry = await TryCrawlEntryAsync(id, ct);
                if (entry is not null)
                    entries[entry.MrdbId] = entry;

                if (++sinceLastSave >= SaveBatchSize)
                {
                    MoviesRemasteredSearchIndexStore.Save(_indexPath!,
                        new MoviesRemasteredSearchIndexData { LastSitemapFetchAt = lastSitemapFetchAt, Entries = [.. entries.Values] });
                    sinceLastSave = 0;
                }
            }

            // Only stamp LastSitemapFetchAt on a run that reached the end of the new-id list
            // normally -- it's purely informational (see MoviesRemasteredSearchIndexData's own
            // doc) so there's no correctness reason to stamp it on a cancelled run, only a
            // clarity one: a timestamp on an interrupted pass would misleadingly read as "fully
            // up to date".
            lastSitemapFetchAt = DateTimeOffset.UtcNow;
        }
        finally
        {
            // Always persist whatever progress was made, including on cancellation -- a process
            // restart mid-crawl should lose at most the last unsaved batch, not the whole pass.
            MoviesRemasteredSearchIndexStore.Save(_indexPath!,
                new MoviesRemasteredSearchIndexData { LastSitemapFetchAt = lastSitemapFetchAt, Entries = [.. entries.Values] });
        }
    }

    private async Task<MoviesRemasteredIndexEntry?> TryCrawlEntryAsync(int id, CancellationToken ct)
    {
        try
        {
            await _limiter!.ThrottleAsync(ct);
            var url = $"{BaseUrl}/movieinfo.php?id={id}";
            var resp = await _http!.GetAsync(url, ct);
            if (resp.StatusCode == HttpStatusCode.NotFound)
                return null;

            resp.EnsureSuccessStatusCode();
            var html = await resp.Content.ReadAsStringAsync(ct);
            var parsed = _scraper!.ParseDetailPage(html, url);

            return new MoviesRemasteredIndexEntry
            {
                MrdbId = id,
                Title = parsed.Title,
                Year = parsed.Year,
                FetchedAt = DateTimeOffset.UtcNow,
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Best effort -- the id simply stays absent from the index and is retried on the
            // next run (see this class's own doc). A transient network blip on entry #1,247 of
            // 3,300 must not abort the whole crawl.
            return null;
        }
    }

    private async Task<HashSet<int>> FetchSitemapIdsAsync(CancellationToken ct)
    {
        try
        {
            await _limiter!.ThrottleAsync(ct);
            var resp = await _http!.GetAsync($"{BaseUrl}/sitemap.xml", ct);
            resp.EnsureSuccessStatusCode();
            var xml = await resp.Content.ReadAsStringAsync(ct);
            return [.. _scraper!.ParseSitemapIds(xml)];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [];
        }
    }
}
