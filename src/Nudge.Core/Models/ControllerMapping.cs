namespace Nudge.Core.Models;

/// <summary>
/// Which <see cref="VirtualKey"/> each <see cref="ControllerButton"/> should be translated into
/// during gameplay. <see cref="Default"/> mirrors Visual Pinball's own out-of-the-box keybindings
/// (verified against the community's documented default keymap - see docs/RESEARCH-NOTES.md) so a
/// controller works with an unmodified VPX install with no extra setup; a user who has remapped VPX
/// itself can override individual buttons via <see cref="NudgeSettings.ControllerButtonOverrides"/>
/// without Nudge needing to know anything changed.
/// </summary>
public sealed class ControllerMapping
{
    private readonly Dictionary<ControllerButton, VirtualKey> _keysByButton;

    public ControllerMapping(IReadOnlyDictionary<ControllerButton, VirtualKey> keysByButton) =>
        _keysByButton = new Dictionary<ControllerButton, VirtualKey>(keysByButton);

    public static ControllerMapping Default { get; } = new(new Dictionary<ControllerButton, VirtualKey>
    {
        [ControllerButton.LeftShoulder] = VirtualKey.LeftShift, // left flipper
        [ControllerButton.RightShoulder] = VirtualKey.RightShift, // right flipper
        [ControllerButton.A] = VirtualKey.Enter, // plunger
        [ControllerButton.Start] = VirtualKey.Digit1, // start game
        [ControllerButton.Back] = VirtualKey.Digit5, // insert coin
        [ControllerButton.LeftTrigger] = VirtualKey.LeftControl, // left magnasave
        [ControllerButton.RightTrigger] = VirtualKey.RightControl, // right magnasave
        [ControllerButton.DPadUp] = VirtualKey.Space, // nudge forward
        [ControllerButton.DPadLeft] = VirtualKey.Z, // nudge left
        [ControllerButton.DPadRight] = VirtualKey.Slash, // nudge right
        [ControllerButton.Y] = VirtualKey.Escape // menu
    });

    /// <summary>Null when this button is not mapped to anything and should be ignored entirely.</summary>
    public VirtualKey? TryGetKey(ControllerButton button) =>
        _keysByButton.TryGetValue(button, out VirtualKey key) ? key : null;

    /// <summary>
    /// Starts from <see cref="Default"/> and replaces individual entries named in
    /// <paramref name="overrides"/> (button name -> key name, as stored in
    /// <see cref="NudgeSettings.ControllerButtonOverrides"/>). An override naming an unrecognised
    /// button or key is skipped rather than failing the whole mapping - a settings file from a newer
    /// or older Nudge build should still produce a usable mapping.
    /// </summary>
    public static ControllerMapping FromOverrides(IReadOnlyDictionary<string, string> overrides)
    {
        Dictionary<ControllerButton, VirtualKey> keysByButton = new(Default._keysByButton);

        foreach ((string buttonName, string keyName) in overrides)
        {
            if (Enum.TryParse(buttonName, ignoreCase: true, out ControllerButton button) &&
                Enum.TryParse(keyName, ignoreCase: true, out VirtualKey key))
            {
                keysByButton[button] = key;
            }
        }

        return new ControllerMapping(keysByButton);
    }
}
