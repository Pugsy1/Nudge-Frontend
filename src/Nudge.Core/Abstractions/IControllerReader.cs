using Nudge.Core.Models;

namespace Nudge.Core.Abstractions;

/// <summary>
/// Reads one Xbox-style controller's current button state.
///
/// Lives in <c>Nudge.Core</c> rather than beside its XInput implementation because both halves of
/// Nudge's controller support consume it: <c>Nudge.Vpx</c> translates a pad into fake keystrokes for
/// a running Visual Pinball, and <c>Nudge.App</c> reads the same pad to drive the library's own
/// selection. The UI depends on contracts from Core only (AGENTS.md section 5), so a shared contract
/// has to live here for the second of those to be legitimate.
/// </summary>
public interface IControllerReader
{
    /// <summary>
    /// True and the controller's current state if a controller is connected at
    /// <paramref name="controllerIndex"/> (0-3), false if nothing is connected there.
    /// </summary>
    bool TryGetState(int controllerIndex, out ControllerState state);
}
