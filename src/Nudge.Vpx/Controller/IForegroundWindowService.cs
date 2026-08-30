namespace Nudge.Vpx.Controller;

/// <summary>
/// Identifies which process owns the current foreground window - how
/// <see cref="ControllerInputSession"/> decides whether it is safe to translate controller input
/// into key presses right now, so a controller plugged in for table play never leaks input into
/// Nudge's own UI or any other application the user alt-tabs to mid-session.
/// </summary>
public interface IForegroundWindowService
{
    /// <summary>
    /// The process name (no extension, e.g. "VPinballX64") that owns the current foreground window,
    /// or null if it cannot be determined.
    /// </summary>
    string? GetForegroundProcessName();
}
