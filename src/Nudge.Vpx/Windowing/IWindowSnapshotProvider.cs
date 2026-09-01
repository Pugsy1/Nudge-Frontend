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

    /// <summary>
    /// Whether the window the user is currently working in already belongs to
    /// <paramref name="processId"/>.
    ///
    /// Exists so Nudge can decline to re-order a process's own windows once it has arrived on its
    /// own. Visual Pinball opens more than one window - the playfield and, when it is turned on, the
    /// score display - and it decides how they sit relative to each other. Forcing one of them to the
    /// foreground rearranges that: the playfield comes up over the DMD and hides it.
    /// </summary>
    bool IsForeground(int processId);
}
