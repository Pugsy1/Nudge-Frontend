using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using Nudge.Core.Abstractions;
using Nudge.Core.Diagnostics;
using Nudge.Core.Models;
using Nudge.Core.Results;

namespace Nudge.Library;

/// <summary>
/// Walks a folder of <c>.vpx</c> tables, reads what's changed since the last scan, and writes the
/// result to the database. This is the one piece of Nudge.Library Phase 3 builds; grouping,
/// duplicate detection, health checking and import all arrive in later phases.
/// </summary>
public sealed class VpxLibraryScanner : IVpxLibraryScanner
{
    /// <summary>
    /// Rows per database transaction. AGENTS.md's performance budget calls for "one transaction
    /// per few hundred rows, not per row" - 200 is comfortably within that without holding an
    /// especially large batch of pending writes in memory at once.
    /// </summary>
    private const int BatchSize = 200;

    private readonly IFileSystem _fileSystem;
    private readonly ITableFileReader _tableFileReader;
    private readonly ITableRepository _repository;
    private readonly IPathRedactor _redactor;
    private readonly ILogger<VpxLibraryScanner> _logger;

    public VpxLibraryScanner(
        IFileSystem fileSystem,
        ITableFileReader tableFileReader,
        ITableRepository repository,
        IPathRedactor redactor,
        ILogger<VpxLibraryScanner> logger)
    {
        _fileSystem = fileSystem;
        _tableFileReader = tableFileReader;
        _repository = repository;
        _redactor = redactor;
        _logger = logger;
    }

    public async Task<ScanResult> ScanAsync(
        string installationId,
        string tablesPath,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        if (!_fileSystem.Directory.Exists(tablesPath))
        {
            _logger.LogWarning("Tables folder {Path} does not exist; nothing to scan.", _redactor.Redact(tablesPath));
            return new ScanResult
            {
                TotalFilesFound = 0,
                Scanned = 0,
                Skipped = 0,
                Failed = 0,
                Removed = 0,
                Duration = stopwatch.Elapsed
            };
        }

        List<string> files;
        try
        {
            files = _fileSystem.Directory
                .EnumerateFiles(tablesPath, "*.vpx", SearchOption.AllDirectories)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not enumerate {Path}.", _redactor.Redact(tablesPath));
            return new ScanResult
            {
                TotalFilesFound = 0,
                Scanned = 0,
                Skipped = 0,
                Failed = 0,
                Removed = 0,
                Duration = stopwatch.Elapsed
            };
        }

        _logger.LogInformation("Scanning {Count} table file(s) in {Path}.", files.Count, _redactor.Redact(tablesPath));

        IReadOnlyDictionary<string, ScannedFileFingerprint> knownFingerprints =
            await _repository.GetFingerprintsAsync(installationId, cancellationToken).ConfigureAwait(false);

        var currentPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pendingBatch = new List<TableScanEntry>(BatchSize);
        var failedPaths = new List<string>();
        int scanned = 0, skipped = 0, failed = 0;

        for (int i = 0; i < files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string path = files[i];
            currentPaths.Add(path);
            progress?.Report(new ScanProgress(i, files.Count, _fileSystem.Path.GetFileName(path)));

            IFileInfo fileInfo;
            try
            {
                fileInfo = _fileSystem.FileInfo.New(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed++;
                failedPaths.Add(path);
                _logger.LogDebug(ex, "Could not stat {Path}.", _redactor.Redact(path));
                continue;
            }

            // Incremental scan: a file whose size and last-write time both match what was recorded
            // last time is assumed unchanged and is not re-read. This is what keeps a repeat scan
            // of a large, mostly-unchanged collection fast.
            if (knownFingerprints.TryGetValue(path, out ScannedFileFingerprint known)
                && known.FileSizeBytes == fileInfo.Length
                && known.FileLastWriteTimeUtc == fileInfo.LastWriteTimeUtc)
            {
                skipped++;
                continue;
            }

            Result<VpxTableFile> result = await _tableFileReader.ReadAsync(path, cancellationToken).ConfigureAwait(false);

            if (result.IsFailure)
            {
                failed++;
                failedPaths.Add(path);
                _logger.LogDebug("Could not read {Path}: {Reason}", _redactor.Redact(path), result.Error);
                continue;
            }

            scanned++;
            pendingBatch.Add(new TableScanEntry(result.Value, fileInfo.Length, fileInfo.LastWriteTimeUtc));

            if (pendingBatch.Count >= BatchSize)
            {
                await _repository.UpsertManyAsync(installationId, pendingBatch, cancellationToken).ConfigureAwait(false);
                pendingBatch.Clear();
            }
        }

        if (pendingBatch.Count > 0)
        {
            await _repository.UpsertManyAsync(installationId, pendingBatch, cancellationToken).ConfigureAwait(false);
        }

        int removed = await _repository.DeleteMissingAsync(installationId, currentPaths, cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(new ScanProgress(files.Count, files.Count, string.Empty));
        stopwatch.Stop();

        _logger.LogInformation(
            "Scan finished in {ElapsedMs} ms: {Scanned} scanned, {Skipped} unchanged, {Failed} failed, {Removed} removed.",
            stopwatch.ElapsedMilliseconds,
            scanned,
            skipped,
            failed,
            removed);

        return new ScanResult
        {
            TotalFilesFound = files.Count,
            Scanned = scanned,
            Skipped = skipped,
            Failed = failed,
            Removed = removed,
            Duration = stopwatch.Elapsed,
            FailedPaths = failedPaths
        };
    }
}
