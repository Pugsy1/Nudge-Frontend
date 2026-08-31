using System.Runtime.InteropServices;
using Nudge.Core.Abstractions;
using Nudge.Core.Models;

namespace Nudge.Vpx.Controller;

/// <summary>
/// Reads a real controller via XInput. Windows ships two versions of this DLL depending on the OS
/// and what's installed - xinput1_4 (Windows 8+) is preferred, falling back to xinput9_1_0 (present
/// since Vista) when 1_4 isn't found, so this works on any supported Windows version without the
/// caller needing to know which is present.
/// </summary>
public sealed class XInputControllerReader : IControllerReader
{
    // Trigger pull and thumbstick deflection both need a dead zone before counting as "pressed" -
    // otherwise resting noise on an analog stick or trigger would translate into a key being spammed
    // down and up. Values are Microsoft's own published XInput dead zone constants.
    private const byte TriggerThreshold = 30; // of 255
    private const short ThumbstickThreshold = 7849; // of 32767, matches XINPUT_GAMEPAD_LEFT_THUMB_DEADZONE

    private const ushort DPadUp = 0x0001;
    private const ushort DPadDown = 0x0002;
    private const ushort DPadLeft = 0x0004;
    private const ushort DPadRight = 0x0008;
    private const ushort Start = 0x0010;
    private const ushort Back = 0x0020;
    private const ushort LeftThumb = 0x0040;
    private const ushort RightThumb = 0x0080;
    private const ushort LeftShoulder = 0x0100;
    private const ushort RightShoulder = 0x0200;
    private const ushort AButton = 0x1000;
    private const ushort BButton = 0x2000;
    private const ushort XButton = 0x4000;
    private const ushort YButton = 0x8000;

    private bool? _preferModernDll;

    public bool TryGetState(int controllerIndex, out ControllerState state)
    {
        int result = GetState(controllerIndex, out XInputState raw);
        if (result != 0) // ERROR_SUCCESS
        {
            state = ControllerState.Empty;
            return false;
        }

        state = Decode(
            raw.Gamepad.wButtons,
            raw.Gamepad.bLeftTrigger,
            raw.Gamepad.bRightTrigger,
            raw.Gamepad.sThumbLX,
            raw.Gamepad.sThumbLY);
        return true;
    }

    /// <summary>
    /// Turns one raw XInput gamepad reading into the buttons Nudge understands.
    ///
    /// Split out from the P/Invoke above so it can actually be tested: everything with a decision in
    /// it lives here (which bit means which button, where a trigger counts as pulled, where a stick
    /// counts as pushed, which way is up), while <see cref="TryGetState"/> is left as a thin wrapper
    /// around the OS call that no test can meaningfully exercise without a controller plugged in.
    /// </summary>
    internal static ControllerState Decode(
        ushort buttons,
        byte leftTrigger,
        byte rightTrigger,
        short thumbLX,
        short thumbLY)
    {
        var pressed = new HashSet<ControllerButton>();
        AddIfSet(pressed, buttons, DPadUp, ControllerButton.DPadUp);
        AddIfSet(pressed, buttons, DPadDown, ControllerButton.DPadDown);
        AddIfSet(pressed, buttons, DPadLeft, ControllerButton.DPadLeft);
        AddIfSet(pressed, buttons, DPadRight, ControllerButton.DPadRight);
        AddIfSet(pressed, buttons, Start, ControllerButton.Start);
        AddIfSet(pressed, buttons, Back, ControllerButton.Back);
        AddIfSet(pressed, buttons, LeftThumb, ControllerButton.LeftThumb);
        AddIfSet(pressed, buttons, RightThumb, ControllerButton.RightThumb);
        AddIfSet(pressed, buttons, LeftShoulder, ControllerButton.LeftShoulder);
        AddIfSet(pressed, buttons, RightShoulder, ControllerButton.RightShoulder);
        AddIfSet(pressed, buttons, AButton, ControllerButton.A);
        AddIfSet(pressed, buttons, BButton, ControllerButton.B);
        AddIfSet(pressed, buttons, XButton, ControllerButton.X);
        AddIfSet(pressed, buttons, YButton, ControllerButton.Y);

        if (leftTrigger >= TriggerThreshold)
        {
            pressed.Add(ControllerButton.LeftTrigger);
        }

        if (rightTrigger >= TriggerThreshold)
        {
            pressed.Add(ControllerButton.RightTrigger);
        }

        // The left stick, collapsed to four discrete directions. Both axes are tested independently
        // rather than picking a single dominant direction, so a diagonal push reports both of the
        // directions it lies between - a caller that only binds up/down simply ignores the other.
        //
        // XInput's Y axis is positive upward, which is the opposite of the screen coordinates most
        // callers think in; converting here keeps that inversion in the one place that knows about
        // the hardware. The right stick is deliberately left unread: nothing in a pinball frontend
        // has an obvious use for it, and reporting directions nobody consumes would just be noise.
        if (thumbLY >= ThumbstickThreshold)
        {
            pressed.Add(ControllerButton.LeftStickUp);
        }
        else if (thumbLY <= -ThumbstickThreshold)
        {
            pressed.Add(ControllerButton.LeftStickDown);
        }

        if (thumbLX >= ThumbstickThreshold)
        {
            pressed.Add(ControllerButton.LeftStickRight);
        }
        else if (thumbLX <= -ThumbstickThreshold)
        {
            pressed.Add(ControllerButton.LeftStickLeft);
        }

        return new ControllerState { PressedButtons = pressed };
    }

    private static void AddIfSet(HashSet<ControllerButton> pressed, ushort buttons, ushort flag, ControllerButton button)
    {
        if ((buttons & flag) != 0)
        {
            pressed.Add(button);
        }
    }

    private int GetState(int controllerIndex, out XInputState state)
    {
        if (_preferModernDll != false)
        {
            try
            {
                int result = XInputGetStateModern((uint)controllerIndex, out state);
                _preferModernDll = true;
                return result;
            }
            catch (DllNotFoundException)
            {
                _preferModernDll = false;
            }
            catch (EntryPointNotFoundException)
            {
                _preferModernDll = false;
            }
        }

        return XInputGetStateLegacy((uint)controllerIndex, out state);
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern int XInputGetStateModern(uint dwUserIndex, out XInputState pState);

    [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
    private static extern int XInputGetStateLegacy(uint dwUserIndex, out XInputState pState);

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint dwPacketNumber;
        public XInputGamepad Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }
}
