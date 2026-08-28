using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using Nudge.Core.Abstractions;
using Nudge.Core.Diagnostics;
using Nudge.Core.Models;
using Nudge.Core.Results;

namespace Nudge.Vpx.TableFiles;

/// <summary>
/// Combines a table file's OLE <c>TableInfo</c> metadata with its filename hints into a single
/// <see cref="VpxTableFile"/>.
///
/// Reconciling the two when they disagree follows the rule already recorded in
/// docs/RESEARCH-NOTES.md: <c>TableInfo</c> is frequently stale, because most tables in
/// circulation are mods of mods and the metadata is often inherited rather than updated. The
/// filename is preferred for the display title when both exist, but nothing is discarded - the raw
/// values from both sources are kept on the record, and a disagreement between them is recorded as
/// evidence rather than silently resolved.
/// </summary>
public sealed class VpxTableFileReader : ITableFileReader
{
    private readonly IFileSystem _fileSystem;
    private readonly IOleTableInfoReader _oleReader;
    private readonly ITableFilenameParser _filenameParser;
    private readonly IPathRedactor _redactor;
    private readonly ILogger<VpxTableFileReader> _logger;

    public VpxTableFileReader(
        IFileSystem fileSystem,
        IOleTableInfoReader oleReader,
        ITableFilenameParser filenameParser,
        IPathRedactor redactor,
        ILogger<VpxTableFileReader> logger)
    {
        _fileSystem = fileSystem;
        _oleReader = oleReader;
        _filenameParser = filenameParser;
        _redactor = redactor;
        _logger = logger;
    }

    public async Task<Result<VpxTableFile>> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!_fileSystem.File.Exists(path))
        {
            return Result<VpxTableFile>.Failure($"'{path}' does not exist.");
        }

        string fileName = _fileSystem.Path.GetFileName(path);
        var evidence = DetectionEvidence.Empty();

        Result<TableInfoMetadata> oleResult = await _oleReader.ReadAsync(path, cancellationToken)
            .ConfigureAwait(false);

        if (oleResult.IsFailure)
        {
            evidence.Add("OLE metadata", oleResult.Error, EvidenceWeight.Contradicting);
            return Result<VpxTableFile>.Failure(oleResult.Error);
        }

        TableInfoMetadata tableInfo = oleResult.Value;
        FilenameHints filenameHints = _filenameParser.Parse(fileName);

        RecordSourceEvidence(evidence, tableInfo, filenameHints);

        (string displayTitle, Confidence confidence) = ReconcileTitle(fileName, tableInfo, filenameHints, evidence);

        long sizeBytes;
        try
        {
            sizeBytes = _fileSystem.FileInfo.New(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not read file size for {Path}", _redactor.Redact(path));
            sizeBytes = 0;
        }

        var tableFile = new VpxTableFile
        {
            Path = path,
            FileName = fileName,
            FileSizeBytes = sizeBytes,
            TableInfo = tableInfo,
            FilenameHints = filenameHints,
            DisplayTitle = displayTitle,
            DisplayManufacturer = filenameHints.Manufacturer,
            DisplayYear = filenameHints.Year,
            Confidence = confidence,
            Evidence = evidence
        };

        return Result<VpxTableFile>.Success(tableFile);
    }

    private static void RecordSourceEvidence(
        DetectionEvidence evidence,
        TableInfoMetadata tableInfo,
        FilenameHints filenameHints)
    {
        evidence.Add(
            "TableInfo",
            tableInfo.IsEmpty
                ? "The table's own metadata is empty or missing."
                : $"The table's own metadata names it '{tableInfo.TableName ?? "(no name)"}'"
                  + (tableInfo.AuthorName is null ? "." : $", by {tableInfo.AuthorName}."),
            tableInfo.IsEmpty ? EvidenceWeight.Informational : EvidenceWeight.Supporting);

        evidence.Add(
            "Filename",
            filenameHints.IsEmpty
                ? "The filename does not follow a recognised naming convention."
                : BuildFilenameHintSummary(filenameHints),
            filenameHints.IsEmpty ? EvidenceWeight.Informational : EvidenceWeight.Supporting);
    }

    private static string BuildFilenameHintSummary(FilenameHints hints)
    {
        var parts = new List<string>();
        if (hints.Title is not null)
        {
            parts.Add($"title '{hints.Title}'");
        }

        if (hints.HasManufacturerYear)
        {
            parts.Add($"{hints.Manufacturer} {hints.Year}");
        }

        if (hints.Tags.Count > 0)
        {
            parts.Add("tags: " + string.Join(", ", hints.Tags));
        }

        return "The filename suggests " + string.Join(", ", parts) + ".";
    }

    /// <summary>
    /// Decides the single display title Nudge shows, and how confident it is in that choice.
    /// Filename wins on disagreement, per docs/RESEARCH-NOTES.md - but the disagreement itself is
    /// always recorded, so the user can see why.
    /// </summary>
    private static (string DisplayTitle, Confidence Confidence) ReconcileTitle(
        string fileName,
        TableInfoMetadata tableInfo,
        FilenameHints filenameHints,
        DetectionEvidence evidence)
    {
        string? oleTitle = string.IsNullOrWhiteSpace(tableInfo.TableName) ? null : tableInfo.TableName.Trim();
        string? filenameTitle = filenameHints.Title;

        if (oleTitle is not null && filenameTitle is not null)
        {
            if (NormalisedTitlesAgree(oleTitle, filenameTitle))
            {
                evidence.Add(
                    "Conclusion",
                    $"The table's metadata and the filename agree on the title '{filenameTitle}'.",
                    EvidenceWeight.Decisive);
                return (filenameTitle, Confidence.High);
            }

            evidence.Add(
                "Conclusion",
                $"The table's metadata calls itself '{oleTitle}', but the filename suggests "
                + $"'{filenameTitle}'. The filename is used, because table metadata is frequently "
                + "stale in mods of mods - but this disagreement is worth a look.",
                EvidenceWeight.Contradicting);
            return (filenameTitle, Confidence.Medium);
        }

        if (filenameTitle is not null)
        {
            evidence.Add(
                "Conclusion",
                $"Only the filename suggests a title: '{filenameTitle}'.",
                EvidenceWeight.Supporting);
            return (filenameTitle, Confidence.Medium);
        }

        if (oleTitle is not null)
        {
            evidence.Add(
                "Conclusion",
                $"Only the table's own metadata names it: '{oleTitle}'.",
                EvidenceWeight.Supporting);
            return (oleTitle, Confidence.Medium);
        }

        string fallback = fileName.EndsWith(".vpx", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^4]
            : fileName;

        evidence.Add(
            "Conclusion",
            "Neither the table's metadata nor the filename offered a usable title. Showing the raw filename.",
            EvidenceWeight.Contradicting);
        return (fallback, Confidence.Low);
    }

    /// <summary>Loose equality: same text once case, whitespace and punctuation are ignored.</summary>
    private static bool NormalisedTitlesAgree(string a, string b)
    {
        string na = Normalise(a);
        string nb = Normalise(b);
        return na.Length > 0 && (na == nb || na.Contains(nb, StringComparison.Ordinal) || nb.Contains(na, StringComparison.Ordinal));
    }

    private static string Normalise(string value) =>
        new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
}
