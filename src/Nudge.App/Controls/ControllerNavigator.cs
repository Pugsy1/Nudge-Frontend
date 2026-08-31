using System.Windows;
using System.Windows.Threading;
using System.Linq;
using Nudge.Core.Abstractions;
using Nudge.Core.Models;

namespace Nudge.App.Controls;

/// <summary>Which navigation action a controller button just asked for.</summary>
public enum ControllerAction
{
    Up,
    Down,
    Left,
    Right,

    /// <summary>A - launch the table under the cursor.</summary>
    Activate,

    /// <summary>X - open the table's details page.</summary>
    Details,

    /// <summary>Y - open the table's customization page (the "three lines").</summary>
    Customize,

    /// <summary>Left bumper - toggle the table as a favourite.</summary>
    Favorite,

    /// <summary>B - back out of wherever you are.</summary>
    Back,

    /// <summary>Start - open Settings, which is otherwise unreachable without a mouse.</summary>
    Menu
}

/// <summary>
/// Turns controller input into navigation actions for Nudge's own UI, so the library can be browsed
/// and a table started without touching a keyboard or mouse.
///
/// Entirely separate from <see cref="IControllerInputService"/>, which synthesizes key presses into
/// a running Visual Pinball. That translates a pad into fake keystrokes for another process; this
/// reads the pad directly and moves the library's own selection. The two never overlap because they
/// are active at different times - translation runs only while a table is running, and this only
/// while Nudge itself is the foreground window.
/// </summary>
public sealed class ControllerNavigator : IDisposable
{
    /// <summary>
    /// Fast enough that holding a direction feels responsive rather than laggy, slow enough that a
    /// single deliberate press cannot be read as several.
    /// </summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(60);

    /// <summary>How long a direction must be held before it starts repeating, and how fast it then repeats.</summary>
    private static readonly TimeSpan RepeatDelay = TimeSpan.FromMilliseconds(420);
    private static readonly TimeSpan RepeatInterval = TimeSpan.FromMilliseconds(110);

    private static readonly (ControllerButton Button, ControllerAction Action)[] Bindings =
    [
        (ControllerButton.DPadUp, ControllerAction.Up),
        (ControllerButton.DPadDown, ControllerAction.Down),
        (ControllerButton.DPadLeft, ControllerAction.Left),
        (ControllerButton.DPadRight, ControllerAction.Right),

        // The stick reads as four directions past XInput's dead zone, so it drives navigation
        // identically to the D-pad and picks up the auto-repeat below for free - the repeat state is
        // keyed by button, and both buttons map to the same action.
        //
        // Holding the D-pad and the stick the same way at once therefore yields two Up actions per
        // repeat tick rather than one. Left unguarded deliberately: nobody holds both, and the
        // bookkeeping to suppress it would cost more than the behaviour it prevents.
        (ControllerButton.LeftStickUp, ControllerAction.Up),
        (ControllerButton.LeftStickDown, ControllerAction.Down),
        (ControllerButton.LeftStickLeft, ControllerAction.Left),
        (ControllerButton.LeftStickRight, ControllerAction.Right),

        (ControllerButton.A, ControllerAction.Activate),
        (ControllerButton.X, ControllerAction.Details),
        (ControllerButton.Y, ControllerAction.Customize),
        (ControllerButton.LeftShoulder, ControllerAction.Favorite),
        (ControllerButton.B, ControllerAction.Back),
        (ControllerButton.Start, ControllerAction.Menu)
    ];

    private readonly IControllerReader _reader;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<ControllerButton, DateTime> _heldSince = [];
    private readonly Dictionary<ControllerButton, DateTime> _lastRepeat = [];

    private ControllerState _previous = ControllerState.Empty;
    private bool _wasReceivingInput;

    public ControllerNavigator(IControllerReader reader)
    {
        _reader = reader;
        _timer = new DispatcherTimer { Interval = PollInterval };
        _timer.Tick += OnTick;
    }

    /// <summary>Raised on the UI thread when a button asks for a navigation action.</summary>
    public event Action<ControllerAction>? Action;

    public void Start() => _timer.Start();

    public void Stop()
    {
        _timer.Stop();
        _previous = ControllerState.Empty;
        _wasReceivingInput = false;
        _heldSince.Clear();
        _lastRepeat.Clear();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        // Only while Nudge is actually the active window. Without this the pad would keep driving
        // the library underneath whatever the user has switched to - including Visual Pinball
        // itself, where the very same buttons are being translated into flipper presses.
        //
        // Checks every window rather than only Application.MainWindow: the main window is assigned
        // during startup, and if that assignment is ever missed or replaced this would silently
        // block all controller input forever, with no error and nothing in the log - a failure mode
        // indistinguishable from the pad not being read at all.
        bool nudgeIsActive = Application.Current?.Windows.OfType<Window>().Any(w => w.IsActive) ?? false;
        if (!nudgeIsActive)
        {
            _previous = ControllerState.Empty;
            _wasReceivingInput = false;
            return;
        }

        if (!_reader.TryGetState(0, out ControllerState state))
        {
            _previous = ControllerState.Empty;
            _wasReceivingInput = false;
            return;
        }

        // Input has just started reaching this navigator - Nudge has regained focus, or a pad was
        // plugged in. Adopt whatever is held right now as the baseline instead of treating it as a
        // fresh press.
        //
        // This is the other end of the handoff that ControllerInputSession guards on the way in, and
        // it breaks the same way without this: the button used to quit a table is still held for the
        // moment it takes Visual Pinball to close and the library to come back, so the very first
        // poll would read it as a brand-new press and fire its action - quitting a table would dump
        // the player straight onto the customization page of whatever tile the selection sat on.
        if (!_wasReceivingInput)
        {
            _wasReceivingInput = true;
            _previous = state;
            return;
        }

        DateTime now = DateTime.UtcNow;

        foreach ((ControllerButton button, ControllerAction action) in Bindings)
        {
            bool isDown = state.IsPressed(button);
            bool wasDown = _previous.IsPressed(button);

            if (isDown && !wasDown)
            {
                _heldSince[button] = now;
                _lastRepeat[button] = now;
                Action?.Invoke(action);
                continue;
            }

            if (!isDown)
            {
                _heldSince.Remove(button);
                _lastRepeat.Remove(button);
                continue;
            }

            // Only the directions auto-repeat while held. Repeating Activate would launch a table
            // over and over from one long press, and repeating Back would fly through every screen.
            if (action is not (ControllerAction.Up or ControllerAction.Down or ControllerAction.Left or ControllerAction.Right))
            {
                continue;
            }

            if (_heldSince.TryGetValue(button, out DateTime since)
                && now - since >= RepeatDelay
                && _lastRepeat.TryGetValue(button, out DateTime last)
                && now - last >= RepeatInterval)
            {
                _lastRepeat[button] = now;
                Action?.Invoke(action);
            }
        }

        _previous = state;
    }

    public void Dispose()
    {
        _timer.Tick -= OnTick;
        Stop();
    }
}
