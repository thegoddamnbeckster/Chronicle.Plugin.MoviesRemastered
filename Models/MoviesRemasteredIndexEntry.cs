namespace Chronicle.Plugin.MoviesRemastered.Models;

/// <summary>
/// One row of the locally cached title/year index built by <see cref="MoviesRemasteredSyncIndexTask"/>
/// from moviesremastered.com's own sitemap.xml + movieinfo.php detail pages, and read back by
/// <see cref="MoviesRemasteredMetadataProvider.SearchAsync"/> to match against without any network
/// call. Deliberately minimal — just enough to score a title match; the full record is only ever
/// fetched (via <see cref="MoviesRemasteredMetadataProvider.GetByIdAsync"/>) once an item is
/// actually matched.
/// </summary>
internal sealed class MoviesRemasteredIndexEntry
{
    public int             MrdbId    { get; set; }
    public string          Title     { get; set; } = string.Empty;
    public int?            Year      { get; set; }
    public DateTimeOffset  FetchedAt { get; set; }
}

/// <summary>On-disk shape of the index file — see <see cref="MoviesRemasteredSearchIndexStore"/>.</summary>
internal sealed class MoviesRemasteredSearchIndexData
{
    /// <summary>
    /// When the sitemap was last successfully fetched and fully parsed. Informational only —
    /// the diff against new ids is always computed from <see cref="Entries"/>' own keys, not
    /// from a separately tracked id list, so a partial/interrupted run can never desync the two.
    /// </summary>
    public DateTimeOffset? LastSitemapFetchAt { get; set; }

    public List<MoviesRemasteredIndexEntry> Entries { get; set; } = [];
}
