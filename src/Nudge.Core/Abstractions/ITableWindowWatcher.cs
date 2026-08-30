namespace Nudge.Core.Abstractions;

/// <summary>
/// Waits for a launched process's real playfield window to actually appear on screen, then brings
/// it to the foreground - the mechanism behind "go from Nudge straight into the table I chose"
/// instead of watching Visual Pinball boot up. See docs/RESEARCH-NOTES.md for what "ready" means
/// and its limits (this can only observe a window becoming visible with a real size; it cannot see
/// inside VPX's own rendering to know a table has finished loading its assets).
/// </summary>
public interface ITableWindowWatcher
{
    /// <summary>
    /// Returns true once a visible, real-sized top-level window belonging to
    /// <paramref name="processId"/> was found and stayed stable for a short debounce window - the
    /// caller's real signal that the table is actually showing. A best-effort attempt is also made
    /// to bring that window to the foreground, but that attempt's own success or failure does not
    /// change this method's result: Windows' anti-focus-stealing rules can legitimately decline the
    /// request independently of whether the window itself is genuinely ready (in practice the
    /// window most often already has focus on its own by this point, since it belongs to a process
    /// Nudge itself just launched - see docs/RESEARCH-NOTES.md). Returns false only if the process
    /// exits first or no such window ever appears before an internal timeout.
    /// </summary>
    Task<bool> ActivateWhenReadyAsync(int processId, CancellationToken cancellationToken = default);
}
