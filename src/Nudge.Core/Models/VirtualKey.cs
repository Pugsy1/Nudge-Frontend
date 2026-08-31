namespace Nudge.Core.Models;

/// <summary>
/// A keyboard key Nudge can synthesize on the controller-to-keyboard path, named for what it means
/// rather than for a Win32 virtual-key constant - the P/Invoke layer that actually presses keys
/// (Nudge.Vpx, not referenced here) is the only place that needs to know the real VK codes.
///
/// Originally just the eleven keys Visual Pinball's own default bindings use. Widened once
/// rebinding became a user-facing feature: the rebind flow captures whatever key someone actually
/// presses, so anything not represented here would be captured and then silently refused. This is
/// the practical set a pinball player might bind - letters, digits, arrows, and the modifiers and
/// punctuation VPX itself uses - rather than every key on the keyboard, since a key with no
/// entry here simply cannot be stored or replayed.
/// </summary>
public enum VirtualKey
{
    // The original VPX default-binding set.
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
    Escape,

    // Remaining digits.
    Digit0,
    Digit2,
    Digit3,
    Digit4,
    Digit6,
    Digit7,
    Digit8,
    Digit9,

    // Letters. Z is already declared above.
    A, B, C, D, E, F, G, H, I, J, K, L, M,
    N, O, P, Q, R, S, T, U, V, W, X, Y,

    // Navigation and editing.
    ArrowUp,
    ArrowDown,
    ArrowLeft,
    ArrowRight,
    Tab,
    Backspace,
    Delete,
    Insert,
    Home,
    End,
    PageUp,
    PageDown,

    // Remaining modifiers.
    LeftAlt,
    RightAlt,

    // Punctuation VPX bindings commonly reach for.
    Comma,
    Period,
    Semicolon,
    Quote,
    LeftBracket,
    RightBracket,
    Backslash,
    Minus,
    Equals,
    Backtick,

    // Function keys.
    F1, F2, F3, F4, F5, F6,
    F7, F8, F9, F10, F11, F12
}
