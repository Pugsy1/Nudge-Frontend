using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
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
        ControllerInputSession session = CreateSession();
        _controllerReader.State = State(ControllerButton.LeftShoulder);

        session.Tick();

        _keyboard.DownCalls.Should().Equal(VirtualKey.LeftShift);
        _keyboard.UpCalls.Should().BeEmpty();
    }

    [Fact]
    public void Holding_the_same_button_across_polls_sends_no_repeated_key_down()
    {
        ControllerInputSession session = CreateSession();
        _controllerReader.State = State(ControllerButton.LeftShoulder);

        session.Tick();
        session.Tick();
        session.Tick();

        _keyboard.DownCalls.Should().Equal(VirtualKey.LeftShift);
    }

    [Fact]
    public void Releasing_the_button_sends_exactly_one_key_up()
    {
        ControllerInputSession session = CreateSession();
        _controllerReader.State = State(ControllerButton.LeftShoulder);
        session.Tick();

        _controllerReader.State = State();
        session.Tick();

        _keyboard.UpCalls.Should().Equal(VirtualKey.LeftShift);
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
        ControllerInputSession session = CreateSession();
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
        ControllerInputSession session = CreateSession();
        _controllerReader.State = State(ControllerButton.LeftShoulder);
        session.Tick();

        _controllerReader.Connected = false;
        session.Tick();

        _keyboard.UpCalls.Should().Equal(VirtualKey.LeftShift);
    }

    [Fact]
    public void Disposing_releases_every_key_still_held()
    {
        ControllerInputSession session = CreateSession();
        _controllerReader.State = State(ControllerButton.LeftShoulder);
        session.Tick();

        session.Dispose();

        _keyboard.UpCalls.Should().Equal(VirtualKey.LeftShift);
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

        public bool TryGetState(int controllerIndex, out ControllerState state)
        {
            WasQueried = true;
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
