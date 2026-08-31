using System.Windows.Input;
using Nudge.Core.Models;

namespace Nudge.App.Services;

/// <summary>
/// Translates a WPF <see cref="Key"/> from a live key press into the <see cref="VirtualKey"/> Nudge
/// stores and replays, for the controller rebinding flow.
///
/// Returns null for anything Nudge cannot synthesize. That is deliberate and is surfaced to the
/// user rather than swallowed: silently accepting a key that the synthesizer has no code for would
/// produce a binding that looks saved and then does nothing in-game.
/// </summary>
public static class KeyCapture
{
    public static VirtualKey? FromWpfKey(Key key) => key switch
    {
        // WPF reports the left/right variants separately only via Key.LeftShift etc., which is
        // exactly the distinction the flipper bindings depend on.
        Key.LeftShift => VirtualKey.LeftShift,
        Key.RightShift => VirtualKey.RightShift,
        Key.LeftCtrl => VirtualKey.LeftControl,
        Key.RightCtrl => VirtualKey.RightControl,
        Key.LeftAlt => VirtualKey.LeftAlt,
        Key.RightAlt => VirtualKey.RightAlt,

        Key.Enter => VirtualKey.Enter,
        Key.Space => VirtualKey.Space,
        Key.Escape => VirtualKey.Escape,
        Key.Tab => VirtualKey.Tab,
        Key.Back => VirtualKey.Backspace,
        Key.Delete => VirtualKey.Delete,
        Key.Insert => VirtualKey.Insert,
        Key.Home => VirtualKey.Home,
        Key.End => VirtualKey.End,
        Key.PageUp => VirtualKey.PageUp,
        Key.PageDown => VirtualKey.PageDown,

        Key.Up => VirtualKey.ArrowUp,
        Key.Down => VirtualKey.ArrowDown,
        Key.Left => VirtualKey.ArrowLeft,
        Key.Right => VirtualKey.ArrowRight,

        // Both the number row and the numpad, since either is a reasonable thing to press when
        // asked for "1" (start) or "5" (coin).
        Key.D0 or Key.NumPad0 => VirtualKey.Digit0,
        Key.D1 or Key.NumPad1 => VirtualKey.Digit1,
        Key.D2 or Key.NumPad2 => VirtualKey.Digit2,
        Key.D3 or Key.NumPad3 => VirtualKey.Digit3,
        Key.D4 or Key.NumPad4 => VirtualKey.Digit4,
        Key.D5 or Key.NumPad5 => VirtualKey.Digit5,
        Key.D6 or Key.NumPad6 => VirtualKey.Digit6,
        Key.D7 or Key.NumPad7 => VirtualKey.Digit7,
        Key.D8 or Key.NumPad8 => VirtualKey.Digit8,
        Key.D9 or Key.NumPad9 => VirtualKey.Digit9,

        Key.OemComma => VirtualKey.Comma,
        Key.OemPeriod => VirtualKey.Period,
        Key.OemQuestion => VirtualKey.Slash,
        Key.OemSemicolon => VirtualKey.Semicolon,
        Key.OemQuotes => VirtualKey.Quote,
        Key.OemOpenBrackets => VirtualKey.LeftBracket,
        Key.OemCloseBrackets => VirtualKey.RightBracket,
        Key.OemBackslash or Key.OemPipe => VirtualKey.Backslash,
        Key.OemMinus => VirtualKey.Minus,
        Key.OemPlus => VirtualKey.Equals,
        Key.OemTilde => VirtualKey.Backtick,

        >= Key.A and <= Key.Z => Enum.Parse<VirtualKey>(key.ToString()),
        >= Key.F1 and <= Key.F12 => Enum.Parse<VirtualKey>(key.ToString()),

        _ => null
    };
}
