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

        var pressed = new HashSet<ControllerButton>();
        AddIfSet(pressed, raw.Gamepad.wButtons, DPadUp, ControllerButton.DPadUp);
        AddIfSet(pressed, raw.Gamepad.wButtons, DPadDown, ControllerButton.DPadDown);
        AddIfSet(pressed, raw.Gamepad.wButtons, DPadLeft, ControllerButton.DPadLeft);
        AddIfSet(pressed, raw.Gamepad.wButtons, DPadRight, ControllerButton.DPadRight);
        AddIfSet(pressed, raw.Gamepad.wButtons, Start, ControllerButton.Start);
        AddIfSet(pressed, raw.Gamepad.wButtons, Back, ControllerButton.Back);
        AddIfSet(pressed, raw.Gamepad.wButtons, LeftThumb, ControllerButton.LeftThumb);
        AddIfSet(pressed, raw.Gamepad.wButtons, RightThumb, ControllerButton.RightThumb);
        AddIfSet(pressed, raw.Gamepad.wButtons, LeftShoulder, ControllerButton.LeftShoulder);
        AddIfSet(pressed, raw.Gamepad.wButtons, RightShoulder, ControllerButton.RightShoulder);
        AddIfSet(pressed, raw.Gamepad.wButtons, AButton, ControllerButton.A);
        AddIfSet(pressed, raw.Gamepad.wButtons, BButton, ControllerButton.B);
        AddIfSet(pressed, raw.Gamepad.wButtons, XButton, ControllerButton.X);
        AddIfSet(pressed, raw.Gamepad.wButtons, YButton, ControllerButton.Y);

        if (raw.Gamepad.bLeftTrigger >= TriggerThreshold)
        {
            pressed.Add(ControllerButton.LeftTrigger);
        }

        if (raw.Gamepad.bRightTrigger >= TriggerThreshold)
        {
            pressed.Add(ControllerButton.RightTrigger);
        }

        state = new ControllerState { PressedButtons = pressed };
        return true;
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
