using System.IO.Abstractions;
using System.Text;
using Microsoft.Extensions.Logging;
using Nudge.Core.Diagnostics;
using Nudge.Core.Results;
using OpenMcdf;

namespace Nudge.Vpx.TableFiles;

/// <summary>Extracts the raw VBScript source text from a <c>.vpx</c> file's <c>GameStg\GameData</c> stream.</summary>
public interface IGameDataScriptReader
{
    /// <summary>
    /// Succeeds with an empty string when the file opens but no script could be found (no
    /// <c>GameStg</c>/<c>GameData</c>, or no <c>CODE</c> record inside it) - an ordinary outcome for
    /// an unusual file, not a failure. Fails only when the file cannot be opened as an OLE compound
    /// document at all.
    /// </summary>
    Task<Result<string>> ReadScriptAsync(string path, CancellationToken cancellationToken = default);
}

/// <summary>
/// <c>GameStg\GameData</c> is a sequence of BIFF-style tagged records: a 4-byte little-endian length
/// (covering the 4-byte tag plus whatever payload follows), then the 4-byte tag itself, then the
/// payload. The table's script lives in the "CODE" record, whose payload is itself a 4-byte length
/// followed by that many bytes of text, decoded as UTF-8 when valid and Latin-1 otherwise. A record
/// tagged "ENDB" marks the end of the stream.
///
/// This format is not documented in vpinball's own repository or shipped docs. It was cross-checked
/// against the open-source community projects github.com/francisdb/vpin and
/// github.com/francisdb/vpxtool (which read and write real <c>.vpx</c> files this same way), then
/// independently verified here against four real, independently-authored table files from the
/// maintainer's test collection - see docs/RESEARCH-NOTES.md.
/// </summary>
public sealed class GameDataScriptReader : IGameDataScriptReader
{
    private const string GameStgStorageName = "GameStg";
    private const string GameDataStreamName = "GameData";
    private const string CodeTag = "CODE";
    private const string EndTag = "ENDB";
    private const int TagLength = 4;
    private const int LengthPrefixSize = 4;

    private readonly IFileSystem _fileSystem;
    private readonly IPathRedactor _redactor;
    private readonly ILogger<GameDataScriptReader> _logger;

    public GameDataScriptReader(IFileSystem fileSystem, IPathRedactor redactor, ILogger<GameDataScriptReader> logger)
    {
        _fileSystem = fileSystem;
        _redactor = redactor;
        _logger = logger;
    }

    public Task<Result<string>> ReadScriptAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_fileSystem.File.Exists(path))
        {
            return Task.FromResult(Result<string>.Failure($"'{path}' does not exist."));
        }

        return Task.Run(() => Read(path), cancellationToken);
    }

    private Result<string> Read(string path)
    {
        RootStorage rootStorage;
        try
        {
            // Opened through IFileSystem, then handed to OpenMcdf - the same approach
            // OleTableInfoReader uses, so this works against fake filesystems in tests exactly as it
            // does against a real disk.
            System.IO.Stream fileStream = _fileSystem.File.OpenRead(path);
            rootStorage = RootStorage.Open(fileStream, StorageModeFlags.None);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not open {Path} as an OLE compound document.", _redactor.Redact(path));
            return Result<string>.Failure($"'{_fileSystem.Path.GetFileName(path)}' is not a readable .vpx file.");
        }

        using (rootStorage)
        {
            byte[] buffer;
            try
            {
                Storage gameStg = rootStorage.OpenStorage(GameStgStorageName);
                using CfbStream gameData = gameStg.OpenStream(GameDataStreamName);
                buffer = new byte[gameData.Length];
                gameData.ReadExactly(buffer);
            }
            catch (Exception ex)
            {
                // A valid OLE file with no GameStg\GameData stream is not a normal .vpx table, but
                // the file DID open - reported as "no script found" rather than a hard failure.
                _logger.LogDebug(
                    ex,
                    "{Path} has no GameStg\\GameData stream.",
                    _redactor.Redact(path));
                return Result<string>.Success(string.Empty);
            }

            return Result<string>.Success(ExtractScript(buffer, path));
        }
    }

    private string ExtractScript(byte[] buffer, string path)
    {
        int pos = 0;
        while (pos + LengthPrefixSize <= buffer.Length)
        {
            int recordLength = BitConverter.ToInt32(buffer, pos);
            pos += LengthPrefixSize;

            if (recordLength < TagLength || pos + recordLength > buffer.Length)
            {
                _logger.LogDebug(
                    "GameData in {Path} has a malformed record at offset {Offset}; stopping the search for a script.",
                    _redactor.Redact(path),
                    pos - LengthPrefixSize);
                break;
            }

            string tag = Encoding.ASCII.GetString(buffer, pos, TagLength);

            if (tag == EndTag)
            {
                break;
            }

            if (tag == CodeTag)
            {
                int payloadStart = pos + TagLength;
                int payloadLength = recordLength - TagLength;

                if (payloadLength < LengthPrefixSize)
                {
                    break;
                }

                int innerLength = BitConverter.ToInt32(buffer, payloadStart);
                int textStart = payloadStart + LengthPrefixSize;

                if (innerLength < 0 || textStart + innerLength > buffer.Length)
                {
                    break;
                }

                return DecodeText(buffer, textStart, innerLength);
            }

            pos += recordLength;
        }

        // No CODE record found - an ordinary outcome for an unusually-shaped file, not a failure.
        // The caller (RomNameParser) treats an empty script as "no ROM name found".
        return string.Empty;
    }

    private static string DecodeText(byte[] buffer, int start, int length)
    {
        try
        {
            var strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            return strictUtf8.GetString(buffer, start, length);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(buffer, start, length);
        }
    }
}
