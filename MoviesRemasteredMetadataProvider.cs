using Chronicle.Plugin.MoviesRemastered.Models;
using Chronicle.Plugins;
using Chronicle.Plugins.Models;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Chronicle.Plugin.MoviesRemastered;

/// <summary>
/// IMetadataProvider implementation for moviesremastered.com (MRDb).
/// Supports media type "fanedits" — the same type Chronicle.Plugin.FanEdit declares.
/// No authentication required; detail pages (movieinfo.php) are public.
///
/// SearchAsync currently finds nothing automatically (see its own doc) -- the only way an item
/// gets matched to this plugin right now is a pasted moviesremastered.com URL/id via Fix Match.
/// A real automatic-discovery mechanism is possible without violating robots.txt: the site
/// publishes /sitemap.xml (itself robots.txt-explicit, not disallowed) listing every one of its
/// ~3,300 movieinfo.php?id=N pages -- a periodic crawl of that sitemap plus each new/changed
/// page could build a local title index for SearchAsync to match against, entirely through
/// allowed endpoints. Not built yet; ScoreSearchResult/Normalise/Levenshtein* below and
/// MoviesRemasteredScraper.ParseSearchResults are the title-matching pieces that mechanism would
/// reuse, kept rather than deleted for that reason -- they are otherwise unreachable from
/// production code right now.
/// </summary>
public sealed class MoviesRemasteredMetadataProvider : IMetadataProvider
{
    private const string BaseUrl        = "https://www.moviesremastered.com";
    private const int    ScoreThreshold = 50;

    private MoviesRemasteredRateLimiter? _limiter;
    private MoviesRemasteredScraper?     _scraper;
    private HttpClient?                  _http;

    // ── Identity ──────────────────────────────────────────────────────────
    public string PluginId => "chronicle.plugin.moviesremastered";
    public string Name     => "Movies Remastered (MRDb)";
    public string Version  => "1.0.0";
    public string Author   => "Chronicle Contributors";

    // ── Capabilities ──────────────────────────────────────────────────────
    public MediaTypeSupport[] GetSupportedMediaTypes() =>
    [
        new MediaTypeSupport
        {
            MediaTypeName   = "fanedits",
            // Same DisplayName as Chronicle.Plugin.FanEdit's declaration — an idempotent
            // upsert of identical data, so the "fanedits" type still exists even if only
            // one of the two plugins is installed.
            DisplayName     = "Fan Edits",
            HierarchyLevels = 1,
            // Lower priority than FanEdit's default of 10 — user can reorder via
            // Settings > Metadata Assignment regardless.
            DefaultPriority = 20,
            SupportedFields = ["title", "overview", "year", "poster_url",
                               "runtime_minutes", "genres", "rating", "tags"],
        },
    ];

    public PluginSettingsSchema GetSettingsSchema() => new()
    {
        Settings =
        [
            new SettingDefinition
            {
                Key          = "request_delay_ms",
                Label        = "Request Delay (ms)",
                Description  = "Minimum delay between requests. Floor: 1000 ms. Be kind to the server.",
                Type         = SettingType.Number,
                Required     = false,
                DefaultValue = "1000",
            },
            new SettingDefinition
            {
                Key          = "user_agent",
                Label        = "User-Agent String",
                Description  = "Identifies Chronicle to the server. Root-caused live (2026-09-28): " +
                                "this used to default to a real Chrome browser string, which " +
                                "identifies this traffic as a browser instead of what it actually is.",
                Type         = SettingType.Text,
                Required     = false,
                DefaultValue = "Chronicle/1.0 (+https://github.com/thegoddamnbeckster/Chronicle)",
            },
        ]
    };

    // ── Lifecycle ─────────────────────────────────────────────────────────
    public void Configure(IReadOnlyDictionary<string, string> settings)
    {
        var delayMs = settings.TryGetValue("request_delay_ms", out var d) && int.TryParse(d, out var di) ? di : 1000;
        var ua      = settings.GetValueOrDefault("user_agent",
            "Chronicle/1.0 (+https://github.com/thegoddamnbeckster/Chronicle)");

        _limiter = new MoviesRemasteredRateLimiter(delayMs);
        _scraper = new MoviesRemasteredScraper();
        _http    = new HttpClient();
        _http.DefaultRequestHeaders.Add("User-Agent", ua);
    }

