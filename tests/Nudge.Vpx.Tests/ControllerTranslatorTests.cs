using FluentAssertions;
using Nudge.Core.Models;
using Nudge.Vpx.Controller;
using Xunit;

namespace Nudge.Vpx.Tests;

/// <summary>
/// <see cref="ControllerTranslator"/> is pure edge-detection: a button held across many polls must
/// produce exactly one key-down (on the poll it first becomes pressed) and exactly one key-up (on
/// the poll it is first released), never a key-down repeated every poll a button stays held.
/// </summary>
public sealed class ControllerTranslatorTests
{
    private static readonly ControllerMapping Mapping = new(new Dictionary<ControllerButton, VirtualKey>
    {
        [ControllerButton.LeftShoulder] = VirtualKey.LeftShift,
        [ControllerButton.RightShoulder] = VirtualKey.RightShift
    });

    [Fact]
    public void A_button_newly_pressed_produces_a_key_press_and_nothing_to_release()
    {
        ControllerTranslationResult result = ControllerTranslator.Translate(
            State(), State(ControllerButton.LeftShoulder), Mapping);

        result.KeysToPress.Should().Equal(VirtualKey.LeftShift);
        result.KeysToRelease.Should().BeEmpty();
    }

    [Fact]
    public void A_button_held_across_two_polls_produces_nothing_on_the_second_poll()
    {
        ControllerTranslationResult result = ControllerTranslator.Translate(
            State(ControllerButton.LeftShoulder), State(ControllerButton.LeftShoulder), Mapping);

        result.KeysToPress.Should().BeEmpty();
        result.KeysToRelease.Should().BeEmpty();
    }

    [Fact]
    public void A_button_released_produces_a_key_release_and_nothing_to_press()
    {
        ControllerTranslationResult result = ControllerTranslator.Translate(
            State(ControllerButton.LeftShoulder), State(), Mapping);

        result.KeysToRelease.Should().Equal(VirtualKey.LeftShift);
        result.KeysToPress.Should().BeEmpty();
    }

    [Fact]
    public void An_unmapped_button_is_ignored_entirely()
    {
        ControllerTranslationResult result = ControllerTranslator.Translate(
            State(), State(ControllerButton.A), Mapping);

        result.Should().Be(ControllerTranslationResult.None);
    }

    [Fact]
    public void Two_buttons_changing_in_the_same_poll_are_both_reported()
    {
        ControllerTranslationResult result = ControllerTranslator.Translate(
            State(ControllerButton.LeftShoulder), State(ControllerButton.RightShoulder), Mapping);

        result.KeysToPress.Should().Equal(VirtualKey.RightShift);
        result.KeysToRelease.Should().Equal(VirtualKey.LeftShift);
    }

    private static ControllerState State(params ControllerButton[] pressed) =>
        new() { PressedButtons = pressed.ToHashSet() };
}
