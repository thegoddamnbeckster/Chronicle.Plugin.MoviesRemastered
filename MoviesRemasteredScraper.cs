using HtmlAgilityPack;
using Chronicle.Plugin.MoviesRemastered.Models;
using System.Text;
using System.Text.RegularExpressions;

namespace Chronicle.Plugin.MoviesRemastered;

internal sealed class MoviesRemasteredScraper
{
    private static readonly Regex _idFromUrl  = new(@"movieinfo\.php\?id=(\d+)", RegexOptions.IgnoreCase);
    private static readonly Regex _hmsRuntime = new(@"(\d+)h:(\d+)m:(\d+)s", RegexOptions.IgnoreCase);

    // ── Sitemap ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts every movieinfo.php numeric id from a sitemap.xml document — the id list
    /// <see cref="MoviesRemasteredSyncIndexTask"/> diffs against its local index to find fan
    /// edits it hasn't crawled yet. Deliberately tolerant: regex over the raw &lt;loc&gt; text
    /// rather than a strict XML-schema parse, since a malformed/truncated sitemap (a partial
    /// download, a stray non-ASCII byte) should still yield whatever ids it can rather than
    /// throwing away the whole batch.
    /// </summary>
    public List<int> ParseSitemapIds(string xml)
    {
        var ids = new HashSet<int>();
        foreach (Match m in _idFromUrl.Matches(xml))
            if (int.TryParse(m.Groups[1].Value, out var id))
                ids.Add(id);
        return [.. ids];
    }

    // ── Search results ────────────────────────────────────────────────────────

    public List<MoviesRemasteredSearchResult> ParseSearchResults(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var results = new List<MoviesRemasteredSearchResult>();

        var cards = doc.DocumentNode.SelectNodes("//*[contains(@class,'result-card')]");
        if (cards is null) return results;

        foreach (var card in cards)
        {
            var titleAnchor = card.SelectSingleNode(".//b[contains(@style,'font-size:1.2em')]/a")
                            ?? card.SelectSingleNode(".//b/a[contains(@href,'movieinfo.php')]");
            if (titleAnchor is null) continue;

            var href = titleAnchor.GetAttributeValue("href", string.Empty);
            var idMatch = _idFromUrl.Match(href);
            if (!idMatch.Success) continue;

            // The <B>Label:</B> siblings live in the same container div as the title
            // <B style='font-size:1.2em'>, one level below the result-card itself.
            var fieldContainer = titleAnchor.ParentNode?.ParentNode ?? card;
            var fields = ParseLabeledFields(fieldContainer);

            var result = new MoviesRemasteredSearchResult
            {
                Title         = HtmlEntity.DeEntitize(titleAnchor.InnerText).Trim(),
                Url           = $"https://www.moviesremastered.com/movieinfo.php?id={idMatch.Groups[1].Value}",
                ThumbnailUrl  = card.SelectSingleNode(".//img[@src]")?.GetAttributeValue("src", null),
                OriginalTitle = fields.GetValueOrDefault("Original Title"),
                Faneditor     = fields.GetValueOrDefault("Faneditor"),
                Franchise     = fields.GetValueOrDefault("Franchise"),
                FanEditType   = fields.GetValueOrDefault("Fanedit Type"),
                Synopsis      = fields.GetValueOrDefault("Synopsis"),
            };

            if (fields.TryGetValue("Fanedit Release Date", out var relDate))
            {
                var ym = Regex.Match(relDate, @"\b(19|20)\d{2}\b");
                if (ym.Success) result.Year = int.Parse(ym.Value);
            }

            if (fields.TryGetValue("Fanedit Runtime", out var rt) && int.TryParse(rt.Trim(), out var minutes))
                result.RuntimeMinutes = minutes;

            var ratingNode = card.SelectSingleNode(".//i[contains(@class,'fa-star')]/parent::*");
            if (ratingNode is not null)
            {
                var ratingText = HtmlEntity.DeEntitize(ratingNode.InnerText).Trim();
                if (!ratingText.Contains("N/A", StringComparison.OrdinalIgnoreCase))
                {
                    var rm = Regex.Match(ratingText, @"[\d.]+");
                    if (rm.Success && double.TryParse(rm.Value,
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var rv))
                        result.Rating = rv;
                }
            }

            results.Add(result);
        }

        return results;
    }

