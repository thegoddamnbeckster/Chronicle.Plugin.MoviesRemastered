namespace Chronicle.Plugin.MoviesRemastered.Models;

internal sealed class MoviesRemasteredSearchResult
{
    public string  Title         { get; set; } = string.Empty;
    public string  Url           { get; set; } = string.Empty;
    public string? ThumbnailUrl  { get; set; }
    public string? Synopsis      { get; set; }
    public string? OriginalTitle { get; set; }
    public string? Faneditor     { get; set; }
    public string? Franchise     { get; set; }
    public string? FanEditType   { get; set; }
    public int?    Year          { get; set; }

    /// <summary>Already in minutes on the search-results page (unlike the detail page's "3h:38m:0s" format).</summary>
    public int?    RuntimeMinutes { get; set; }
    public double? Rating         { get; set; }
}
