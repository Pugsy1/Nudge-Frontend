using Nudge.Core.Models;

namespace Nudge.Vpx.Controller;

/// <summary>
/// Reads one Xbox-style controller's current button state. Behind an interface so
/// <see cref="ControllerInputService"/> is testable without a real pad plugged in.
/// </summary>
public interface IControllerReader
{
    /// <summary>
    /// True and the controller's current state if a controller is connected at
    /// <paramref name="controllerIndex"/> (0-3), false if nothing is connected there.
    /// </summary>
    bool TryGetState(int controllerIndex, out ControllerState state);
}