    private void EnsureConfigured()
    {
        if (_limiter is null)
            throw new InvalidOperationException(
                "MoviesRemasteredMetadataProvider is not configured. Call Configure() first.");
    }

    // ── Core operations ───────────────────────────────────────────────────

    private static readonly Regex _trailingYear = new(@"\s*\(\d{4}\)\s*$");
    private static readonly Regex _punctuation   = new(@"[^a-z0-9\s]");

    /// <summary>
    /// No longer searches -- moviesremastered.com's own robots.txt disallows
    /// <c>/searchresults.php</c> (it explicitly Allows <c>/movieinfo.php</c>, <c>/index.php</c>,
    /// and <c>/user/</c>, and disallows searchresults.php/viewreview.php/follow.php/
    /// moreresults.php alongside its admin/account paths). Root-caused live (2026-09-28): this
    /// method's automatic title search hit exactly the disallowed endpoint on every enrichment
    /// attempt.
    ///
    /// Returns no candidates now. Code review (2026-09-28) caught the accompanying claim here
    /// ("GetByIdAsync still works ... so this plugin still functions") as materially misleading:
    /// nothing in Chronicle ever emits an "mrdb:"-prefixed cross-reference id automatically (no
    /// other plugin points at this one, and this provider declares no accepted cross-ref
    /// prefixes), so GetByIdAsync is reachable ONLY via a user manually pasting a
    /// moviesremastered.com URL/id through Fix Match. Automatic discovery is fully off for now,
    /// not merely "no longer by title" -- see this class's own doc for the sitemap-based
    /// replacement that could restore it without violating robots.txt.
    /// </summary>
    public Task<IReadOnlyList<ScoredCandidate>> SearchAsync(
        MediaSearchContext context, CancellationToken ct = default)
    {
        EnsureConfigured();
        return Task.FromResult<IReadOnlyList<ScoredCandidate>>([]);
    }

    internal static (int Score, string Reason) ScoreSearchResult(MediaSearchContext ctx, MoviesRemasteredSearchResult r)
    {
        var score   = 0;
        var reasons = new List<string>();

        var norm  = Normalise(r.Title);
        var query = Normalise(_trailingYear.Replace(ctx.Name, ""));

        if (norm == query) { score += 40; reasons.Add("exact title match"); }
        else if (LevenshteinRatio(norm, query) <= 0.2) { score += 20; reasons.Add("fuzzy title match"); }

        if (ctx.Year.HasValue && r.Year.HasValue)
        {
            var diff = Math.Abs(ctx.Year.Value - r.Year.Value);
            if (diff == 0)      { score += 20; reasons.Add("year exact match"); }
            else if (diff == 1) { score += 10; reasons.Add("year within 1"); }
            else                { score -= 10; reasons.Add("year mismatch"); }
        }

        if (ctx.ParentName is not null &&
            (r.OriginalTitle?.Contains(ctx.ParentName, StringComparison.OrdinalIgnoreCase) ?? false))
        {
            score += 10; reasons.Add("source title match");
        }

        return (score, string.Join("; ", reasons));
    }

    private static string Normalise(string s)
    {
        s = s.ToLowerInvariant();
        s = _punctuation.Replace(s, " ");
        s = Regex.Replace(s, @"\s+", " ");
        return s.Trim();
    }

    private static double LevenshteinRatio(string a, string b)
    {
        if (a == b) return 0;
        var maxLen = Math.Max(a.Length, b.Length);
        if (maxLen == 0) return 0;
        return (double)LevenshteinDistance(a, b) / maxLen;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
        for (var j = 1; j <= b.Length; j++)
        {
            var cost = a[i - 1] == b[j - 1] ? 0 : 1;
            d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
        }
        return d[a.Length, b.Length];
    }

    private static string UrlToExternalId(string url)
    {
        var m = _idFromUrlRegex.Match(url);
        return m.Success ? $"mrdb:{m.Groups[1].Value}" : url;
    }

    private static readonly Regex _idFromUrlRegex = new(@"movieinfo\.php\?id=(\d+)", RegexOptions.IgnoreCase);

