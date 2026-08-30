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
        [VirtualKey.Escape] = 0x1B
    };

    // Right-side modifier keys and a few others are "extended" keys on a standard keyboard - without
    // this flag, Windows can resolve their scan code back to the left-side key instead.
    private static readonly HashSet<VirtualKey> ExtendedKeys =
    [
        VirtualKey.RightShift, VirtualKey.RightControl, VirtualKey.Enter
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
