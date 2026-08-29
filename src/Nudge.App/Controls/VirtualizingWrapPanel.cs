using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Nudge.App.Controls;

/// <summary>
/// A virtualizing grid of fixed-size tiles that wraps to the available width.
///
/// WPF has no built-in panel that both wraps and virtualizes: <see cref="VirtualizingStackPanel"/>
/// only virtualizes a single row or column. AGENTS.md's performance budget calls UI virtualization
/// "non-negotiable" for a library of up to 1,000 tables, so this realizes only the rows currently
/// on screen (plus a small buffer above and below), recycling containers as the user scrolls rather
/// than keeping every tile alive at once.
/// </summary>
public sealed class VirtualizingWrapPanel : VirtualizingPanel, IScrollInfo
{
    public static readonly DependencyProperty ItemWidthProperty =
        DependencyProperty.Register(nameof(ItemWidth), typeof(double), typeof(VirtualizingWrapPanel),
            new FrameworkPropertyMetadata(200d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ItemHeightProperty =
        DependencyProperty.Register(nameof(ItemHeight), typeof(double), typeof(VirtualizingWrapPanel),
            new FrameworkPropertyMetadata(260d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>
    /// Overrides the column count instead of deriving it from ItemWidth - 0 (the default) keeps the
    /// original "as many ItemWidth-sized columns as fit" behaviour, matching the density slider's
    /// "auto" position. Any other value locks the row to exactly that many tiles, computing an
    /// effective tile width from the available space and scaling ItemHeight to match ItemWidth's
    /// aspect ratio, so every tile still looks like a tile - just a smaller or larger one - rather
    /// than distorting.
    /// </summary>
    public static readonly DependencyProperty ColumnsProperty =
        DependencyProperty.Register(nameof(Columns), typeof(int), typeof(VirtualizingWrapPanel),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Extra rows realized beyond the visible viewport, so a small scroll never has to wait on realization.</summary>
    private const int RowBuffer = 2;

    private Size _extent;
    private Size _viewport;
    private Point _offset;
    private int _columns = 1;

    /// <summary>The tile size actually used for layout this pass - ItemWidth/ItemHeight verbatim when Columns is 0 (auto), otherwise a size fitted to exactly Columns tiles per row at ItemWidth/ItemHeight's aspect ratio.</summary>
    private Size _effectiveItemSize;

    public VirtualizingWrapPanel()
    {
        ClipToBounds = true;
    }

    public double ItemWidth
    {
        get => (double)GetValue(ItemWidthProperty);
        set => SetValue(ItemWidthProperty, value);
    }

    public double ItemHeight
    {
        get => (double)GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    public int Columns
    {
        get => (int)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        int itemCount = ItemsControl.GetItemsOwner(this)?.Items.Count ?? 0;

        int requestedColumns = Columns;
        double aspect = ItemHeight / ItemWidth;

        if (requestedColumns > 0 && !double.IsInfinity(availableSize.Width))
        {
            _columns = Math.Max(1, Math.Min(requestedColumns, Math.Max(1, itemCount)));
            double effectiveWidth = availableSize.Width / requestedColumns;
            _effectiveItemSize = new Size(effectiveWidth, effectiveWidth * aspect);
        }
        else
        {
            _columns = itemCount == 0 || double.IsInfinity(availableSize.Width)
                ? Math.Max(1, itemCount)
                : Math.Max(1, (int)(availableSize.Width / ItemWidth));
            _effectiveItemSize = new Size(ItemWidth, ItemHeight);
        }

        int rows = itemCount == 0 ? 0 : (int)Math.Ceiling(itemCount / (double)_columns);
        double extentWidth = double.IsInfinity(availableSize.Width) ? _effectiveItemSize.Width * itemCount : _columns * _effectiveItemSize.Width;
        double extentHeight = rows * _effectiveItemSize.Height;

        // ScrollContentPresenter measures IScrollInfo content with PositiveInfinity along whichever
        // axis it can scroll (here, height - CanVerticallyScroll is true), precisely so the content
        // decides its own full extent instead of being constrained to what's visible. That means
        // availableSize.Height is never the real viewport height; the only place that size is ever
        // known is ArrangeOverride's finalSize (always finite), so fall back to the last value
        // Arrange recorded rather than an infinite one, which would otherwise make RealizeItems
        // think "every remaining row fits" and the windowing logic realize the wrong rows.
        double viewportHeight = double.IsInfinity(availableSize.Height) ? _viewport.Height : availableSize.Height;
        double viewportWidth = double.IsInfinity(availableSize.Width) ? _viewport.Width : availableSize.Width;

        var newExtent = new Size(extentWidth, extentHeight);
        var newViewport = new Size(viewportWidth, viewportHeight);
        if (newExtent != _extent || newViewport != _viewport)
        {
            _extent = newExtent;
            _viewport = newViewport;
            ScrollOwner?.InvalidateScrollInfo();
        }

        double maxOffset = Math.Max(0, _extent.Height - _viewport.Height);
        if (_offset.Y > maxOffset)
        {
            _offset.Y = maxOffset;
            ScrollOwner?.InvalidateScrollInfo();
        }

        RealizeItems(itemCount, rows);

        return new Size(
            double.IsInfinity(availableSize.Width) ? extentWidth : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? extentHeight : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // The one place the real viewport size is guaranteed finite. If it differs from what
        // MeasureOverride had to guess (see the comment there), record it and ask for another
        // measure pass so RealizeItems windows against the correct size instead of a stale one.
        if (Math.Abs(finalSize.Width - _viewport.Width) > 0.5 || Math.Abs(finalSize.Height - _viewport.Height) > 0.5)
        {
            _viewport = finalSize;
            ScrollOwner?.InvalidateScrollInfo();
            InvalidateMeasure();
        }

        // Centers the block of columns horizontally within any leftover width the floor-division
        // column count didn't use, so a partial remainder splits evenly between both edges instead
        // of piling up on the right.
        double usedWidth = _columns * _effectiveItemSize.Width;
        double columnOffsetX = finalSize.Width > usedWidth ? (finalSize.Width - usedWidth) / 2 : 0;

        for (int i = 0; i < InternalChildren.Count; i++)
        {
            UIElement child = InternalChildren[i];
            int itemIndex = ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));
            if (itemIndex < 0)
            {
                continue;
            }

            int column = itemIndex % _columns;
            int row = itemIndex / _columns;

            double x = columnOffsetX + (column * _effectiveItemSize.Width) - _offset.X;
            double y = (row * _effectiveItemSize.Height) - _offset.Y;

            child.Arrange(new Rect(x, y, _effectiveItemSize.Width, _effectiveItemSize.Height));
        }

        return finalSize;
    }

    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        base.OnItemsChanged(sender, args);

        // This override runs from inside the ItemContainerGenerator's own CollectionChanged
        // handling (OnItemRemoved/OnItemMoved etc. call down into Panel.OnItemsChanged before
        // they've finished updating the generator's internal state). Calling
        // ItemContainerGenerator.Remove synchronously from here - whether one bulk range or one
        // position at a time - re-enters the generator mid-update and throws (confirmed both ways:
        // a stale InternalChildren.Count made the bulk form throw InvalidOperationException, and
        // even the per-item form still threw NullReferenceException). A live filter/sort issuing a
        // single-item Remove (see LibraryViewModel's IsLiveFiltering/IsLiveSorting - unfavoriting a
        // tile while "Favourites only" is selected does exactly this) hits this path; a full
        // collection Reset after a rescan doesn't, which is why this went unnoticed until then.
        // Only invalidating measure here defers all container cleanup to the next layout pass,
        // where RealizeItems/CleanupContainers run from a clean call stack and can touch the
        // generator safely.
        InvalidateMeasure();
    }

    private void RealizeItems(int itemCount, int totalRows)
    {
        if (itemCount == 0)
        {
            CleanupAllContainers();
            return;
        }

        int firstVisibleRow = Math.Max(0, (int)(_offset.Y / _effectiveItemSize.Height) - RowBuffer);
        int visibleRowSpan = (int)Math.Ceiling(_viewport.Height / _effectiveItemSize.Height) + (RowBuffer * 2) + 1;
        int lastVisibleRow = Math.Min(totalRows - 1, firstVisibleRow + visibleRowSpan);

        int firstIndex = firstVisibleRow * _columns;
        int lastIndex = Math.Min(itemCount - 1, ((lastVisibleRow + 1) * _columns) - 1);

        if (firstIndex > lastIndex)
        {
            CleanupAllContainers();
            return;
        }

        IItemContainerGenerator generator = ItemContainerGenerator;
        GeneratorPosition startPos = generator.GeneratorPositionFromIndex(firstIndex);
        int childIndex = startPos.Offset == 0 ? startPos.Index : startPos.Index + 1;

        using (generator.StartAt(startPos, GeneratorDirection.Forward, true))
        {
            for (int itemIndex = firstIndex; itemIndex <= lastIndex; itemIndex++, childIndex++)
            {
                var child = (UIElement)generator.GenerateNext(out bool isNewlyRealized);

                if (isNewlyRealized)
                {
                    if (childIndex >= InternalChildren.Count)
                    {
                        AddInternalChild(child);
                    }
                    else
                    {
                        InsertInternalChild(childIndex, child);
                    }

                    generator.PrepareItemContainer(child);
                }

                child.Measure(_effectiveItemSize);
            }
        }

        CleanupContainers(firstIndex, lastIndex);
    }

    private void CleanupContainers(int firstIndex, int lastIndex)
    {
        for (int i = InternalChildren.Count - 1; i >= 0; i--)
        {
            var position = new GeneratorPosition(i, 0);
            int itemIndex = ItemContainerGenerator.IndexFromGeneratorPosition(position);

            // A negative itemIndex means the generator has already dropped its own mapping for this
            // slot - its underlying item was removed from the source collection (a live filter or
            // sort hiding a tile - unfavoriting one while "Favourites only" is selected does exactly
            // this), and the generator processes that itself before this ever runs. Asking it to
            // Remove a mapping it no longer has throws NullReferenceException; only the now-stale
            // visual child needs cleaning up in that case, not the generator's already-gone entry.
            if (itemIndex < 0)
            {
                RemoveInternalChildRange(i, 1);
            }
            else if (itemIndex < firstIndex || itemIndex > lastIndex)
            {
                ItemContainerGenerator.Remove(position, 1);
                RemoveInternalChildRange(i, 1);
            }
        }
    }

    // An empty valid range: every currently realized container falls outside it, so this reuses
    // CleanupContainers' per-item logic (including the negative-itemIndex handling above) instead
    // of a second, separately-maintained sweep.
    private void CleanupAllContainers() => CleanupContainers(0, -1);

    #region IScrollInfo

    public bool CanHorizontallyScroll { get; set; }

    public bool CanVerticallyScroll { get; set; }

    public double ExtentWidth => _extent.Width;

    public double ExtentHeight => _extent.Height;

    public double ViewportWidth => _viewport.Width;

    public double ViewportHeight => _viewport.Height;

    public double HorizontalOffset => _offset.X;

    public double VerticalOffset => _offset.Y;

    public ScrollViewer? ScrollOwner { get; set; }

    public void LineUp() => SetVerticalOffset(_offset.Y - (_effectiveItemSize.Height / 3));

    public void LineDown() => SetVerticalOffset(_offset.Y + (_effectiveItemSize.Height / 3));

    public void LineLeft()
    {
    }

    public void LineRight()
    {
    }

    public void PageUp() => SetVerticalOffset(_offset.Y - _viewport.Height);

    public void PageDown() => SetVerticalOffset(_offset.Y + _viewport.Height);

    public void PageLeft()
    {
    }

    public void PageRight()
    {
    }

    public void MouseWheelUp() => SetVerticalOffset(_offset.Y - (_effectiveItemSize.Height / 2));

    public void MouseWheelDown() => SetVerticalOffset(_offset.Y + (_effectiveItemSize.Height / 2));

    public void MouseWheelLeft()
    {
    }

    public void MouseWheelRight()
    {
    }

    public void SetHorizontalOffset(double offset)
    {
        // Horizontal scrolling is not offered - the grid always wraps to the available width.
    }

    public void SetVerticalOffset(double offset)
    {
        double clamped = Math.Max(0, Math.Min(offset, Math.Max(0, _extent.Height - _viewport.Height)));
        if (Math.Abs(clamped - _offset.Y) < 0.5)
        {
            return;
        }

        _offset.Y = clamped;
        ScrollOwner?.InvalidateScrollInfo();
        InvalidateMeasure();
    }

    public Rect MakeVisible(Visual visual, Rect rectangle) => rectangle;

    #endregion
}
