namespace Nudge.Data.Entities;

/// <summary>
/// The persisted shape of one scanned table. Deliberately separate from
/// <see cref="Nudge.Core.Models.VpxTableFile"/> - that record is Core's I/O-free domain model,
/// produced fresh by a scan; this is EF Core's mutable, storage-shaped mapping of the same data.
/// <see cref="Nudge.Data.Repositories.TableRepository"/> converts between the two so neither Core
/// nor the rest of the application needs to know EF Core exists.
/// </summary>
public sealed class TableEntity
{
    public int Id { get; set; }

    /// <summary>Which Visual Pinball installation this table belongs to (<c>VpxInstallation.Id</c> from Phase 1).</summary>
    public required string InstallationId { get; set; }

    /// <summary>Full path. Unique per installation - see the index configured in <c>NudgeDbContext</c>.</summary>
    public required string FilePath { get; set; }

    public required string FileName { get; set; }

    public long FileSizeBytes { get; set; }

    /// <summary>The file's own last-write time, used to detect changes without re-reading the file.</summary>
    public DateTimeOffset FileLastWriteTimeUtc { get; set; }

    public DateTimeOffset LastScannedAt { get; set; }

    // --- Raw OLE TableInfo fields -------------------------------------------------------------
    public string? TableInfoTableName { get; set; }
    public string? TableInfoAuthorName { get; set; }
    public string? TableInfoAuthorEmail { get; set; }
    public string? TableInfoAuthorWebSite { get; set; }
    public string? TableInfoReleaseDate { get; set; }
    public string? TableInfoVersion { get; set; }
    public string? TableInfoBlurb { get; set; }
    public string? TableInfoDescription { get; set; }
    public string? TableInfoRules { get; set; }

    // --- Raw filename hints ---------------------------------------------------------------------
    public string? FilenameTitle { get; set; }
    public string? FilenameManufacturer { get; set; }
    public int? FilenameYear { get; set; }

    /// <summary>JSON array of strings. Kept as JSON rather than a related table - tags have no
    /// identity of their own and are never queried individually.</summary>
    public string? FilenameTagsJson { get; set; }

    // --- Reconciled display fields --------------------------------------------------------------
    public required string DisplayTitle { get; set; }
    public string? DisplayManufacturer { get; set; }
    public int? DisplayYear { get; set; }

    /// <summary>Stored as its enum name (a string column), so the database stays readable without the app.</summary>
    public required string Confidence { get; set; }

    /// <summary>JSON array of evidence items - see <see cref="Nudge.Core.Models.DetectionEvidence"/>.</summary>
    public string? EvidenceJson { get; set; }
}
