using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Nudge.App.Services;

/// <summary>
/// Keeps a private copy of any cover image the user picks by hand.
/// </summary>
public interface ICustomArtworkStore
{
    /// <summary>
    /// Copies <paramref name="sourcePath"/> into Nudge's own storage and returns the path to that
    /// copy - or the original path unchanged if the copy could not be made, so picking an image
    /// never fails outright just because the private copy did.
    /// </summary>
    string Import(string sourcePath, string tablePath);
}

/// <summary>
/// Custom covers used to be remembered as a path straight into wherever the user happened to pick
/// the file - their Pictures folder, a download, a USB stick. Nudge stored that path faithfully, but
/// the moment the file was renamed, moved, tidied away or deleted, loading it threw
/// FileNotFoundException and the tile silently fell back to scraped artwork. From the outside that
/// is indistinguishable from "Nudge forgot the picture I chose", and it was the single most common
/// way a chosen cover appeared to reset itself.
///
/// Copying the image into Nudge's own artwork folder at pick time fixes that permanently: the
/// remembered path points somewhere only Nudge manages, so nothing the user does to the original
/// afterwards can break it.
/// </summary>
public sealed class CustomArtworkStore : ICustomArtworkStore
{
    private readonly string _directory;
    private readonly ILogger<CustomArtworkStore> _logger;

    public CustomArtworkStore(string directory, ILogger<CustomArtworkStore> logger)
    {
        _directory = directory;
        _logger = logger;
    }

    public string Import(string sourcePath, string tablePath)
    {
        try
        {
            if (!File.Exists(sourcePath))
            {
                return sourcePath;
            }

            // Already ours: re-importing would just pile up identical copies every time the
            // customization page is saved.
            if (sourcePath.StartsWith(_directory, StringComparison.OrdinalIgnoreCase))
            {
                return sourcePath;
            }

            Directory.CreateDirectory(_directory);

            // Named from the table's own path, not the image's: one cover per table, so re-picking
            // replaces the previous copy instead of leaving the old one behind forever. Hashed
            // because a table path contains characters (colons, backslashes) a filename cannot.
            string extension = Path.GetExtension(sourcePath);
            string destination = Path.Combine(_directory, HashOf(tablePath) + extension);

            File.Copy(sourcePath, destination, overwrite: true);

            // A previous pick for this same table with a different extension (.png then .jpg) would
            // otherwise linger as an orphan nothing ever references again.
            foreach (string stale in Directory.EnumerateFiles(_directory, HashOf(tablePath) + ".*"))
            {
                if (!string.Equals(stale, destination, StringComparison.OrdinalIgnoreCase))
                {
                    TryDelete(stale);
                }
            }

            return destination;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Falling back to the original path keeps the feature working exactly as it did before;
            // it just loses the protection against the file later moving.
            _logger.LogWarning(ex, "Could not copy the chosen cover into Nudge's own storage; using the original path.");
            return sourcePath;
        }
    }

    private static string HashOf(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToLowerInvariant())))[..16];

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not remove a superseded custom cover.");
        }
    }
}
