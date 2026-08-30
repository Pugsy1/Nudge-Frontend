namespace Nudge.Vpx.Launching;

/// <summary>
/// Thin wrapper over <see cref="System.Diagnostics.Process"/> so <c>LaunchEngine</c> is testable
/// without actually starting a real executable - the same role <c>IFileSystem</c> plays for disk
/// access elsewhere in Nudge.Vpx.
/// </summary>
public interface IProcessRunner
{
    /// <summary>
    /// Starts <paramref name="fileName"/> with <paramref name="arguments"/> and returns once it
    /// exits. Cancelling <paramref name="cancellationToken"/> stops *waiting* - it never kills the
    /// child process, since Nudge does not own the user's play session once Visual Pinball is
    /// running (see AGENTS.md section 6, "Execution").
    /// </summary>
    /// <param name="onProcessStarted">
    /// Invoked once, synchronously, with the started process's id, right after it starts - before
    /// this method awaits its exit. Lets a caller (e.g. <c>ILaunchEngine</c>, to watch for the
    /// table's window becoming ready) act on the running process without needing its own copy of
    /// the start/wait logic.
    /// </param>
    Task<int> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken,
        Action<int>? onProcessStarted = null);
}
