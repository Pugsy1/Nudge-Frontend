using FluentAssertions;
using Nudge.Core.Models;
using Xunit;

namespace Nudge.Vpx.Tests;

/// <summary>
/// <see cref="ControllerMapping.Default"/> mirrors Visual Pinball's own out-of-the-box keybindings
/// (see docs/RESEARCH-NOTES.md); <see cref="ControllerMapping.FromOverrides"/> is how a user's
/// per-button remaps (<c>NudgeSettings.ControllerButtonOverrides</c>) land on top of it.
/// </summary>
public sealed class ControllerMappingTests
{
    [Theory]
    [InlineData(ControllerButton.LeftShoulder, VirtualKey.LeftShift)]
    [InlineData(ControllerButton.RightShoulder, VirtualKey.RightShift)]
    [InlineData(ControllerButton.A, VirtualKey.Enter)]
    [InlineData(ControllerButton.Start, VirtualKey.Digit1)]
    [InlineData(ControllerButton.Back, VirtualKey.Digit5)]
    public void Default_matches_Visual_Pinballs_own_out_of_the_box_bindings(ControllerButton button, VirtualKey expected) =>
        ControllerMapping.Default.TryGetKey(button).Should().Be(expected);

    [Fact]
    public void A_button_with_no_default_mapping_returns_null()
    {
        ControllerMapping.Default.TryGetKey(ControllerButton.X).Should().BeNull();
    }

    [Fact]
    public void An_override_replaces_only_the_named_button_and_keeps_every_other_default()
    {
        ControllerMapping mapping = ControllerMapping.FromOverrides(new Dictionary<string, string>
        {
            ["LeftShoulder"] = "Escape"
        });

        mapping.TryGetKey(ControllerButton.LeftShoulder).Should().Be(VirtualKey.Escape);
        mapping.TryGetKey(ControllerButton.RightShoulder).Should().Be(VirtualKey.RightShift, "untouched buttons must keep their default");
    }

    [Fact]
    public void An_override_naming_an_unrecognised_button_or_key_is_skipped_rather_than_failing()
    {
        ControllerMapping mapping = ControllerMapping.FromOverrides(new Dictionary<string, string>
        {
            ["NotARealButton"] = "Enter",
            ["A"] = "NotARealKey"
        });

        mapping.TryGetKey(ControllerButton.A).Should().Be(VirtualKey.Enter, "the malformed override for A must not clobber A's default");
    }

    [Fact]
    public void Overrides_are_case_insensitive()
    {
        ControllerMapping mapping = ControllerMapping.FromOverrides(new Dictionary<string, string>
        {
            ["leftshoulder"] = "escape"
        });

        mapping.TryGetKey(ControllerButton.LeftShoulder).Should().Be(VirtualKey.Escape);
    }

    [Fact]
    public void No_overrides_reproduces_the_default_mapping_exactly()
    {
        ControllerMapping mapping = ControllerMapping.FromOverrides(new Dictionary<string, string>());

        foreach (ControllerButton button in Enum.GetValues<ControllerButton>())
        {
            mapping.TryGetKey(button).Should().Be(ControllerMapping.Default.TryGetKey(button));
        }
    }
}
