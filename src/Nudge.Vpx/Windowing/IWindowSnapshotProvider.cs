namespace Nudge.Vpx.Windowing;

/// <summary>
/// Looks up windows belonging to a process. Behind an interface so <see cref="TableWindowWatcher"/>'s
/// polling/debounce logic is testable without real OS windows.
/// </summary>
public interface IWindowSnapshotProvider
{
    /// <summary>
    /// The handle of a visible top-level window belonging to <paramref name="processId"/> whose
    /// client area is at least <paramref name="minimumWidth"/> by <paramref name="minimumHeight"/>,
    /// or null if no such window currently exists. The minimum size exists to skip past a process's
    /// own small/placeholder windows (a hidden helper window, a not-yet-resized initial window) and
    /// only recognise something that looks like the real playfield.
    /// </summary>
    IntPtr? FindReadyWindow(int processId, int minimumWidth, int minimumHeight);
}