    // ── Detail page ───────────────────────────────────────────────────────────

    public MoviesRemasteredEntry ParseDetailPage(string html, string url)
    {
        var doc   = new HtmlDocument();
        doc.LoadHtml(html);
        var entry = new MoviesRemasteredEntry { Url = url };

        // 1. Title — JSON-LD "name" preferred (no site-name suffix), fall back to og:title
        entry.Title = ParseJsonLdField(doc, "name")
            ?? OgMeta(doc, "og:title")
            ?? PageTitle(doc)
            ?? string.Empty;
        entry.Title = Regex.Replace(entry.Title, @"\s*\|\s*MRDb Fanedits\s*$", "").Trim();

        // 2. Overview — prefer the full Synopsis section; JSON-LD/og:description as fallback
        entry.Overview = ParseLabeledSection(doc, "Synopsis")
            ?? ParseJsonLdField(doc, "description")
            ?? OgMeta(doc, "og:description");

        // 3. Poster — og:image is stable (no cache-bust query string)
        entry.PosterUrl = OgMeta(doc, "og:image") ?? ParseJsonLdField(doc, "image");

        // 4. Labeled key/value fields — the flat <B>Label:</B> value <BR> run
        var container = FindFieldContainer(doc);
        var fields = container is null
            ? new Dictionary<string, string>()
            : ParseLabeledFields(container);

        entry.FaneditorUsername = fields.GetValueOrDefault("Faneditor");
        var faneditorAnchor = container?.SelectSingleNode(".//b[contains(text(),'Faneditor')]/following-sibling::a[1]");
        entry.FaneditorProfileUrl = faneditorAnchor?.GetAttributeValue("href", null);

        entry.FanEditType         = fields.GetValueOrDefault("Fanedit Type");
        entry.ReleaseDate         = fields.GetValueOrDefault("Fanedit Release Date");
        entry.TimeCut             = fields.GetValueOrDefault("Time Cut");
        entry.TimeAdded           = fields.GetValueOrDefault("Time Added");
        entry.Franchise           = fields.GetValueOrDefault("Franchise");
        entry.OriginalTitle       = fields.GetValueOrDefault("Original Title");
        entry.OriginalReleaseDate = fields.GetValueOrDefault("Original Release Date");
        entry.Certificate         = fields.GetValueOrDefault("Certificate");
        entry.Language            = fields.GetValueOrDefault("Language");

        if (fields.TryGetValue("Fanedit Runtime", out var rt))
            entry.RuntimeMinutes = ParseHmsRuntimeToMinutes(rt);
        if (fields.TryGetValue("Original Runtime", out var ort))
            entry.OriginalRuntimeMinutes = ParseHmsRuntimeToMinutes(ort);

        if (fields.TryGetValue("Genre", out var genreVal))
            entry.Genres = SplitDotList(genreVal);
        if (fields.TryGetValue("Subtitles", out var subVal))
            entry.Subtitles = SplitDotList(subVal);

        if (fields.TryGetValue("Fanedit Release Date", out var rel))
        {
            var ym = Regex.Match(rel, @"\b(19|20)\d{2}\b");
            if (ym.Success) entry.Year = int.Parse(ym.Value);
        }

        var source     = fields.GetValueOrDefault("Source");
        var resolution = fields.GetValueOrDefault("Resolution");
        var soundMix   = fields.GetValueOrDefault("Sound Mix");
        if (source is not null || resolution is not null || soundMix is not null)
            entry.TechSpecs = new MoviesRemasteredTechSpecs
            {
                Source = source, Resolution = resolution, SoundMix = soundMix,
            };

        // 5. Free-text sections
        entry.Intentions = ParseLabeledSection(doc, "Intentions");
        entry.ChangeList = ParseLabeledSection(doc, "Change List");

        // 6. Stats block — rating / views / reviews / favorites
        ParseStats(doc, entry);

        // 7. Franchise / fanedit type as tags
        if (entry.Franchise is not null)
            entry.Tags = [entry.Franchise, .. entry.Tags];
        if (entry.FanEditType is not null && !entry.Tags.Contains(entry.FanEditType))
            entry.Tags.Add(entry.FanEditType);

        // 8. MRDb numeric ID from the URL
        var idM = _idFromUrl.Match(url);
        entry.MrdbId = idM.Success ? idM.Groups[1].Value : null;

        return entry;
    }

