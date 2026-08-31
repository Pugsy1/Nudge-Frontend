using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Nudge.App.Controls;

/// <summary>
/// Scrolls a ComboBox's own selected item into view every time its dropdown opens - set as an
/// attached property from ComboBox.Standard's Style (Themes/Controls.xaml) rather than needing a
/// code-behind on every view that uses that style, since a ResourceDictionary has no code-behind of
/// its own for a plain EventSetter to bind to.
///
/// Without this, opening the theme dropdown always starts scrolled to the top of the list
/// regardless of which theme is actually selected (there may be a dozen options above it) - a real
/// ComboBox's default behaviour is to already be scrolled to the selection when it opens, and this
/// restores that for the fully custom template ComboBox.Standard uses instead of the stock one.
/// </summary>
public static class ComboBoxBehavior
{
    public static readonly DependencyProperty ScrollSelectedIntoViewOnOpenProperty =
        DependencyProperty.RegisterAttached(
            "ScrollSelectedIntoViewOnOpen",
            typeof(bool),
            typeof(ComboBoxBehavior),
            new PropertyMetadata(false, OnScrollSelectedIntoViewOnOpenChanged));

    public static bool GetScrollSelectedIntoViewOnOpen(DependencyObject element) =>
        (bool)element.GetValue(ScrollSelectedIntoViewOnOpenProperty);

    public static void SetScrollSelectedIntoViewOnOpen(DependencyObject element, bool value) =>
        element.SetValue(ScrollSelectedIntoViewOnOpenProperty, value);

    private static void OnScrollSelectedIntoViewOnOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ComboBox comboBox)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            comboBox.DropDownOpened += OnDropDownOpened;
        }
        else
        {
            comboBox.DropDownOpened -= OnDropDownOpened;
        }
    }

    private static void OnDropDownOpened(object? sender, EventArgs e)
    {
        if (sender is not ComboBox { SelectedItem: { } selected } comboBox)
        {
            return;
        }

        // Deferred to ContextIdle - lower priority than Loaded/Render/Input, so this runs after the
        // popup's own opening layout pass, its Fade PopupAnimation, and anything else queued in
        // response to the dropdown opening. Loaded was tried first and did scroll to the right place
        // - but then visibly snapped back to the top a moment later, because something later in that
        // same open sequence (most likely the newly-selected ComboBoxItem's own IsSelected visual
        // state change triggering another layout pass) re-ran after it and won. Running last, instead
        // of early, is what actually sticks.
        comboBox.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            if (comboBox.ItemContainerGenerator.ContainerFromItem(selected) is FrameworkElement container)
            {
                container.BringIntoView();
            }
        }));
    }
}
