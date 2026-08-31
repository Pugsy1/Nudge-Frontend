namespace Nudge.App.ViewModels;

/// <summary>
/// One entry in the on-screen controller legend: a button, and what it does here.
///
/// Rendered as a badge holding the button name followed by the action, rather than "A = Play" -
/// the badge already says "this is a button", so the equals sign is reading as an instruction the
/// glyph has made unnecessary.
/// </summary>
/// <param name="Button">The button as it is labelled on the pad ("A", "LB", "Start").</param>
/// <param name="Action">What it does on the current screen, in the fewest words that are still clear.</param>
public sealed record ControllerHint(string Button, string Action);
