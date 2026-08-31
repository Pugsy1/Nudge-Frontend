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
    Y,

    // The left stick pushed past its dead zone, reported as four discrete directions rather than as
    // an analog value. Everything that consumes a controller in Nudge wants discrete presses - moving
    // one tile, sending one keystroke - so the analog-to-digital decision is made once, in the
    // reader, instead of by every caller. LeftThumb/RightThumb above are the stick *click* buttons,
    // which are unrelated to these.
    LeftStickUp,
    LeftStickDown,
    LeftStickLeft,
    LeftStickRight
}
