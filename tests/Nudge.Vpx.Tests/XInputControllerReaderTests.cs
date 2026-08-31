using FluentAssertions;
using Nudge.Core.Models;
using Nudge.Vpx.Controller;
using Xunit;

namespace Nudge.Vpx.Tests;

/// <summary>
/// Covers <see cref="XInputControllerReader.Decode"/> - the half of the reader that actually decides
/// anything (which bit is which button, where a trigger counts as pulled, where a stick counts as
/// pushed, and which way is up). The P/Invoke around it cannot be exercised without a real
/// controller and is deliberately left as a thin wrapper so there is nothing else in it to test.
///
/// Bit values are XInput's own published XINPUT_GAMEPAD_* constants, written out here rather than
/// referenced from the production code so a typo in one is not silently mirrored by the other.
/// </summary>
public sealed class XInputControllerReaderTests
{
    private const ushort GamepadA = 0x1000;
    private const ushort GamepadB = 0x2000;
    private const ushort GamepadY = 0x8000;
    private const ushort GamepadDPadUp = 0x0001;
    private const ushort GamepadLeftShoulder = 0x0100;

    /// <summary>XINPUT_GAMEPAD_LEFT_THUMB_DEADZONE - the reader's own threshold, so just past it must register and just inside it must not.</summary>
    private const short DeadZone = 7849;

    [Fact]
    public void No_input_at_all_reports_nothing_pressed()
    {
        ControllerState state = Decode();

        state.PressedButtons.Should().BeEmpty();
    }

    [Theory]
    [InlineData(GamepadA, ControllerButton.A)]
    [InlineData(GamepadB, ControllerButton.B)]
    [InlineData(GamepadY, ControllerButton.Y)]
    [InlineData(GamepadDPadUp, ControllerButton.DPadUp)]
    [InlineData(GamepadLeftShoulder, ControllerButton.LeftShoulder)]
    public void A_button_bit_maps_to_its_button(ushort bit, ControllerButton expected)
    {
        ControllerState state = Decode(buttons: bit);

        state.IsPressed(expected).Should().BeTrue();
    }

    [Fact]
    public void Several_buttons_at_once_are_all_reported()
    {
        ControllerState state = Decode(buttons: GamepadA | GamepadLeftShoulder);

        state.PressedButtons.Should().BeEquivalentTo([ControllerButton.A, ControllerButton.LeftShoulder]);
    }

    [Fact]
    public void A_trigger_resting_below_the_dead_zone_is_not_a_press()
    {
        // The whole point of the trigger dead zone: a trigger at rest reports a small non-zero value
        // on plenty of real pads, and without this it would spam a keypress down and up forever.
        ControllerState state = Decode(leftTrigger: 5);

        state.IsPressed(ControllerButton.LeftTrigger).Should().BeFalse();
    }

    [Fact]
    public void A_trigger_pulled_past_the_dead_zone_is_a_press()
    {
        ControllerState state = Decode(leftTrigger: 200, rightTrigger: 200);

        state.IsPressed(ControllerButton.LeftTrigger).Should().BeTrue();
        state.IsPressed(ControllerButton.RightTrigger).Should().BeTrue();
    }

    [Fact]
    public void A_stick_resting_inside_the_dead_zone_reports_no_direction()
    {
        ControllerState state = Decode(thumbLX: DeadZone - 1, thumbLY: DeadZone - 1);

        state.PressedButtons.Should().BeEmpty("a stick at rest drifts, and must not scroll the library on its own");
    }

    [Fact]
    public void Pushing_the_stick_up_reports_up_not_down()
    {
        // XInput's Y axis is positive upward, the opposite of screen coordinates - an inversion that
        // is easy to get backwards and impossible to notice without a controller in hand.
        ControllerState state = Decode(thumbLY: short.MaxValue);

        state.IsPressed(ControllerButton.LeftStickUp).Should().BeTrue();
        state.IsPressed(ControllerButton.LeftStickDown).Should().BeFalse();
    }

    [Fact]
    public void Pulling_the_stick_down_reports_down_not_up()
    {
        ControllerState state = Decode(thumbLY: short.MinValue);

        state.IsPressed(ControllerButton.LeftStickDown).Should().BeTrue();
        state.IsPressed(ControllerButton.LeftStickUp).Should().BeFalse();
    }

    [Fact]
    public void Pushing_the_stick_left_and_right_report_the_matching_direction()
    {
        Decode(thumbLX: short.MinValue).IsPressed(ControllerButton.LeftStickLeft).Should().BeTrue();
        Decode(thumbLX: short.MaxValue).IsPressed(ControllerButton.LeftStickRight).Should().BeTrue();
    }

    [Fact]
    public void A_diagonal_push_reports_both_directions_it_lies_between()
    {
        ControllerState state = Decode(thumbLX: short.MaxValue, thumbLY: short.MaxValue);

        state.IsPressed(ControllerButton.LeftStickUp).Should().BeTrue();
        state.IsPressed(ControllerButton.LeftStickRight).Should().BeTrue();
    }

    [Fact]
    public void The_stick_click_buttons_are_unrelated_to_the_stick_directions()
    {
        // LeftThumb is the stick pressed *in*; it must not be confused with pushing it around.
        ControllerState state = Decode(thumbLY: short.MaxValue);

        state.IsPressed(ControllerButton.LeftThumb).Should().BeFalse();
    }

    private static ControllerState Decode(
        ushort buttons = 0,
        byte leftTrigger = 0,
        byte rightTrigger = 0,
        short thumbLX = 0,
        short thumbLY = 0) =>
        XInputControllerReader.Decode(buttons, leftTrigger, rightTrigger, thumbLX, thumbLY);
}
