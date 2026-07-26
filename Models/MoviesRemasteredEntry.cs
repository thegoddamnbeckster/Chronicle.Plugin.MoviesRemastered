namespace Chronicle.Plugin.MoviesRemastered.Models;

internal sealed class MoviesRemasteredEntry
{
    public string  Title          { get; set; } = string.Empty;
    public string  Url            { get; set; } = string.Empty;
    public string? Overview       { get; set; }
    public int?    Year           { get; set; }
    public int?    RuntimeMinutes { get; set; }
    public string? PosterUrl      { get; set; }
    public List<string> Genres    { get; set; } = [];
    public double? Rating         { get; set; }
    public List<string> Tags      { get; set; } = [];

    // Faneditor
    public string? FaneditorUsername   { get; set; }
    public string? FaneditorProfileUrl { get; set; }

    // Classification
    public string? FanEditType { get; set; }
    public string? Franchise   { get; set; }

    // Source material
    public string? OriginalTitle          { get; set; }
    public string? OriginalReleaseDate    { get; set; }
    public int?    OriginalRuntimeMinutes { get; set; }

    // Tech specs
    public MoviesRemasteredTechSpecs? TechSpecs { get; set; }

    // Cut/edit details
    public string? TimeCut    { get; set; }
    public string? TimeAdded  { get; set; }
    public string? Intentions { get; set; }
    public string? ChangeList { get; set; }

    // Reception
    public int? Views         { get; set; }
    public int? ReviewCount   { get; set; }
    public int? FavoriteCount { get; set; }

    // Certificate / language
    public string?      Certificate { get; set; }
    public string?       Language    { get; set; }
    public List<string> Subtitles   { get; set; } = [];

    // Publishing
    public string? MrdbId      { get; set; }
    public string? ReleaseDate { get; set; }
}
