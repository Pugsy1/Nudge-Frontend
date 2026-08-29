using System.IO.Abstractions;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nudge.Core.Diagnostics;

namespace Nudge.Media.VpsDb;

/// <summary>Provides the current vps-db entry list, fetching and caching it as needed.</summary>
public interface IVpsDbIndex
{
    /// <summary>
    /// Never throws: a network failure with no usable cached copy yet returns an empty list, which
    /// callers treat exactly like "no match found" rather than a special error case.
    /// </summary>
    Task<IReadOnlyList<VpsDbEntry>> GetEntriesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Downloads vps-db's <c>db/vpsdb.json</c> (the community-maintained VPX metadata dataset - see
/// docs/RESEARCH-NOTES.md) and caches it to disk. vps-db is described as updated daily, so the cached
/// copy is reused for a day before a fresh download is attempted; a download failure falls back to
/// whatever is cached, however old, rather than leaving Nudge with nothing.
/// </summary>
public sealed class VpsDbIndex : IVpsDbIndex
{
    private const string IndexUrl = "https://raw.githubusercontent.com/VirtualPinballSpreadsheet/vps-db/main/db/vpsdb.json";
    private static readonly TimeSpan MaxCacheAge = TimeSpan.FromHours(24);

    private readonly HttpClient _httpClient;
    private readonly IFileSystem _fileSystem;
    private readonly string _cacheFilePath;
    private readonly IPathRedactor _redactor;
    private readonly ILogger<VpsDbIndex> _logger;

    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private IReadOnlyList<VpsDbEntry>? _loaded;

    public VpsDbIndex(
        HttpClient httpClient,
        IFileSystem fileSystem,
        string cacheFilePath,
        IPathRedactor redactor,
        ILogger<VpsDbIndex> logger)
    {
        _httpClient = httpClient;
        _fileSystem = fileSystem;
        _cacheFilePath = cacheFilePath;
        _redactor = redactor;
        _logger = logger;
    }

    public async Task<IReadOnlyList<VpsDbEntry>> GetEntriesAsync(CancellationToken cancellationToken = default)
    {
        if (_loaded is not null)
        {
            return _loaded;
        }

        // Guards against a burst of concurrent artwork lookups (e.g. many tiles loading at once)
        // each independently deciding the index needs refreshing and downloading it redundantly.
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_loaded is not null)
            {
                return _loaded;
            }

            _loaded = await LoadAsync(cancellationToken).ConfigureAwait(false);
            return _loaded;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task<IReadOnlyList<VpsDbEntry>> LoadAsync(CancellationToken cancellationToken)
    {
        if (IsCacheFresh())
        {
            IReadOnlyList<VpsDbEntry>? cached = TryReadCache();
            if (cached is not null)
            {
                return cached;
            }
        }

        IReadOnlyList<VpsDbEntry>? downloaded = await TryDownloadAsync(cancellationToken).ConfigureAwait(false);
        if (downloaded is not null)
        {
            return downloaded;
        }

        // The download failed (offline, vps-db unreachable, rate limited) - fall back to whatever is
        // cached, even if stale, rather than leaving artwork lookups with nothing all day.
        return TryReadCache() ?? [];
    }

    private bool IsCacheFresh()
    {
        try
        {
            return _fileSystem.File.Exists(_cacheFilePath)
                && DateTimeOffset.UtcNow - _fileSystem.File.GetLastWriteTimeUtc(_cacheFilePath) < MaxCacheAge;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private List<VpsDbEntry>? TryReadCache()
    {
        try
        {
            if (!_fileSystem.File.Exists(_cacheFilePath))
            {
                return null;
            }

            string json = _fileSystem.File.ReadAllText(_cacheFilePath);
            return JsonSerializer.Deserialize<List<VpsDbEntry>>(json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogDebug(ex, "Could not read the cached vps-db index at {Path}.", _redactor.Redact(_cacheFilePath));
            return null;
        }
    }

    private async Task<List<VpsDbEntry>?> TryDownloadAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));

            byte[] bytes = await _httpClient.GetByteArrayAsync(IndexUrl, timeout.Token).ConfigureAwait(false);
            List<VpsDbEntry>? entries = JsonSerializer.Deserialize<List<VpsDbEntry>>(bytes);

            if (entries is null)
            {
                return null;
            }

            _fileSystem.Directory.CreateDirectory(_fileSystem.Path.GetDirectoryName(_cacheFilePath)!);
            await _fileSystem.File.WriteAllBytesAsync(_cacheFilePath, bytes, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Downloaded the vps-db index: {Count} tables.", entries.Count);
            return entries;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            _logger.LogDebug(ex, "Could not download the vps-db index; falling back to the cached copy if any.");
            return null;
        }
    }
}
