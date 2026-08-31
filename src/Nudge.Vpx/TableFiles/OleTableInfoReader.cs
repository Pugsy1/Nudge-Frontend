using System.IO.Abstractions;
using System.Text;
using Microsoft.Extensions.Logging;
using Nudge.Core.Diagnostics;
using Nudge.Core.Models;
using Nudge.Core.Results;
using OpenMcdf;

namespace Nudge.Vpx.TableFiles;

/// <summary>
/// Reads the <c>TableInfo</c> OLE storage out of a <c>.vpx</c> file.
/// </summary>
public interface IOleTableInfoReader
{
    /// <summary>
    /// Fails when the file cannot be opened as an OLE compound document at all - not a valid
    /// <c>.vpx</c> file, truncated, or something else entirely. Succeeds with a possibly-empty
    /// <see cref="TableInfoMetadata"/> when the file opens but individual fields are missing,
    /// which is the ordinary case for many real tables.
    /// </summary>
    Task<Result<TableInfoMetadata>> ReadAsync(string path, CancellationToken cancellationToken = default);
}

/// <summary>
/// Verified against real table files during Phase 2 development (see docs/RESEARCH-NOTES.md): the
/// <c>TableInfo</c> storage holds a small, fixed set of streams, each one plain UTF-16LE text with
/// no length prefix and no null terminator - the stream's own length is the string's length. This
/// held across every real table file checked; no other encoding has been observed.
///
/// <c>.vpx</c> files are typically tens to hundreds of megabytes, almost all of it images and
/// sound in the much larger <c>GameStg</c> storage. This reader never touches that storage - it
/// opens only the small <c>TableInfo</c> entries, which is what keeps a library scan fast.
/// </summary>
public sealed class OleTableInfoReader : IOleTableInfoReader
{
    private const string TableInfoStorageName = "TableInfo";

    private static readonly string[] StreamNames =
    [
        "TableName",
        "AuthorName",
        "AuthorEmail",
        "AuthorWebSite",
        "ReleaseDate",
        "TableVersion",
        "TableBlurb",
        "TableDescription",
        "TableRules"
    ];

    private readonly IFileSystem _fileSystem;
    private readonly IPathRedactor _redactor;
    private readonly ILogger<OleTableInfoReader> _logger;

    public OleTableInfoReader(IFileSystem fileSystem, IPathRedactor redactor, ILogger<OleTableInfoReader> logger)
    {
        _fileSystem = fileSystem;
        _redactor = redactor;
        _logger = logger;
    }

    public Task<Result<TableInfoMetadata>> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_fileSystem.File.Exists(path))
        {
            return Task.FromResult(Result<TableInfoMetadata>.Failure($"'{path}' does not exist."));
        }

        return Task.Run(() => Read(path), cancellationToken);
    }

    private Result<TableInfoMetadata> Read(string path)
    {
        RootStorage rootStorage;
        System.IO.Stream fileStream = _fileSystem.File.OpenRead(path);
        try
        {
            // Opened through IFileSystem, then handed to OpenMcdf as a stream, rather than letting
            // OpenMcdf open the path itself - the same approach PeArchitectureReader uses in Phase 1.
            // OLE compound file access needs random-access seeking, which a real FileStream and a
            // MockFileSystem's in-memory stream both support equally well, so this works against
            // fake filesystems in tests exactly as it does against a real disk.
            rootStorage = RootStorage.Open(fileStream, StorageModeFlags.None);
        }
        catch (Exception ex)
        {
            // RootStorage.Open only takes ownership of fileStream once it succeeds - on failure the
            // stream is still ours to close. Without this, every mis-named or corrupt file a user has
            // in their Tables folder leaks a FileStream handle on every single scan, forever (the
            // scanner never records a fingerprint for a file it failed to read, so it looks "new"
            // again next time and gets reopened rather than skipped).
            fileStream.Dispose();

            // OpenMcdf's failure modes for "this is not an OLE compound document" are not narrowly
            // documented, and the input here is an arbitrary file a user pointed Nudge at - it could
            // be anything. Any failure to open means Nudge cannot read this as a .vpx file; that is
            // an ordinary, expected outcome for a mis-named or corrupt file, not a bug to crash over.
            _logger.LogDebug(ex, "Could not open {Path} as an OLE compound document.", _redactor.Redact(path));
            return Result<TableInfoMetadata>.Failure(
                $"'{_fileSystem.Path.GetFileName(path)}' is not a readable .vpx file.");
        }

        using (rootStorage)
        {
            Storage tableInfo;
            try
            {
                tableInfo = rootStorage.OpenStorage(TableInfoStorageName);
            }
            catch (Exception ex)
            {
                // A valid OLE file with no TableInfo storage is not a .vpx table - could be some
                // other compound document entirely. Reported as empty metadata rather than a hard
                // failure, since the file DID open; callers combine this with filename hints anyway.
                _logger.LogDebug(
                    ex,
                    "{Path} has no TableInfo storage; not a Visual Pinball table file.",
                    _redactor.Redact(path));
                return Result<TableInfoMetadata>.Success(TableInfoMetadata.Empty);
            }

            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in StreamNames)
            {
                values[name] = ReadStreamAsText(tableInfo, name, path);
            }

            var metadata = new TableInfoMetadata
            {
                TableName = values["TableName"],
                AuthorName = values["AuthorName"],
                AuthorEmail = values["AuthorEmail"],
                AuthorWebSite = values["AuthorWebSite"],
                ReleaseDate = values["ReleaseDate"],
                TableVersion = values["TableVersion"],
                TableBlurb = values["TableBlurb"],
                TableDescription = values["TableDescription"],
                TableRules = values["TableRules"]
            };

            return Result<TableInfoMetadata>.Success(metadata);
        }
    }

    /// <summary>
    /// Reads one stream as UTF-16LE text. A stream that does not exist, or that has an odd byte
    /// length (not a whole number of UTF-16 code units, so not the format every real table has
    /// used), is treated as absent rather than guessed at.
    /// </summary>
    private string? ReadStreamAsText(Storage tableInfo, string streamName, string filePathForLogging)
    {
        CfbStream stream;
        try
        {
            stream = tableInfo.OpenStream(streamName);
        }
        catch (Exception)
        {
            // This particular field is simply absent from this table. Ordinary and common.
            return null;
        }

        using (stream)
        {
            if (stream.Length == 0)
            {
                return null;
            }

            if (stream.Length % 2 != 0)
            {
                _logger.LogDebug(
                    "Stream {StreamName} in {Path} has an odd byte length ({Length}); not treated as UTF-16 text.",
                    streamName,
                    _redactor.Redact(filePathForLogging),
                    stream.Length);
                return null;
            }

            var buffer = new byte[stream.Length];
            stream.ReadExactly(buffer);
            string text = Encoding.Unicode.GetString(buffer).Trim();
            return text.Length == 0 ? null : text;
        }
    }
}
