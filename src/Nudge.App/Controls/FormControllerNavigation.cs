using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Nudge.App.Controls;

/// <summary>
/// Drives a form-shaped page (details, customization, settings) from a controller.
///
/// Unlike the library, these pages use ordinary WPF keyboard focus. Focus traversal is the wrong
/// tool for the tile grid - it cannot move onto containers a virtualizing panel has not realized -
/// but it is exactly right here: every control on a form exists as soon as the page does, and using
/// real focus means the focus visuals, tab order and screen-reader behaviour already built into
/// these controls all work without being reimplemented.
///
/// One instance per page, because it remembers where the focus last was. That memory is the whole
/// difference between navigation that works and navigation that throws you back to the top: WPF
/// drops focus in several ordinary situations - a dropdown closing, a control being re-templated,
/// the page clearing focus on load - and with nowhere to resume from, the next press has no choice
/// but to start again at the first control, which is exactly what "it teleports to the top" was.
/// </summary>
public sealed class FormControllerNavigation
{
    private readonly FrameworkElement _page;
    private UIElement? _lastFocused;

    public FormControllerNavigation(FrameworkElement page) => _page = page;

    /// <summary>
    /// Applies one controller action. Returns true when it was handled here, so the caller can decide
    /// what to do with the rest (Back, mainly).
    /// </summary>
    public bool Apply(ControllerAction action)
    {
        // An open dropdown owns the directions: they move through its list rather than wandering off
        // to the next control on the page behind it. Without this, opening the theme list and
        // pressing down moved page focus instead, so the list could be opened but never used.
        if (OpenDropDown() is { } open)
        {
            ApplyToOpenDropDown(open, action);
            Remember(open);
            return true;
        }

        switch (action)
        {
            case ControllerAction.Down:
            case ControllerAction.Right:
                Move(FocusNavigationDirection.Next);
                return true;

            case ControllerAction.Up:
            case ControllerAction.Left:
                Move(FocusNavigationDirection.Previous);
                return true;

            case ControllerAction.Activate:
                ActivateFocused();
                Remember(Keyboard.FocusedElement as UIElement);
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
    private void Move(FocusNavigationDirection direction)
    {
        UIElement? current = CurrentWithinPage();

        if (current is not null)
        {
            current.MoveFocus(new TraversalRequest(direction));
            Remember(Keyboard.FocusedElement as UIElement);
            BringFocusIntoView();
            return;
        }

        // Focus has been lost. Put it back where it was rather than moving on from there, so the
        // press after a dropdown closes resumes on the control the user was actually on instead of
        // skipping a step - or, worse, restarting at the top of the page.
        if (ResumePoint() is { } resume)
        {
            resume.Focus();
            BringFocusIntoView();
            return;
        }

        // Genuinely nothing to resume from: the first press on this page.
        _page.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        Remember(Keyboard.FocusedElement as UIElement);
        BringFocusIntoView();
    }

    private UIElement? CurrentWithinPage() =>
        Keyboard.FocusedElement is UIElement element && IsWithin(element, _page) ? element : null;

    /// <summary>The remembered control, but only while it is still part of this page's tree.</summary>
    private UIElement? ResumePoint() =>
        _lastFocused is not null && IsWithin(_lastFocused, _page) ? _lastFocused : null;

    private void Remember(UIElement? element)
    {
        if (element is not null && IsWithin(element, _page))
        {
            _lastFocused = element;
        }
    }

    /// <summary>
    /// The dropdown currently showing its list, if any. Checks the remembered control as well as the
    /// focused one: a popup is a separate visual tree, so once the list opens the focused element is
    /// no longer underneath the ComboBox it belongs to.
    /// </summary>
    private ComboBox? OpenDropDown()
    {
        ComboBox? candidate = FindComboBox(Keyboard.FocusedElement as DependencyObject)
                              ?? FindComboBox(_lastFocused);

        return candidate is { IsDropDownOpen: true } ? candidate : null;
    }

    /// <summary>
    /// The dropdown showing its list somewhere at or above <paramref name="focused"/>, if any.
    /// Shared with the library header, which drives focus itself rather than through this class but
    /// needs an open list to behave identically once it is open.
    /// </summary>
    public static ComboBox? OpenDropDownAt(UIElement? focused)
    {
        ComboBox? candidate = FindComboBox(Keyboard.FocusedElement as DependencyObject)
                              ?? FindComboBox(focused);

        return candidate is { IsDropDownOpen: true } ? candidate : null;
    }

    /// <summary>
    /// Moves through an open list. The directions belong to the list while it is up - they must not
    /// wander off to whatever is behind it, which is what made a dropdown openable but unusable: the
    /// list appeared, and pressing down moved the page underneath instead of the selection.
    ///
    /// Everything not handled here is deliberately swallowed, so a stray button cannot act on the
    /// page behind an open list.
    /// </summary>
    public static void ApplyToOpenDropDown(ComboBox open, ControllerAction action)
    {
        switch (action)
        {
            case ControllerAction.Up:
                open.SelectedIndex = Math.Max(0, open.SelectedIndex - 1);
                break;
            case ControllerAction.Down:
                open.SelectedIndex = Math.Min(open.Items.Count - 1, open.SelectedIndex + 1);
                break;
            case ControllerAction.Activate:
            case ControllerAction.Back:
                open.IsDropDownOpen = false;
                open.Focus();
                break;
        }
    }

    private static ComboBox? FindComboBox(DependencyObject? start)
    {
        for (DependencyObject? current = start; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is ComboBox box)
            {
                return box;
            }
        }

        return null;
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
