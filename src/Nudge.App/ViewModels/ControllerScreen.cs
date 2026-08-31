namespace Nudge.App.ViewModels;

/// <summary>
/// Which context the controller is driving, and therefore which button legend applies. The same
/// buttons do different things in each, so the legend has to follow the focus rather than being
/// written once per page.
/// </summary>
public enum ControllerScreen
{
    /// <summary>Moving between tiles in the library.</summary>
    Library,

    /// <summary>Moving between the header's own controls.</summary>
    Header,

    /// <summary>Holding a slider open and changing its value.</summary>
    SliderAdjust,

    /// <summary>Typing on the on-screen keyboard.</summary>
    Keyboard
}
