using System.Text.Json;
using Chronicle.Plugin.MoviesRemastered.Models;

namespace Chronicle.Plugin.MoviesRemastered;

/// <summary>
/// Reads/writes the local title index at <c>{data_dir}/search-index.json</c>. No in-memory
/// caching on top of the file — <see cref="MoviesRemasteredMetadataProvider.SearchAsync"/> loads
/// fresh on every call. The file is small (a few hundred KB for ~3,300 entries) and this keeps
/// the provider genuinely stateless between calls (matching every other plugin in this codebase)
/// instead of needing its own staleness/reload tracking to stay in sync with
/// <see cref="MoviesRemasteredSyncIndexTask"/>, which runs as a separate instance and may write
/// to the same file mid-crawl.
/// </summary>
internal static class MoviesRemasteredSearchIndexStore
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = false };

    /// <summary>Returns an empty index (never throws) if the file is missing, unreadable, or corrupt.</summary>
    public static MoviesRemasteredSearchIndexData Load(string indexPath)
    {
        try
        {
            if (!File.Exists(indexPath))
                return new MoviesRemasteredSearchIndexData();

            var json = File.ReadAllText(indexPath);
            return JsonSerializer.Deserialize<MoviesRemasteredSearchIndexData>(json)
                   ?? new MoviesRemasteredSearchIndexData();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new MoviesRemasteredSearchIndexData();
        }
    }

    /// <summary>
    /// Writes via a temp file + <see cref="File.Move"/> so a crash mid-write (the first full
    /// crawl takes ~55 minutes at the enforced 1 req/sec floor) can never leave a truncated,
    /// unreadable index behind — the previous complete version stays in place until the new
    /// one has landed in full.
    /// </summary>
    public static void Save(string indexPath, MoviesRemasteredSearchIndexData data)
    {
        var tmpPath = indexPath + ".tmp";
        var json = JsonSerializer.Serialize(data, _jsonOptions);
        File.WriteAllText(tmpPath, json);
        File.Move(tmpPath, indexPath, overwrite: true);
    }
}
