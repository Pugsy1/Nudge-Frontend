using System.Diagnostics;
using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using Nudge.Core.Diagnostics;

namespace Nudge.Vpx.Identification;

/// <summary>
/// Reads the Win32 version resource from an executable.
/// </summary>
public interface IFileVersionInfoReader
{
    /// <summary>
    /// Returns what the version resource says. Returns <see cref="FileVersionDetails.Empty"/> rather
    /// than failing when there is no version resource, because that is a normal thing for a file to
    /// lack and is itself a piece of evidence.
    /// </summary>
    Task<FileVersionDetails> ReadAsync(string executablePath, CancellationToken cancellationToken = default);
}

/// <summary>
/// Version resource reader backed by <see cref="FileVersionInfo"/>.
///
/// This is the one place in Nudge that touches a real path instead of <c>IFileSystem</c>. Win32
/// version resources can only be read through the operating system, which knows nothing about an
/// in-memory test filesystem. The interface above is what tests substitute; this implementation is
/// exercised against real files during manual testing only.
/// </summary>
public sealed class FileVersionInfoReader : IFileVersionInfoReader
{
    private readonly IFileSystem _fileSystem;
    private readonly IPathRedactor _redactor;
    private readonly ILogger<FileVersionInfoReader> _logger;

    public FileVersionInfoReader(
        IFileSystem fileSystem,
        IPathRedactor redactor,
        ILogger<FileVersionInfoReader> logger)
    {
        _fileSystem = fileSystem;
        _redactor = redactor;
        _logger = logger;
    }

    public Task<FileVersionDetails> ReadAsync(string executablePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => Read(executablePath), cancellationToken);
    }

    private FileVersionDetails Read(string executablePath)
    {
        // If the abstraction says the file is not there, do not ask Win32 about it.
        if (!_fileSystem.File.Exists(executablePath))
        {
            return FileVersionDetails.Empty;
        }

        try
        {
            FileVersionInfo info = FileVersionInfo.GetVersionInfo(executablePath);

            return new FileVersionDetails
            {
                FileVersion = NullIfBlank(info.FileVersion),
                ProductVersion = NullIfBlank(info.ProductVersion),
                ProductName = NullIfBlank(info.ProductName),
                FileDescription = NullIfBlank(info.FileDescription),
                CompanyName = NullIfBlank(info.CompanyName),
                OriginalFilename = NullIfBlank(info.OriginalFilename),
                InternalName = NullIfBlank(info.InternalName),
                NumericFileVersion = BuildNumericVersion(info)
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _logger.LogDebug(
                ex,
                "Could not read a version resource from {ExecutablePath}",
                _redactor.Redact(executablePath));

            return FileVersionDetails.Empty;
        }
    }

    /// <summary>
    /// Builds a Version from the numeric fields. These are more trustworthy than the FileVersion
    /// string, which build systems often fill with free text such as "10.8.0 Final".
    /// </summary>
    private static Version? BuildNumericVersion(FileVersionInfo info)
    {
        if (info.FileMajorPart == 0
            && info.FileMinorPart == 0
            && info.FileBuildPart == 0
            && info.FilePrivatePart == 0)
        {
            return null;
        }

        return new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart);
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