    /// <summary>
    /// The field container is the &lt;div&gt; whose direct children include the
    /// "Faneditor:" &lt;B&gt; label — locate it by that anchor rather than assuming a
    /// fixed class name, since the surrounding layout classes are inline-style-only.
    /// </summary>
    private static HtmlNode? FindFieldContainer(HtmlDocument doc)
    {
        var label = doc.DocumentNode.SelectSingleNode("//b[starts-with(normalize-space(text()),'Faneditor')]");
        return label?.ParentNode;
    }

    private static void ParseStats(HtmlDocument doc, MoviesRemasteredEntry entry)
    {
        var items = doc.DocumentNode.SelectNodes("//*[contains(@class,'stats-item')]");
        if (items is null) return;

        foreach (var item in items)
        {
            var label = item.SelectSingleNode(".//b")?.InnerText.Trim();
            if (label is null) continue;
            var text = HtmlEntity.DeEntitize(item.InnerText).Replace(label, "", StringComparison.OrdinalIgnoreCase).Trim();

            switch (label.ToLowerInvariant())
            {
                case "mrdb rating":
                    if (!text.Contains("No votes", StringComparison.OrdinalIgnoreCase))
                    {
                        var m = Regex.Match(text, @"[\d.]+");
                        if (m.Success && double.TryParse(m.Value,
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out var r))
                            entry.Rating = r;
                    }
                    break;
                case "views":
                {
                    var m = Regex.Match(text, @"\d+");
                    if (m.Success) entry.Views = int.Parse(m.Value);
                    break;
                }
                case "reviews":
                {
                    var m = Regex.Match(text, @"\d+");
                    if (m.Success) entry.ReviewCount = int.Parse(m.Value);
                    break;
                }
                case "favorite":
                {
                    var m = Regex.Match(text, @"\d+");
                    if (m.Success) entry.FavoriteCount = int.Parse(m.Value);
                    break;
                }
            }
        }
    }