    public async Task<MediaMetadata> GetByIdAsync(string externalId, CancellationToken ct = default)
    {
        EnsureConfigured();

        var url = ResolveUrl(externalId);
        await _limiter!.ThrottleAsync(ct);
        var resp = await _http!.GetAsync(url, ct);

        if (resp.StatusCode == HttpStatusCode.NotFound)
            throw new KeyNotFoundException($"No MRDb entry found at {url}");

        resp.EnsureSuccessStatusCode();
        var html  = await resp.Content.ReadAsStringAsync(ct);
        var entry = _scraper!.ParseDetailPage(html, url);

        return MapToMetadata(entry, url);
    }

    public async Task<byte[]> GetImageAsync(string url, CancellationToken ct = default)
    {
        EnsureConfigured();
        await _limiter!.ThrottleAsync(ct);
        var resp = await _http!.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsByteArrayAsync(ct);
    }

    public async Task<bool> HealthCheckAsync(CancellationToken ct = default)
    {
        EnsureConfigured();
        try
        {
            var resp = await _http!.GetAsync($"{BaseUrl}/hub.php", ct);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    // ── Private helpers ───────────────────────────────────────────────────

    internal static string ResolveUrl(string externalId)
    {
        if (externalId.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            // SSRF guard — only fetch moviesremastered.com URLs, same precedent as FanEdit's
            // host allowlist check in ResolveUrl.
            if (!Uri.TryCreate(externalId, UriKind.Absolute, out var uri) ||
                (!uri.Host.Equals("www.moviesremastered.com", StringComparison.OrdinalIgnoreCase) &&
                 !uri.Host.Equals("moviesremastered.com", StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"URL must be a moviesremastered.com address: '{externalId}'");
            return externalId;
        }
        if (externalId.StartsWith("mrdb:", StringComparison.OrdinalIgnoreCase))
        {
            var id = externalId["mrdb:".Length..];
            return $"{BaseUrl}/movieinfo.php?id={id}";
        }
        // Bare integer
        return $"{BaseUrl}/movieinfo.php?id={externalId}";
    }

    private static MediaMetadata MapToMetadata(MoviesRemasteredEntry entry, string url)
    {
        var extData = new Dictionary<string, object?>
        {
            ["originalTitle"]          = entry.OriginalTitle,
            ["originalReleaseDate"]    = entry.OriginalReleaseDate,
            ["originalRuntimeMinutes"] = entry.OriginalRuntimeMinutes,
            ["faneditorUsername"]      = entry.FaneditorUsername,
            ["faneditorProfileUrl"]    = entry.FaneditorProfileUrl,
            ["fanEditType"]            = entry.FanEditType,
            ["franchise"]              = entry.Franchise,
            ["techSpecs"]              = entry.TechSpecs is null ? null : new
            {
                source     = entry.TechSpecs.Source,
                resolution = entry.TechSpecs.Resolution,
                soundMix   = entry.TechSpecs.SoundMix,
            },
            // "certification" and "released" are the canonical keys ScraperController/
            // MetadataResolutionService read (see FieldMap and BuildMovieDetails) -- named
            // to match those, not MRDb's own field names, so this data actually surfaces
            // through to the NFO/Kodi instead of sitting unread under a key nothing looks for.
            ["certification"] = entry.Certificate,
            ["language"]      = entry.Language,
            ["subtitles"]     = entry.Subtitles,
            ["timeCut"]       = entry.TimeCut,
            ["timeAdded"]     = entry.TimeAdded,
            ["intentions"]    = entry.Intentions,
            ["changeList"]    = entry.ChangeList,
            ["views"]         = entry.Views,
            ["reviewCount"]   = entry.ReviewCount,
            ["favoriteCount"] = entry.FavoriteCount,
            ["mrdbId"]        = entry.MrdbId,
            ["mrdbUrl"]       = url,
            ["released"]      = entry.ReleaseDate,
        };

        return new MediaMetadata
        {
            Title          = entry.Title,
            Overview       = entry.Overview,
            Year           = entry.Year,
            RuntimeMinutes = entry.RuntimeMinutes,
            PosterUrl      = entry.PosterUrl,
            Genres         = entry.Genres,
            Rating         = entry.Rating,
            Tags           = entry.Tags,
            ExternalId     = UrlToExternalId(url),
            ExtendedData   = JsonSerializer.SerializeToElement(extData),
        };
    }
}
