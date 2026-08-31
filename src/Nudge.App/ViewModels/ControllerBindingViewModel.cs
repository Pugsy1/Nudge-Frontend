using CommunityToolkit.Mvvm.ComponentModel;
using Nudge.Core.Models;

namespace Nudge.App.ViewModels;

/// <summary>
/// One row of the controller rebinding list: an Xbox button, the keyboard key it sends to Visual
/// Pinball, and whether it is being held right now.
/// </summary>
public sealed partial class ControllerBindingViewModel : ObservableObject
{
    public ControllerBindingViewModel(ControllerButton button, string pinballRole)
    {
        Button = button;
        PinballRole = pinballRole;
    }

    public ControllerButton Button { get; }

    /// <summary>What this button does in a pinball table ("Left flipper"), not what it is on the pad - the reason someone is looking at this row at all.</summary>
    public string PinballRole { get; }

    /// <summary>The button's name as it reads on an Xbox pad ("Left bumper" rather than "LeftShoulder").</summary>
    public string ButtonLabel => Button switch
    {
        ControllerButton.DPadUp => "D-pad up",
        ControllerButton.DPadDown => "D-pad down",
        ControllerButton.DPadLeft => "D-pad left",
        ControllerButton.DPadRight => "D-pad right",
        ControllerButton.LeftShoulder => "Left bumper",
        ControllerButton.RightShoulder => "Right bumper",
        ControllerButton.LeftTrigger => "Left trigger",
        ControllerButton.RightTrigger => "Right trigger",
        ControllerButton.LeftThumb => "Left stick click",
        ControllerButton.RightThumb => "Right stick click",
        ControllerButton.Back => "Back / View",
        _ => Button.ToString()
    };

    /// <summary>The key currently sent for this button, or null when the button is deliberately unmapped.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeyLabel))]
    private VirtualKey? _key;

    /// <summary>Readable key name for the row, or a clear "not mapped" rather than an empty cell.</summary>
    public string KeyLabel => Key is null ? "Not assigned" : FormatKey(Key.Value);

    /// <summary>True while this row is waiting for the user to press the key they want assigned.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeyLabel))]
    private bool _isListening;

    /// <summary>
    /// True while the physical button is held. Drives a live indicator so someone can press a button
    /// on the pad and immediately see which row it is - far easier than guessing whether the thing
    /// under their finger is "LeftShoulder" or "LeftTrigger".
    /// </summary>
    [ObservableProperty]
    private bool _isPressed;

    private static string FormatKey(VirtualKey key) => key switch
    {
        VirtualKey.LeftShift => "Left Shift",
        VirtualKey.RightShift => "Right Shift",
        VirtualKey.LeftControl => "Left Ctrl",
        VirtualKey.RightControl => "Right Ctrl",
        VirtualKey.Digit1 => "1",
        VirtualKey.Digit2 => "2",
        VirtualKey.Digit3 => "3",
        VirtualKey.Digit4 => "4",
        VirtualKey.Digit5 => "5",
        VirtualKey.Slash => "/",
        _ => key.ToString()
    };
}
