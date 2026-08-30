using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Nudge.Core.Diagnostics;
using Nudge.Core.Models;

namespace Nudge.Media;

/// <summary>
/// Permanent, per-table disk cache for resolved artwork - once an image has been found and resized
/// for a table, it is never fetched again. Keyed by (source name, table file path) - the source name
/// is part of the key, not just the path, so caching one source's result for a table can never be
/// mistaken for another source's when more than one <see cref="Core.Abstractions.IArtworkProvider"/>
/// exists (see <c>CompositeArtworkProvider</c>) - otherwise switching a table from vps-db to Google
/// Images would keep silently serving the old vps-db image back out of a cache entry that looked
/// keyed by table alone. Hashed so the cache filename is always safe regardless of what characters
/// the real path or source name contain.
/// </summary>
public interface IArtworkCache
{
    Task<ArtworkImage?> TryGetAsync(string sourceName, string tablePath, CancellationToken cancellationToken = default);

    Task SaveAsync(string sourceName, string tablePath, ArtworkImage image, CancellationToken cancellationToken = default);
}

public sealed class ArtworkCache : IArtworkCache
{
    private readonly IFileSystem _fileSystem;
    private readonly string _cacheDirectory;
    private readonly IPathRedactor _redactor;
    private readonly ILogger<ArtworkCache> _logger;

    public ArtworkCache(IFileSystem fileSystem, string cacheDirectory, IPathRedactor redactor, ILogger<ArtworkCache> logger)
    {
        _fileSystem = fileSystem;
        _cacheDirectory = cacheDirectory;
        _redactor = redactor;
        _logger = logger;
    }

    public Task<ArtworkImage?> TryGetAsync(string sourceName, string tablePath, CancellationToken cancellationToken = default)
    {
        string imagePath = PathFor(sourceName, tablePath, ".png");
        string metaPath = PathFor(sourceName, tablePath, ".meta.txt");

        try
        {
            if (!_fileSystem.File.Exists(imagePath) || !_fileSystem.File.Exists(metaPath))
            {
                return Task.FromResult<ArtworkImage?>(null);
            }

            string[] meta = _fileSystem.File.ReadAllLines(metaPath);
            if (meta.Length < 3 || !int.TryParse(meta[0], out int width) || !int.TryParse(meta[1], out int height))
            {
                return Task.FromResult<ArtworkImage?>(null);
            }

            byte[] data = _fileSystem.File.ReadAllBytes(imagePath);
            return Task.FromResult<ArtworkImage?>(new ArtworkImage
            {
                Data = data,
                Width = width,
                Height = height,
                Source = meta[2]
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not read the cached artwork for {Path}.", _redactor.Redact(tablePath));
            return Task.FromResult<ArtworkImage?>(null);
        }
    }

    public async Task SaveAsync(string sourceName, string tablePath, ArtworkImage image, CancellationToken cancellationToken = default)
    {
        try
        {
            _fileSystem.Directory.CreateDirectory(_cacheDirectory);

            await _fileSystem.File
                .WriteAllBytesAsync(PathFor(sourceName, tablePath, ".png"), image.Data, cancellationToken)
                .ConfigureAwait(false);

            await _fileSystem.File
                .WriteAllTextAsync(
                    PathFor(sourceName, tablePath, ".meta.txt"),
                    $"{image.Width}\n{image.Height}\n{image.Source}",
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing the cache write just means this table gets looked up again next time - not
            // worth failing the whole artwork lookup over.
            _logger.LogDebug(ex, "Could not cache artwork for {Path}.", _redactor.Redact(tablePath));
        }
    }

    private string PathFor(string sourceName, string tablePath, string extension)
    {
        string key = $"{sourceName.ToLowerInvariant()}|{tablePath.ToLowerInvariant()}";
        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return _fileSystem.Path.Combine(_cacheDirectory, hash + extension);
    }
}
