namespace Nudge.Core.Models;

/// <summary>
/// A keyboard key Nudge can synthesize on the controller-to-keyboard path, named for what it means
/// rather than for a Win32 virtual-key constant - the P/Invoke layer that actually presses keys
/// (Nudge.Vpx, not referenced here) is the only place that needs to know the real VK codes.
/// Deliberately just the keys Visual Pinball's own default bindings use (see
/// docs/RESEARCH-NOTES.md); a user free to remap in VPX itself can also remap
/// <see cref="ControllerMapping"/> to reach any of these for any button.
/// </summary>
public enum VirtualKey
{
    LeftShift,
    RightShift,
    Enter,
    Digit1,
    Digit5,
    Space,
    Z,
    Slash,
    LeftControl,
    RightControl,
    Escape
}
