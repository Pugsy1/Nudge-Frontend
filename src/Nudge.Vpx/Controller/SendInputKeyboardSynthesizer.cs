using System.Runtime.InteropServices;
using Nudge.Core.Models;

namespace Nudge.Vpx.Controller;

/// <summary>
/// Synthesizes key presses via Win32 <c>SendInput</c>, using hardware scan codes
/// (<c>KEYEVENTF_SCANCODE</c>) rather than virtual-key codes - the same technique tools like
/// AutoHotkey/JoyToKey use, and the one most reliable with games (including Visual Pinball) that
/// read the keyboard through DirectInput, which sees scan codes rather than the higher-level
/// virtual-key events a plain <c>keybd_event</c> VK press produces.
/// </summary>
public sealed class SendInputKeyboardSynthesizer : IKeyboardInputSynthesizer
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventFScanCode = 0x0008;
    private const uint KeyEventFKeyUp = 0x0002;
    private const uint KeyEventFExtendedKey = 0x0001;
    private const uint MapvkVkToVsc = 0;

    private static readonly Dictionary<VirtualKey, ushort> VirtualKeyCodes = new()
    {
        [VirtualKey.LeftShift] = 0xA0,
        [VirtualKey.RightShift] = 0xA1,
        [VirtualKey.Enter] = 0x0D,
        [VirtualKey.Digit1] = 0x31,
        [VirtualKey.Digit5] = 0x35,
        [VirtualKey.Space] = 0x20,
        [VirtualKey.Z] = 0x5A,
        [VirtualKey.Slash] = 0xBF, // VK_OEM_2, the '/' key on a US keyboard layout
        [VirtualKey.LeftControl] = 0xA2,
        [VirtualKey.RightControl] = 0xA3,
        [VirtualKey.Escape] = 0x1B,

        // Everything below exists because rebinding lets a user assign whatever key they actually
        // press. A VirtualKey with no entry here would be captured by the UI and then silently do
        // nothing, so the two sets have to stay in step.
        [VirtualKey.Digit0] = 0x30,
        [VirtualKey.Digit2] = 0x32,
        [VirtualKey.Digit3] = 0x33,
        [VirtualKey.Digit4] = 0x34,
        [VirtualKey.Digit6] = 0x36,
        [VirtualKey.Digit7] = 0x37,
        [VirtualKey.Digit8] = 0x38,
        [VirtualKey.Digit9] = 0x39,

        [VirtualKey.A] = 0x41, [VirtualKey.B] = 0x42, [VirtualKey.C] = 0x43, [VirtualKey.D] = 0x44,
        [VirtualKey.E] = 0x45, [VirtualKey.F] = 0x46, [VirtualKey.G] = 0x47, [VirtualKey.H] = 0x48,
        [VirtualKey.I] = 0x49, [VirtualKey.J] = 0x4A, [VirtualKey.K] = 0x4B, [VirtualKey.L] = 0x4C,
        [VirtualKey.M] = 0x4D, [VirtualKey.N] = 0x4E, [VirtualKey.O] = 0x4F, [VirtualKey.P] = 0x50,
        [VirtualKey.Q] = 0x51, [VirtualKey.R] = 0x52, [VirtualKey.S] = 0x53, [VirtualKey.T] = 0x54,
        [VirtualKey.U] = 0x55, [VirtualKey.V] = 0x56, [VirtualKey.W] = 0x57, [VirtualKey.X] = 0x58,
        [VirtualKey.Y] = 0x59,

        [VirtualKey.ArrowUp] = 0x26,
        [VirtualKey.ArrowDown] = 0x28,
        [VirtualKey.ArrowLeft] = 0x25,
        [VirtualKey.ArrowRight] = 0x27,
        [VirtualKey.Tab] = 0x09,
        [VirtualKey.Backspace] = 0x08,
        [VirtualKey.Delete] = 0x2E,
        [VirtualKey.Insert] = 0x2D,
        [VirtualKey.Home] = 0x24,
        [VirtualKey.End] = 0x23,
        [VirtualKey.PageUp] = 0x21,
        [VirtualKey.PageDown] = 0x22,

        [VirtualKey.LeftAlt] = 0xA4,
        [VirtualKey.RightAlt] = 0xA5,

        [VirtualKey.Comma] = 0xBC,        // VK_OEM_COMMA
        [VirtualKey.Period] = 0xBE,       // VK_OEM_PERIOD
        [VirtualKey.Semicolon] = 0xBA,    // VK_OEM_1
        [VirtualKey.Quote] = 0xDE,        // VK_OEM_7
        [VirtualKey.LeftBracket] = 0xDB,  // VK_OEM_4
        [VirtualKey.RightBracket] = 0xDD, // VK_OEM_6
        [VirtualKey.Backslash] = 0xDC,    // VK_OEM_5
        [VirtualKey.Minus] = 0xBD,        // VK_OEM_MINUS
        [VirtualKey.Equals] = 0xBB,       // VK_OEM_PLUS
        [VirtualKey.Backtick] = 0xC0,     // VK_OEM_3

        [VirtualKey.F1] = 0x70, [VirtualKey.F2] = 0x71, [VirtualKey.F3] = 0x72,
        [VirtualKey.F4] = 0x73, [VirtualKey.F5] = 0x74, [VirtualKey.F6] = 0x75,
        [VirtualKey.F7] = 0x76, [VirtualKey.F8] = 0x77, [VirtualKey.F9] = 0x78,
        [VirtualKey.F10] = 0x79, [VirtualKey.F11] = 0x7A, [VirtualKey.F12] = 0x7B
    };

    // Right-side modifier keys and a few others are "extended" keys on a standard keyboard - without
    // this flag, Windows can resolve their scan code back to the left-side key instead. The arrow,
    // navigation and numpad-adjacent keys are extended for the same reason: their unextended scan
    // codes belong to the numeric keypad.
    private static readonly HashSet<VirtualKey> ExtendedKeys =
    [
        VirtualKey.RightShift, VirtualKey.RightControl, VirtualKey.Enter, VirtualKey.RightAlt,
        VirtualKey.ArrowUp, VirtualKey.ArrowDown, VirtualKey.ArrowLeft, VirtualKey.ArrowRight,
        VirtualKey.Insert, VirtualKey.Delete, VirtualKey.Home, VirtualKey.End,
        VirtualKey.PageUp, VirtualKey.PageDown
    ];

    public void KeyDown(VirtualKey key) => Send(key, keyUp: false);

    public void KeyUp(VirtualKey key) => Send(key, keyUp: true);

    private static void Send(VirtualKey key, bool keyUp)
    {
        ushort virtualKeyCode = VirtualKeyCodes[key];
        ushort scanCode = (ushort)MapVirtualKey(virtualKeyCode, MapvkVkToVsc);

        uint flags = KeyEventFScanCode;
        if (keyUp)
        {
            flags |= KeyEventFKeyUp;
        }

        if (ExtendedKeys.Contains(key))
        {
            flags |= KeyEventFExtendedKey;
        }

        var input = new Input
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKeyCode = 0,
                    ScanCode = scanCode,
                    Flags = flags,
                    Time = 0,
                    ExtraInfo = IntPtr.Zero
                }
            }
        };

        SendInput(1, [input], Marshal.SizeOf<Input>());
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint numberOfInputs, Input[] inputs, int inputSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    // Win32's real INPUT union also contains MOUSEINPUT and HARDWAREINPUT - MOUSEINPUT is the
    // largest at 32 bytes on x64 (this process only ever targets x64 Windows). Explicitly sizing
    // this union to 32 bytes, even though only the Keyboard branch is ever used, matters: Marshal
    // .SizeOf<Input>() feeds SendInput's own cbSize parameter, and SendInput silently rejects the
    // entire call - sending nothing, with no visible error - if that doesn't match its own real
    // sizeof(INPUT) (40 bytes on x64). Leaving this at its natural (smaller) size is the single
    // most common SendInput-on-x64 bug.
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKeyCode;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }
}
