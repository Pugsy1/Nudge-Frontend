using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;

namespace Nudge.App.Controls;

/// <summary>
/// Drives a form-shaped page (details, customization, settings) from a controller.
///
/// Unlike the library, these pages use ordinary WPF keyboard focus. Focus traversal is the wrong
/// tool for the tile grid - it cannot move onto containers a virtualizing panel has not realized -
/// but it is exactly right here: every control on a form exists as soon as the page does, and using
/// real focus means the focus visuals, tab order and screen-reader behaviour already built into
/// these controls all work without being reimplemented.
/// </summary>
public static class FormControllerNavigation
{
    /// <summary>
    /// Applies one controller action to <paramref name="page"/>. Returns true when the action was
    /// handled here, so the caller can decide what to do with the ones that were not (Back, mainly).
    /// </summary>
    public static bool Apply(FrameworkElement page, ControllerAction action)
    {
        switch (action)
        {
            case ControllerAction.Down:
            case ControllerAction.Right:
                Move(page, FocusNavigationDirection.Next);
                return true;

            case ControllerAction.Up:
            case ControllerAction.Left:
                Move(page, FocusNavigationDirection.Previous);
                return true;

            case ControllerAction.Activate:
                ActivateFocused();
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Next/Previous rather than Up/Down/Left/Right: these pages are a single column of controls in
    /// reading order, and directional traversal on a stacked form frequently finds nothing to move
    /// to (there is no control to the "right" of a full-width toggle), which reads as the pad being
    /// ignored. Tab order always has somewhere to go.
    /// </summary>
    private static void Move(FrameworkElement page, FocusNavigationDirection direction)
    {
        if (Keyboard.FocusedElement is UIElement current && IsWithin(current, page))
        {
            current.MoveFocus(new TraversalRequest(direction));
            BringFocusIntoView();
            return;
        }

        // Nothing on this page has focus yet - the pad is being used before anything was clicked.
        page.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        BringFocusIntoView();
    }

    /// <summary>Scrolls whatever now has focus into sight, since these pages are taller than the window.</summary>
    private static void BringFocusIntoView()
    {
        if (Keyboard.FocusedElement is FrameworkElement focused)
        {
            focused.BringIntoView();
        }
    }

    /// <summary>
    /// Presses whatever has focus. Goes through automation peers rather than type-checking every
    /// control: a Button, a CheckBox and a ComboBox each need a different "activate", and their
    /// peers already express that difference - Invoke for things that do something, Toggle for
    /// things that flip, ExpandCollapse for things that open.
    /// </summary>
    /// <summary>Presses whatever currently has keyboard focus. Public so callers with their own key handling (the library header) can reuse it.</summary>
    public static void ActivateFocused()
    {
        if (Keyboard.FocusedElement is not UIElement focused)
        {
            return;
        }

        AutomationPeer? peer = UIElementAutomationPeer.CreatePeerForElement(focused);
        if (peer is null)
        {
            return;
        }

        if (peer.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke)
        {
            invoke.Invoke();
            return;
        }

        if (peer.GetPattern(PatternInterface.Toggle) is IToggleProvider toggle)
        {
            toggle.Toggle();
            return;
        }

        if (peer.GetPattern(PatternInterface.ExpandCollapse) is IExpandCollapseProvider expand)
        {
            expand.Expand();
        }
    }

    private static bool IsWithin(DependencyObject element, DependencyObject page)
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, page))
            {
                return true;
            }
        }

        return false;
    }
}
