using System.Collections.Concurrent;
using System.IO.Abstractions;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPathRedactor _redactor;
    private readonly ILogger<VpxLibraryScanner> _logger;

    /// <summary>
    /// One gate per installation, so two overlapping scans of the *same* installation never write
    /// to the database at the same time (the known gap flagged in Phase 3 -
    /// docs/IMPLEMENTATION-STATUS.md: "no concurrent-scan protection... worth guarding against
    /// before Phase 4 adds a rescan button"). Scans of different installations are unaffected by
    /// each other's gate. This class is registered as a singleton, so the dictionary's lifetime
    /// matches the application's.
    /// </summary>
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _scanGates =
        new(StringComparer.OrdinalIgnoreCase);

    /// <remarks>
    /// Takes an <see cref="IServiceScopeFactory"/> rather than an <see cref="ITableRepository"/>
    /// directly: this class is registered as a DI singleton (so its per-installation scan gates
    /// persist for the app's lifetime - see <see cref="_scanGates"/>), but <c>ITableRepository</c>
    /// and its <c>NudgeDbContext</c> are Scoped. A singleton that captured a scoped dependency in
    /// its constructor would hold one database context open forever, its change tracker only ever
    /// growing. Resolving a fresh repository from a fresh scope inside each <see cref="ScanAsync"/>
    /// call avoids that without changing the singleton's own lifetime.
    /// </remarks>
    public VpxLibraryScanner(
        IFileSystem fileSystem,
        ITableFileReader tableFileReader,
        IServiceScopeFactory scopeFactory,
        IPathRedactor redactor,
        ILogger<VpxLibraryScanner> logger)
    {
        _fileSystem = fileSystem;
        _tableFileReader = tableFileReader;
        _scopeFactory = scopeFactory;
        _redactor = redactor;
        _logger = logger;
    }

    /// <summary>
    /// A second call for an installation already being scanned waits for the first to finish, then
    /// runs its own fresh scan - it is never rejected and never allowed to race the first one's
    /// database writes.
    /// </summary>
    public async Task<ScanResult> ScanAsync(
        string installationId,
        string tablesPath,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        SemaphoreSlim gate = _scanGates.GetOrAdd(installationId, static _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ScanCoreAsync(installationId, tablesPath, progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<ScanResult> ScanCoreAsync(
        string installationId,
        string tablesPath,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        ITableRepository repository = scope.ServiceProvider.GetRequiredService<ITableRepository>();

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

        // The SearchOption overload enumerates in "Compatible" mode, where one inaccessible
        // subdirectory anywhere under the tree (a locked folder, a cloud-placeholder stub, a
        // permissions quirk) throws and aborts the whole walk - discarding every file already
        // found, so a rescan silently reports zero removals and nothing already in the database
        // ever gets cleaned up. IgnoreInaccessible=true skips the bad entry and keeps walking the
        // rest of the tree instead.
        // AttributesToSkip=0 overrides EnumerationOptions' own default (Hidden | System) - the old
        // SearchOption.AllDirectories "Compatible" mode this replaced never skipped files by
        // attribute at all, and plenty of real .vpx files end up marked Hidden or System without the
        // user ever choosing that (some VPX table installers/extractors set it, and so does at least
        // one common cloud-sync client for files not yet fully downloaded) - silently excluding those
        // from every future scan would look identical to "Nudge isn't picking up files I added",
        // which is exactly the failure mode this whole rewrite exists to close, not reopen a new way
        // to hit.
        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = 0
        };

        List<string> files;
        try
        {
            files = _fileSystem.Directory
                .EnumerateFiles(tablesPath, "*.vpx", enumerationOptions)
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
            await repository.GetFingerprintsAsync(installationId, cancellationToken).ConfigureAwait(false);

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
                await repository.UpsertManyAsync(installationId, pendingBatch, cancellationToken).ConfigureAwait(false);
                pendingBatch.Clear();
            }
        }

        if (pendingBatch.Count > 0)
        {
            await repository.UpsertManyAsync(installationId, pendingBatch, cancellationToken).ConfigureAwait(false);
        }

        int removed = await repository.DeleteMissingAsync(installationId, currentPaths, cancellationToken)
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
