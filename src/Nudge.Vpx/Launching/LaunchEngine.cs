using System.ComponentModel;
using System.Diagnostics;
using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using Nudge.Core.Abstractions;
using Nudge.Core.Diagnostics;
using Nudge.Core.Models;
using Nudge.Core.Results;

namespace Nudge.Vpx.Launching;

/// <inheritdoc cref="ILaunchEngine" />
public sealed class LaunchEngine : ILaunchEngine
{
    private readonly IProcessRunner _processRunner;
    private readonly IFileSystem _fileSystem;
    private readonly IPathRedactor _redactor;
    private readonly ILogger<LaunchEngine> _logger;

    public LaunchEngine(
        IProcessRunner processRunner,
        IFileSystem fileSystem,
        IPathRedactor redactor,
        ILogger<LaunchEngine> logger)
    {
        _processRunner = processRunner;
        _fileSystem = fileSystem;
        _redactor = redactor;
        _logger = logger;
    }

    public async Task<Result<LaunchOutcome>> LaunchAsync(
        VpxInstallation installation,
        string tablePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installation);
        ArgumentException.ThrowIfNullOrWhiteSpace(tablePath);

        if (!_fileSystem.File.Exists(tablePath))
        {
            return Result<LaunchOutcome>.Failure("That table file no longer exists on disk.");
        }

        VpxExecutable? executable = installation.BestDesktopExecutable;
        if (executable is null)
        {
            return Result<LaunchOutcome>.Failure(
                "Nudge couldn't find a Visual Pinball build in this installation that can play .vpx tables.");
        }

        // The complete supported command-line set is documented in AGENTS.md section 4.2. "-Play"
        // is the launch verb; there is no "-Desktop" flag to pass alongside it - a Desktop session is
        // simply what you get without an "-Ini" pointing at a VR profile (Phase 6's job).
        string[] arguments = ["-Play", tablePath];
        string workingDirectory = _fileSystem.Path.GetDirectoryName(executable.Path) ?? installation.RootPath;

        _logger.LogInformation(
            "Launching {Executable} to play {Table}.",
            executable.FileName,
            _redactor.Redact(tablePath));

        var stopwatch = Stopwatch.StartNew();
        int exitCode;
        try
        {
            exitCode = await _processRunner
                .RunAsync(executable.Path, arguments, workingDirectory, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            _logger.LogError(ex, "Could not launch {Executable}.", executable.FileName);
            return Result<LaunchOutcome>.Failure(
                $"Nudge could not start {executable.FileName}. The log file has the details.");
        }

        stopwatch.Stop();

        _logger.LogInformation(
            "{Executable} exited with code {ExitCode} after {ElapsedMs} ms.",
            executable.FileName,
            exitCode,
            stopwatch.ElapsedMilliseconds);

        return Result<LaunchOutcome>.Success(new LaunchOutcome
        {
            ExecutablePath = executable.Path,
            ExitCode = exitCode,
            Duration = stopwatch.Elapsed
        });
    }
}
