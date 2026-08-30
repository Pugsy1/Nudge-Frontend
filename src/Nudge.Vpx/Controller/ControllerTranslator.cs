using Nudge.Core.Models;

namespace Nudge.Vpx.Controller;

/// <summary>Which keys to press and release to move from one controller snapshot to the next.</summary>
public sealed record ControllerTranslationResult
{
    public static readonly ControllerTranslationResult None = new()
    {
        KeysToPress = [],
        KeysToRelease = []
    };

    public required IReadOnlyList<VirtualKey> KeysToPress { get; init; }

    public required IReadOnlyList<VirtualKey> KeysToRelease { get; init; }
}

/// <summary>
/// Pure diffing logic, no I/O: given the controller state from the last poll and this poll, decides
/// which keys just started being held (press) and which just stopped (release) - so a button held
/// across many polls produces exactly one key-down and, later, exactly one key-up, not a key-down
/// spammed every poll.
/// </summary>
public static class ControllerTranslator
{
    public static ControllerTranslationResult Translate(
        ControllerState previous,
        ControllerState current,
        ControllerMapping mapping)
    {
        List<VirtualKey>? toPress = null;
        List<VirtualKey>? toRelease = null;

        foreach (ControllerButton button in Enum.GetValues<ControllerButton>())
        {
            VirtualKey? key = mapping.TryGetKey(button);
            if (key is null)
            {
                continue;
            }

            bool wasPressed = previous.IsPressed(button);
            bool isPressed = current.IsPressed(button);

            if (isPressed && !wasPressed)
            {
                (toPress ??= []).Add(key.Value);
            }
            else if (wasPressed && !isPressed)
            {
                (toRelease ??= []).Add(key.Value);
            }
        }

        return toPress is null && toRelease is null
            ? ControllerTranslationResult.None
            : new ControllerTranslationResult
            {
                KeysToPress = toPress ?? [],
                KeysToRelease = toRelease ?? []
            };
    }
}
