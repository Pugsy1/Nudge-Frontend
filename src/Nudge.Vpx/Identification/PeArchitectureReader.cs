using System.IO.Abstractions;
using System.Reflection.PortableExecutable;
using Microsoft.Extensions.Logging;
using Nudge.Core.Diagnostics;
using Nudge.Core.Models;
using Nudge.Core.Results;

namespace Nudge.Vpx.Identification;

/// <summary>
/// Reads the machine architecture out of an executable's PE header.
/// </summary>
public interface IPeArchitectureReader
{
    /// <summary>
    /// Returns the architecture recorded in the file's COFF header. Fails when the file is not a
    /// readable PE image. Never falls back to the filename.
    /// </summary>
    Task<Result<ProcessorArchitecture>> ReadArchitectureAsync(string executablePath, CancellationToken cancellationToken = default);
}

/// <summary>
/// PE header reader built on <see cref="PEReader"/> from the base class library.
///
/// Filenames lie. "VPinballX_GL64.exe" is conventionally the 64-bit OpenGL build, but a user can
/// rename anything, and some distributions ship a 32-bit binary under a 64-bit-looking name. The
/// COFF machine field is the only statement the file itself makes about its architecture, so that
/// is what Nudge reports.
/// </summary>
public sealed class PeArchitectureReader : IPeArchitectureReader
{
    private readonly IFileSystem _fileSystem;
    private readonly IPathRedactor _redactor;
    private readonly ILogger<PeArchitectureReader> _logger;

    public PeArchitectureReader(
        IFileSystem fileSystem,
        IPathRedactor redactor,
        ILogger<PeArchitectureReader> logger)
    {
        _fileSystem = fileSystem;
        _redactor = redactor;
        _logger = logger;
    }

    public Task<Result<ProcessorArchitecture>> ReadArchitectureAsync(
        string executablePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // PEReader is synchronous and reads only the first few hundred bytes of header. Pushing it
        // onto the thread pool keeps the UI thread free without pretending the API is async.
        return Task.Run(() => ReadArchitecture(executablePath), cancellationToken);
    }

    private Result<ProcessorArchitecture> ReadArchitecture(string executablePath)
    {
        try
        {
            using Stream stream = _fileSystem.File.OpenRead(executablePath);

            // PEReader needs a seekable stream. Mock filesystems and real files both give us one,
            // but a network stream would not, so copy defensively if it ever is not.
            Stream seekable = stream.CanSeek ? stream : CopyToMemory(stream);

            try
            {
                using var peReader = new PEReader(seekable, PEStreamOptions.LeaveOpen);
                Machine machine = peReader.PEHeaders.CoffHeader.Machine;
                return Result<ProcessorArchitecture>.Success(MapMachine(machine));
            }
            finally
            {
                if (!ReferenceEquals(seekable, stream))
                {
                    seekable.Dispose();
                }
            }
        }
        catch (Exception ex) when (ex is BadImageFormatException or IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(
                ex,
                "Could not read a PE header from {ExecutablePath}",
                _redactor.Redact(executablePath));

            return Result<ProcessorArchitecture>.Failure(
                $"'{_fileSystem.Path.GetFileName(executablePath)}' is not a readable Windows executable.");
        }
    }

    private static Stream CopyToMemory(Stream source)
    {
        var buffer = new MemoryStream();
        source.CopyTo(buffer);
        buffer.Position = 0;
        return buffer;
    }

    /// <summary>
    /// Maps the COFF machine field. Anything Nudge does not explicitly understand becomes Unknown
    /// rather than being lumped in with x86 or x64.
    /// </summary>
    internal static ProcessorArchitecture MapMachine(Machine machine) => machine switch
    {
        Machine.I386 => ProcessorArchitecture.X86,
        Machine.Amd64 => ProcessorArchitecture.X64,
        _ => ProcessorArchitecture.Unknown
    };
}
