using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Nudge.Core.Abstractions;
using Nudge.Core.Models;
using Nudge.Vpx.Controller;
using Xunit;

namespace Nudge.Vpx.Tests;

/// <summary>
/// Drives <see cref="ControllerInputSession.Tick"/> directly rather than starting its real 60Hz
/// background loop, so these tests are deterministic and instant instead of racing a timer. The
/// background loop itself is a thin, untestable wrapper around calling <c>Tick</c> repeatedly - see
/// <see cref="ControllerInputSession"/>'s own remarks.
/// </summary>
public sealed class ControllerInputSessionTests
{
    private const string TargetProcessName = "VPinballX64";

    private readonly FakeControllerReader _controllerReader = new();
    private readonly FakeKeyboardInputSynthesizer _keyboard = new();
    private readonly FakeForegroundWindowService _foregroundWindow = new() { ProcessName = TargetProcessName };

    private static readonly ControllerMapping Mapping = new(new Dictionary<ControllerButton, VirtualKey>
    {
        [ControllerButton.LeftShoulder] = VirtualKey.LeftShift
    });

    [Fact]
    public void A_newly_pressed_button_sends_exactly_one_key_down()
    {
        ControllerInputSession session = CreateFocusedSession();
        _controllerReader.State = State(ControllerButton.LeftShoulder);

        session.Tick();

        _keyboard.DownCalls.Should().Equal(VirtualKey.LeftShift);
        _keyboard.UpCalls.Should().BeEmpty();
    }

    [Fact]
    public void Holding_the_same_button_across_polls_sends_no_repeated_key_down()
    {
        ControllerInputSession session = CreateFocusedSession();
        _controllerReader.State = State(ControllerButton.LeftShoulder);

        session.Tick();
        session.Tick();
        session.Tick();

        _keyboard.DownCalls.Should().Equal(VirtualKey.LeftShift);
    }

    [Fact]
    public void Releasing_the_button_sends_exactly_one_key_up()
    {
        ControllerInputSession session = CreateFocusedSession();
        _controllerReader.State = State(ControllerButton.LeftShoulder);
        session.Tick();

        _controllerReader.State = State();
        session.Tick();

        _keyboard.UpCalls.Should().Equal(VirtualKey.LeftShift);
    }

    [Fact]
    public void A_button_still_held_when_focus_arrives_does_not_fire_a_phantom_press()
    {
        // The launch handoff: the user pressed A in Nudge's library to start this table and is still
        // holding it as Visual Pinball takes focus. That held button must be adopted as the starting
        // baseline, not replayed into the table as a fresh press - otherwise every controller launch
        // begins by firing the plunger on its own.
        _foregroundWindow.ProcessName = "Nudge"; // Nudge still foreground, table starting up
        ControllerInputSession session = CreateSession();
        _controllerReader.State = State(ControllerButton.LeftShoulder);
        session.Tick();

        _foregroundWindow.ProcessName = TargetProcessName; // the table takes focus, button still held
        session.Tick();

        _keyboard.DownCalls.Should().BeEmpty("a button held across the handoff was never pressed inside the table");
    }

    [Fact]
    public void Releasing_a_button_that_was_only_ever_the_focus_baseline_sends_no_key_up()
    {
        // Follows the scenario above through to its end: since no key-down was ever sent for the
        // held button, letting go of it must not send the table an unmatched key-up either.
        _foregroundWindow.ProcessName = "Nudge";
        ControllerInputSession session = CreateSession();
        _controllerReader.State = State(ControllerButton.LeftShoulder);
        session.Tick();

        _foregroundWindow.ProcessName = TargetProcessName;
        session.Tick();

        _controllerReader.State = State(); // user lets go
        session.Tick();

        _keyboard.UpCalls.Should().BeEmpty();
    }

    [Fact]
    public void A_button_pressed_after_focus_arrives_still_works_normally()
    {
        // The baseline adoption must not swallow real input - only what was already held at the
        // moment of handoff.
        _foregroundWindow.ProcessName = "Nudge";
        ControllerInputSession session = CreateSession();
        _controllerReader.State = State(ControllerButton.LeftShoulder);
        session.Tick();

        _foregroundWindow.ProcessName = TargetProcessName;
        session.Tick();

        _controllerReader.State = State(); // release the launch button
        session.Tick();
        _controllerReader.State = State(ControllerButton.LeftShoulder); // a real, deliberate flip
        session.Tick();

        _keyboard.DownCalls.Should().Equal(VirtualKey.LeftShift);
    }

