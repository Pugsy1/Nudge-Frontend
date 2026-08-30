using Nudge.Core.Models;

namespace Nudge.Core.Abstractions;

/// <summary>
/// Translates an Xbox-style controller's input into synthesized keyboard presses for as long as the
/// returned session is not disposed - the mechanism behind "trick the computer into thinking the
/// trigger is Right Shift" (the maintainer's own framing) so a controller works with Visual Pinball
/// without VPX itself ever needing to know a controller exists. See docs/RESEARCH-NOTES.md.
/// </summary>
public interface IControllerInputService
{
    /// <summary>
    /// Starts translating controller input into key presses, but only while the foreground window
    /// belongs to a process named <paramref name="targetProcessName"/> (no extension, e.g.
    /// "VPinballX64") - so a controller plugged in for table play never leaks input into Nudge's own
    /// UI or any other application. Disposing the returned session stops translating and releases
    /// any key it was still holding down.
    /// </summary>
    IDisposable StartTranslating(string targetProcessName, ControllerMapping mapping);
}
