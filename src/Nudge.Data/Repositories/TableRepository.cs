using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nudge.Core.Abstractions;
using Nudge.Core.Models;
using Nudge.Data.Entities;

namespace Nudge.Data.Repositories;

/// <summary>
/// EF Core / SQLite implementation of <see cref="ITableRepository"/>. Converts between the
/// I/O-free <see cref="VpxTableFile"/> Nudge.Core and Nudge.Vpx work with, and the mutable,
/// storage-shaped <see cref="TableEntity"/> EF Core persists - see the type doc on
/// <see cref="TableEntity"/> for why the two are kept separate.
/// </summary>
public sealed class TableRepository : ITableRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly NudgeDbContext _dbContext;

    public TableRepository(NudgeDbContext dbContext) => _dbContext = dbContext;

    public Task UpsertAsync(string installationId, TableScanEntry entry, CancellationToken cancellationToken = default) =>
        UpsertManyAsync(installationId, [entry], cancellationToken);

    public async Task UpsertManyAsync(
        string installationId,
        IReadOnlyList<TableScanEntry> entries,
        CancellationToken cancellationToken = default)
    {
        if (entries.Count == 0)
        {
            return;
        }

        // One query for every existing row this batch might touch, rather than one query per
        // entry - this is what keeps a large scan's database work to a handful of round trips
        // instead of thousands.
        HashSet<string> paths = entries.Select(e => e.Table.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, TableEntity> existingByPath = await _dbContext.Tables
            .Where(t => t.InstallationId == installationId && paths.Contains(t.FilePath))
            .ToDictionaryAsync(t => t.FilePath, StringComparer.OrdinalIgnoreCase, cancellationToken)
            .ConfigureAwait(false);

        foreach (TableScanEntry entry in entries)
        {
            if (existingByPath.TryGetValue(entry.Table.Path, out TableEntity? existing))
            {
                ApplyTo(existing, entry.Table, entry.FileSizeBytes, entry.FileLastWriteTimeUtc);
            }
            else
            {
                _dbContext.Tables.Add(ToNewEntity(installationId, entry.Table, entry.FileSizeBytes, entry.FileLastWriteTimeUtc));
            }
        }

        // A single SaveChanges commits the whole batch in one transaction, per AGENTS.md's
        // batched-writes performance requirement - not one transaction per row.
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<string, ScannedFileFingerprint>> GetFingerprintsAsync(
        string installationId,
        CancellationToken cancellationToken = default)
    {
        List<FingerprintProjection> rows = await _dbContext.Tables
            .Where(t => t.InstallationId == installationId)
            .Select(t => new FingerprintProjection(t.FilePath, t.FileSizeBytes, t.FileLastWriteTimeUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.ToDictionary(
            t => t.FilePath,
            t => new ScannedFileFingerprint(t.FileSizeBytes, t.FileLastWriteTimeUtc),
            StringComparer.OrdinalIgnoreCase);
    }

    private sealed record FingerprintProjection(string FilePath, long FileSizeBytes, DateTimeOffset FileLastWriteTimeUtc);

    public async Task<IReadOnlyList<VpxTableFile>> GetAllAsync(
        string installationId,
        CancellationToken cancellationToken = default)
    {
        List<TableEntity> rows = await _dbContext.Tables
            .Where(t => t.InstallationId == installationId)
            .OrderBy(t => t.DisplayTitle)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(ToTableFile).ToList();
    }

    public async Task<int> DeleteMissingAsync(
        string installationId,
        IReadOnlySet<string> currentPaths,
        CancellationToken cancellationToken = default)
    {
        List<TableEntity> stored = await _dbContext.Tables
            .Where(t => t.InstallationId == installationId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<TableEntity> toRemove = stored
            .Where(t => !currentPaths.Contains(t.FilePath))
            .ToList();

        if (toRemove.Count == 0)
        {
            return 0;
        }

        _dbContext.Tables.RemoveRange(toRemove);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return toRemove.Count;
    }

    private static TableEntity ToNewEntity(
        string installationId,
        VpxTableFile table,
        long fileSizeBytes,
        DateTimeOffset fileLastWriteTimeUtc)
    {
        var entity = new TableEntity
        {
            InstallationId = installationId,
            FilePath = table.Path,
            FileName = table.FileName,
            DisplayTitle = table.DisplayTitle,
            Confidence = table.Confidence.ToString()
        };

        ApplyTo(entity, table, fileSizeBytes, fileLastWriteTimeUtc);
        return entity;
    }

    private static void ApplyTo(
        TableEntity entity,
        VpxTableFile table,
        long fileSizeBytes,
        DateTimeOffset fileLastWriteTimeUtc)
    {
        entity.FileName = table.FileName;
        entity.FileSizeBytes = fileSizeBytes;
        entity.FileLastWriteTimeUtc = fileLastWriteTimeUtc;
        entity.LastScannedAt = DateTimeOffset.Now;

        entity.TableInfoTableName = table.TableInfo.TableName;
        entity.TableInfoAuthorName = table.TableInfo.AuthorName;
        entity.TableInfoAuthorEmail = table.TableInfo.AuthorEmail;
        entity.TableInfoAuthorWebSite = table.TableInfo.AuthorWebSite;
        entity.TableInfoReleaseDate = table.TableInfo.ReleaseDate;
        entity.TableInfoVersion = table.TableInfo.TableVersion;
        entity.TableInfoBlurb = table.TableInfo.TableBlurb;
        entity.TableInfoDescription = table.TableInfo.TableDescription;
        entity.TableInfoRules = table.TableInfo.TableRules;

        entity.FilenameTitle = table.FilenameHints.Title;
        entity.FilenameManufacturer = table.FilenameHints.Manufacturer;
        entity.FilenameYear = table.FilenameHints.Year;
        entity.FilenameTagsJson = table.FilenameHints.Tags.Count > 0
            ? JsonSerializer.Serialize(table.FilenameHints.Tags, JsonOptions)
            : null;

        entity.DisplayTitle = table.DisplayTitle;
        entity.DisplayManufacturer = table.DisplayManufacturer;
        entity.DisplayYear = table.DisplayYear;
        entity.Confidence = table.Confidence.ToString();
        entity.EvidenceJson = table.Evidence.Count > 0
            ? JsonSerializer.Serialize(table.Evidence.ToList(), JsonOptions)
            : null;
    }

    private static VpxTableFile ToTableFile(TableEntity entity)
    {
        var evidence = DetectionEvidence.Empty();
        if (!string.IsNullOrEmpty(entity.EvidenceJson))
        {
            List<EvidenceItem>? items = JsonSerializer.Deserialize<List<EvidenceItem>>(entity.EvidenceJson, JsonOptions);
            if (items is not null)
            {
                evidence.AddRange(items);
            }
        }

        IReadOnlyList<string> tags = [];
        if (!string.IsNullOrEmpty(entity.FilenameTagsJson))
        {
            tags = JsonSerializer.Deserialize<List<string>>(entity.FilenameTagsJson, JsonOptions) ?? [];
        }

        return new VpxTableFile
        {
            Path = entity.FilePath,
            FileName = entity.FileName,
            FileSizeBytes = entity.FileSizeBytes,
            TableInfo = new TableInfoMetadata
            {
                TableName = entity.TableInfoTableName,
                AuthorName = entity.TableInfoAuthorName,
                AuthorEmail = entity.TableInfoAuthorEmail,
                AuthorWebSite = entity.TableInfoAuthorWebSite,
                ReleaseDate = entity.TableInfoReleaseDate,
                TableVersion = entity.TableInfoVersion,
                TableBlurb = entity.TableInfoBlurb,
                TableDescription = entity.TableInfoDescription,
                TableRules = entity.TableInfoRules
            },
            FilenameHints = new FilenameHints
            {
                Title = entity.FilenameTitle,
                Manufacturer = entity.FilenameManufacturer,
                Year = entity.FilenameYear,
                Tags = tags
            },
            DisplayTitle = entity.DisplayTitle,
            DisplayManufacturer = entity.DisplayManufacturer,
            DisplayYear = entity.DisplayYear,
            Confidence = Enum.Parse<Confidence>(entity.Confidence),
            Evidence = evidence
        };
    }
}