    [Fact]
    public void Input_is_ignored_entirely_while_a_different_window_has_focus()
    {
        _foregroundWindow.ProcessName = "SomeOtherApp";
        ControllerInputSession session = CreateSession();
        _controllerReader.State = State(ControllerButton.LeftShoulder);

        session.Tick();

        _keyboard.DownCalls.Should().BeEmpty();
        _controllerReader.WasQueried.Should().BeFalse("there is no reason to even read the controller without focus");
    }

    [Fact]
    public void Losing_focus_while_a_key_is_held_forces_it_to_release()
    {
        ControllerInputSession session = CreateFocusedSession();
        _controllerReader.State = State(ControllerButton.LeftShoulder);
        session.Tick();
        _keyboard.DownCalls.Should().HaveCount(1);

        _foregroundWindow.ProcessName = "SomeOtherApp";
        session.Tick();

        _keyboard.UpCalls.Should().Equal(VirtualKey.LeftShift);
    }

    [Fact]
    public void The_controller_disconnecting_while_a_key_is_held_forces_it_to_release()
    {
        ControllerInputSession session = CreateFocusedSession();
        _controllerReader.State = State(ControllerButton.LeftShoulder);
        session.Tick();

        _controllerReader.Connected = false;
        session.Tick();

        _keyboard.UpCalls.Should().Equal(VirtualKey.LeftShift);
    }

    [Fact]
    public void With_no_controller_connected_XInput_is_probed_occasionally_rather_than_every_poll()
    {
        // This loop runs for the whole play session, so on a machine with no pad the naive version
        // would query XInput 60 times a second for the entire time a table is running - see
        // ReadController's remarks for why that is not free.
        _controllerReader.Connected = false;
        ControllerInputSession session = CreateSession();

        for (int i = 0; i < 100; i++)
        {
            session.Tick();
        }

        _controllerReader.QueryCount.Should().BeLessThan(5, "a disconnected pad should be probed about once a second, not every poll");
    }

    [Fact]
    public void A_controller_plugged_in_mid_session_is_picked_up_once_the_backoff_elapses()
    {
        // The backoff must not mean "no controller at startup, no controller ever".
        _controllerReader.Connected = false;
        ControllerInputSession session = CreateSession();
        session.Tick();

        _controllerReader.Connected = true;
        _controllerReader.State = State(ControllerButton.LeftShoulder);

        for (int i = 0; i < 120; i++)
        {
            session.Tick();
        }

        _keyboard.DownCalls.Should().Equal(VirtualKey.LeftShift);
    }

    [Fact]
    public void Disposing_releases_every_key_still_held()
    {
        ControllerInputSession session = CreateFocusedSession();
        _controllerReader.State = State(ControllerButton.LeftShoulder);
        session.Tick();

        session.Dispose();

        _keyboard.UpCalls.Should().Equal(VirtualKey.LeftShift);
    }

    /// <summary>
    /// A session that has already completed its focus handoff with nothing held. This is where
    /// production sits a moment after Visual Pinball's window appears, and where every test except
    /// the handoff ones themselves wants to start - a session's first focused tick only establishes
    /// a baseline (see the handoff tests for why), so a test that presses a button on tick one would
    /// otherwise be asserting against the handoff rather than against ordinary play.
    /// </summary>
    private ControllerInputSession CreateFocusedSession()
    {
        ControllerInputSession session = CreateSession();
        _controllerReader.State = ControllerState.Empty;
        session.Tick();
        return session;
    }

    private ControllerInputSession CreateSession() => new(
        _controllerReader,
        _keyboard,
        _foregroundWindow,
        TargetProcessName,
        Mapping,
        NullLogger.Instance);

    private static ControllerState State(params ControllerButton[] pressed) =>
        new() { PressedButtons = pressed.ToHashSet() };

    private sealed class FakeControllerReader : IControllerReader
    {
        public ControllerState State { get; set; } = ControllerState.Empty;

        public bool Connected { get; set; } = true;

        public bool WasQueried { get; private set; }

        /// <summary>How many times XInput was actually asked - what the disconnected-backoff test measures.</summary>
        public int QueryCount { get; private set; }

        public bool TryGetState(int controllerIndex, out ControllerState state)
        {
            WasQueried = true;
            QueryCount++;
            state = State;
            return Connected;
        }
    }

    private sealed class FakeKeyboardInputSynthesizer : IKeyboardInputSynthesizer
    {
        public List<VirtualKey> DownCalls { get; } = [];

        public List<VirtualKey> UpCalls { get; } = [];

        public void KeyDown(VirtualKey key) => DownCalls.Add(key);

        public void KeyUp(VirtualKey key) => UpCalls.Add(key);
    }

    private sealed class FakeForegroundWindowService : IForegroundWindowService
    {
        public string? ProcessName { get; set; }

        public string? GetForegroundProcessName() => ProcessName;
    }
}
