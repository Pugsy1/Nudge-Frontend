namespace Nudge.Core.Models;

/// <summary>
/// A single logical Xbox-style controller input, independent of any specific pad's driver or byte
/// layout. <see cref="ControllerMapping"/> maps each of these to a <see cref="VirtualKey"/>.
/// </summary>
public enum ControllerButton
{
    DPadUp,
    DPadDown,
    DPadLeft,
    DPadRight,
    Start,
    Back,
    LeftThumb,
    RightThumb,
    LeftShoulder,
    RightShoulder,
    LeftTrigger,
    RightTrigger,
    A,
    B,
    X,
    Y
}
