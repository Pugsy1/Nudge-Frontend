using System.Diagnostics;

namespace Nudge.Vpx.Launching;

/// <inheritdoc cref="IProcessRunner" />
public sealed class ProcessRunner : IProcessRunner
{
    public async Task<int> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken,
        Action<int>? onProcessStarted = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false
        };

        // ArgumentList, not a manually-built argument string: it handles quoting/escaping itself,
        // so a table path containing spaces or quotes can never be misparsed or break out of its
        // argument the way hand-built quoting could.
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };

        if (!process.Start())
        {
            throw new InvalidOperationException($"'{fileName}' did not start.");
        }

        onProcessStarted?.Invoke(process.Id);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return process.ExitCode;
    }
}