    /// <summary>Finds the &lt;h3&gt; matching <paramref name="label"/> (e.g. "Synopsis") and
    /// returns the text of its following siblings within the same parent div.</summary>
    private static string? ParseLabeledSection(HtmlDocument doc, string label)
    {
        var h3 = doc.DocumentNode.SelectNodes("//h3")?.FirstOrDefault(n =>
            HtmlEntity.DeEntitize(n.InnerText).Trim().TrimEnd(':').Equals(label, StringComparison.OrdinalIgnoreCase));
        if (h3 is null) return null;

        var sb = new StringBuilder();
        var node = h3.NextSibling;
        while (node is not null)
        {
            sb.Append(HtmlEntity.DeEntitize(node.InnerText ?? string.Empty));
            node = node.NextSibling;
        }

        var text = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string? ParseJsonLdField(HtmlDocument doc, string field)
    {
        var scripts = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']");
        if (scripts is null) return null;

        foreach (var script in scripts)
        {
            try
            {
                using var jd = System.Text.Json.JsonDocument.Parse(script.InnerText);
                if (jd.RootElement.TryGetProperty("@type", out var t) && t.GetString() == "Movie" &&
                    jd.RootElement.TryGetProperty(field, out var v))
                    return v.GetString();
            }
            catch (System.Text.Json.JsonException) { /* malformed/partial LD+JSON — skip */ }
        }
        return null;
    }

    private static string? OgMeta(HtmlDocument doc, string property)
        => doc.DocumentNode.SelectSingleNode($"//meta[@property='{property}']")
              ?.GetAttributeValue("content", null);

    private static string? PageTitle(HtmlDocument doc)
        => doc.DocumentNode.SelectSingleNode("//title")?.InnerText.Trim();

    private static List<string> SplitDotList(string s) =>
        s.Split('•', StringSplitOptions.RemoveEmptyEntries)
         .Select(x => x.Trim())
         .Where(x => x.Length > 0)
         .ToList();

    // ── Shared labeled-field walker ───────────────────────────────────────────
    // MRDb has no class hooks on field values (unlike FanEdit's jrFieldRow/jrFieldLabel/
    // jrFieldValue) — fields are a flat run of <B>Label:</B> value <BR> siblings inside one
    // container. Walk children, tracking the current label, buffering nodes until the next
    // <B>…:</B> or <HR>, then render the buffer to a string.
    internal static Dictionary<string, string> ParseLabeledFields(HtmlNode container)
    {
        var raw = new Dictionary<string, List<HtmlNode>>(StringComparer.OrdinalIgnoreCase);
        string? currentLabel = null;
        var buffer = new List<HtmlNode>();

        void Flush()
        {
            if (currentLabel is not null)
                raw[currentLabel] = buffer;
            buffer = new List<HtmlNode>();
        }

        foreach (var node in container.ChildNodes)
        {
            if (node.Name.Equals("b", StringComparison.OrdinalIgnoreCase))
            {
                var text = HtmlEntity.DeEntitize(node.InnerText).Trim();
                if (text.EndsWith(':'))
                {
                    Flush();
                    currentLabel = text.TrimEnd(':').Trim();
                    continue;
                }
            }
            if (node.Name.Equals("hr", StringComparison.OrdinalIgnoreCase))
            {
                Flush();
                currentLabel = null;
                continue;
            }
            if (currentLabel is not null)
                buffer.Add(node);
        }
        Flush();

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (label, nodes) in raw)
        {
            var value = RenderFieldValue(nodes);
            if (!string.IsNullOrWhiteSpace(value))
                result[label] = value;
        }
        return result;
    }

    /// <summary>
    /// Multi-value fields (Genre, Subtitles) render as several &lt;a&gt;/&lt;span&gt;
    /// items separated by "•" in the source — join with " • ". When any named anchor is
    /// present, it IS the field value (e.g. Faneditor, Franchise) — trailing siblings are
    /// just report-abuse icon links and stray "&nbsp;" text nodes, not part of the value.
    /// Only fall back to raw text concatenation when there's no anchor at all.
    /// </summary>
    private static string RenderFieldValue(List<HtmlNode> nodes)
    {
        var namedAnchors = nodes
            .Where(n => n.Name.Equals("a", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(HtmlEntity.DeEntitize(n.InnerText)))
            .ToList();
        if (namedAnchors.Count >= 1)
            return string.Join(" • ", namedAnchors.Select(a => HtmlEntity.DeEntitize(a.InnerText).Trim()));

        var sb = new StringBuilder();
        foreach (var n in nodes)
        {
            if (n.Name is "br" or "img") continue;
            sb.Append(HtmlEntity.DeEntitize(n.InnerText));
        }
        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }

    internal static int? ParseHmsRuntimeToMinutes(string s)
    {
        var m = _hmsRuntime.Match(s);
        if (!m.Success) return null;
        return int.Parse(m.Groups[1].Value) * 60 + int.Parse(m.Groups[2].Value);
    }
}
