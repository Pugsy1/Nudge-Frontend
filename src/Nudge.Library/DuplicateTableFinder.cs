using System.IO.Abstractions;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nudge.Core.Abstractions;
using Nudge.Core.Diagnostics;
using Nudge.Core.Models;

namespace Nudge.Library;

/// <inheritdoc cref="IDuplicateTableFinder" />
public sealed class DuplicateTableFinder : IDuplicateTableFinder
{
    private readonly IFileSystem _fileSystem;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPathRedactor _redactor;
    private readonly ILogger<DuplicateTableFinder> _logger;

    /// <remarks>
    /// Takes an <see cref="IServiceScopeFactory"/> rather than an <see cref="ITableRepository"/>
    /// directly, for the same reason <c>VpxLibraryScanner</c> does - this class is a DI singleton,
    /// but <c>ITableRepository</c> is Scoped.
    /// </remarks>
    public DuplicateTableFinder(
        IFileSystem fileSystem,
        IServiceScopeFactory scopeFactory,
        IPathRedactor redactor,
        ILogger<DuplicateTableFinder> logger)
    {
        _fileSystem = fileSystem;
        _scopeFactory = scopeFactory;
        _redactor = redactor;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DuplicateTableGroup>> FindDuplicatesAsync(
        string installationId,
        IProgress<DuplicateScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        ITableRepository repository = scope.ServiceProvider.GetRequiredService<ITableRepository>();

        IReadOnlyList<VpxTableFile> allTables = await repository
            .GetAllAsync(installationId, cancellationToken)
            .ConfigureAwait(false);

        // Two files that are truly identical must be the same size - grouping by the size the
        // routine scan already recorded costs nothing and rules out the overwhelming majority of a
        // library before a single byte is hashed. Only a table sharing its size with at least one
        // other table is ever actually read.
        List<IGrouping<long, VpxTableFile>> sizeGroups = allTables
            .GroupBy(t => t.FileSizeBytes)
            .Where(g => g.Count() > 1)
            .ToList();

        int totalToHash = sizeGroups.Sum(g => g.Count());
        int completed = 0;
        var duplicateGroups = new List<DuplicateTableGroup>();

        foreach (IGrouping<long, VpxTableFile> sizeGroup in sizeGroups)
        {
            var byHash = new Dictionary<string, List<VpxTableFile>>(StringComparer.Ordinal);

            foreach (VpxTableFile table in sizeGroup)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new DuplicateScanProgress(completed, totalToHash, table.FileName));

                string? hash = await TryHashAsync(table.Path, cancellationToken).ConfigureAwait(false);
                completed++;

                if (hash is null)
                {
                    continue;
                }

                if (!byHash.TryGetValue(hash, out List<VpxTableFile>? matches))
                {
                    byHash[hash] = matches = [];
                }

                matches.Add(table);
            }

            duplicateGroups.AddRange(byHash.Values
                .Where(matches => matches.Count > 1)
                .Select(matches => new DuplicateTableGroup { Tables = matches }));
        }

        progress?.Report(new DuplicateScanProgress(totalToHash, totalToHash, string.Empty));

        _logger.LogInformation(
            "Duplicate scan for {InstallationId} found {GroupCount} group(s) of identical tables, after hashing {HashedCount} candidate file(s).",
            installationId,
            duplicateGroups.Count,
            totalToHash);

        return duplicateGroups;
    }

    private async Task<string?> TryHashAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            using Stream stream = _fileSystem.File.OpenRead(path);
            byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            return Convert.ToHexString(hash);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable file (locked, moved mid-scan, permissions) is skipped rather than
            // failing the whole duplicate search - the same "don't let one bad file stop everything
            // else" philosophy the routine scan already follows.
            _logger.LogDebug(ex, "Could not hash {Path} while looking for duplicates.", _redactor.Redact(path));
            return null;
        }
    }
}
