namespace Nudge.Core.Models;

/// <summary>
/// A single snapshot of which <see cref="ControllerButton"/>s are currently held down, taken from
/// one poll of one controller. Analog inputs (triggers, thumbsticks) are already collapsed to a
/// pressed/not-pressed boolean by whatever produced this snapshot - a controller reader decides its
/// own "how far is a trigger pulled before it counts" threshold, not this type.
/// </summary>
public sealed record ControllerState
{
    public static readonly ControllerState Empty = new() { PressedButtons = new HashSet<ControllerButton>() };

    public required IReadOnlySet<ControllerButton> PressedButtons { get; init; }

    public bool IsPressed(ControllerButton button) => PressedButtons.Contains(button);
}
