using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Skia;
using Avalonia.Skia.Helpers;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkiaSharp;

namespace Beutl.Controls;

/// <summary>
/// A copy of what the UI draws over the frame of an <see cref="HdrBitmapView"/>: the visuals that come after an anchor,
/// the control holding the frame's place in the layout, in drawing order up to the root of the window, whose dialogs and
/// other overlays come last. The native view covers what the UI draws in its place, so it draws this copy over the frame
/// itself. Around the frame the UI shows as it is.
/// </summary>
/// <remarks>
/// The copy follows the property changes of the visuals under a root, which is how controls change what they draw. Above
/// the root it follows those of a visual drawn later only while its bounds meet the view, as a window's layers do, and
/// otherwise only where it lies: a panel beside the view draws nothing over the frame, however often it changes.
/// A control whose drawing changes without a property change calls <see cref="InvalidateVisual"/> instead of
/// <see cref="Visual.InvalidateVisual"/>. A control is drawn only where its bounds meet the frame, so one that draws
/// past them takes the size it draws in. So is each layer, a child that an ancestor of the anchor draws after the
/// anchor's branch, with all it holds: a control that reaches out of its layer into the frame is not in the copy.
/// </remarks>
internal sealed class HdrOverlay : IDisposable
{
    private static readonly ConditionalWeakTable<Visual, HdrOverlay> s_roots = new();

    private readonly Visual _anchor;
    private readonly Visual _root;
    private readonly HdrBitmapView _target;
    private readonly HashSet<Visual> _tracked = [];
    // Above the root: its ancestors up to the window's root, and the visuals they draw after the root's branch.
    private List<Visual> _ancestors = [];
    private List<Visual> _later = [];
    private bool _aboveRootQueued;
    private bool _recording;
    private bool _disposed;

    public HdrOverlay(Visual anchor, Visual root, HdrBitmapView target)
    {
        _anchor = anchor;
        _root = root;
        _target = target;
        s_roots.AddOrUpdate(root, this);
        Track(root);

        root.AttachedToVisualTree += OnRootAttachedToVisualTree;
        target.PropertyChanged += OnTargetPropertyChanged;
        // The view records a copy when it takes the overlay, so there is nothing to invalidate yet.
        FollowAboveRoot(root.IsAttachedToVisualTree());
    }

    /// <summary>Raised when the copy may differ from what the UI draws now.</summary>
    public event EventHandler? Invalidated;

    /// <summary>
    /// Draws <paramref name="visual"/> again, and the copy an overlay over a frame holds of it.
    /// </summary>
    public static void InvalidateVisual(Visual visual)
    {
        visual.InvalidateVisual();
        // A visual a copy holds lies under its root, or above it in the same window, such as in a dialog.
        foreach (KeyValuePair<Visual, HdrOverlay> entry in s_roots)
        {
            HdrOverlay overlay = entry.Value;
            if (overlay._tracked.Contains(visual) && overlay.Shows(visual, null))
                overlay.Invalidate();
        }
    }

    /// <summary>
    /// Records the copy in the target's coordinates, for a target <paramref name="size"/> in size. Only
    /// <paramref name="region"/>, the part of the target the frame takes, is drawn: elsewhere the UI shows itself.
    /// </summary>
    /// <param name="scaling">The device pixels per unit the copy is drawn at.</param>
    public SKPicture Record(Size size, Rect region, double scaling)
    {
        using var recorder = new SKPictureRecorder();
        SKCanvas canvas = recorder.BeginRecording(SKRect.Create((float)size.Width, (float)size.Height));
        canvas.ClipRect(SKRect.Create((float)region.X, (float)region.Y, (float)region.Width, (float)region.Height));

        _recording = true;
        try
        {
            Visual child = _anchor;
            for (Visual? parent = child.GetVisualParent(); parent != null; child = parent, parent = parent.GetVisualParent())
            {
                if (parent.TransformToVisual(_target) is { } parentToTarget)
                {
                    foreach (Visual later in ChildrenAfter(parent, child))
                    {
                        if (!ReferenceEquals(later, _target) && later.IsVisible)
                            DrawCopy(canvas, later, parentToTarget, region, scaling);
                    }
                }

                if (ReferenceEquals(parent, _root))
                    break;
            }

            // Above the root only what meets the view can draw over the frame, such as a dialog in the window's overlay
            // layer, and those the copy follows come in drawing order. Walking the window's ancestors again for each copy
            // cost several times the rest of it.
            foreach (Visual later in _later)
            {
                if (later.IsVisible && _tracked.Contains(later)
                    && later.GetVisualParent()?.TransformToVisual(_target) is { } parentToTarget)
                {
                    DrawCopy(canvas, later, parentToTarget, region, scaling);
                }
            }
        }
        finally
        {
            _recording = false;
        }

        return recorder.EndRecording();
    }

    // Whether the copy follows what a visual draws, rather than at most where it lies.
    internal bool Follows(Visual visual) => _tracked.Contains(visual);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (s_roots.TryGetValue(_root, out HdrOverlay? overlay) && ReferenceEquals(overlay, this))
            s_roots.Remove(_root);

        _root.AttachedToVisualTree -= OnRootAttachedToVisualTree;
        _target.PropertyChanged -= OnTargetPropertyChanged;
        FollowAboveRoot(attached: false);

        foreach (Visual visual in _tracked)
            Unsubscribe(visual);

        _tracked.Clear();
        Invalidated = null;
    }

    // The children of parent drawn after child, in the order the renderer draws them: by ZIndex, then as listed.
    private static IEnumerable<Visual> ChildrenAfter(Visual parent, Visual child)
    {
        bool after = false;
        foreach (Visual sibling in parent.GetVisualChildren().OrderBy(visual => visual.ZIndex))
        {
            if (after)
                yield return sibling;
            else if (ReferenceEquals(sibling, child))
                after = true;
        }
    }

    private void DrawCopy(SKCanvas canvas, Visual visual, Matrix parentToTarget, Rect region, double scaling)
    {
        // Nothing of a visual outside the frame shows, such as the player's footer under it.
        if (!Meets(visual, region))
            return;

        // Where the visual's layout slot lies in the target. The renderer applies the visual's own transform.
        Matrix slot = Matrix.CreateTranslation(visual.Bounds.Position) * parentToTarget;
        if (!slot.TryInvert(out Matrix inverse))
            return;

        // The part of the slot the region covers, which DrawingContextHelper draws from the canvas's top left corner. It
        // culls the visual's descendants against a rectangle of the clip's size at the visual's top left corner, not at
        // the clip, so the clip reaches back to that corner; the canvas's clip drops what lies outside the region.
        Rect covered = region.TransformToAABB(inverse);
        var clip = new Rect(new Point(Math.Min(covered.X, 0), Math.Min(covered.Y, 0)), covered.BottomRight);
        using var recorder = new SKPictureRecorder();
        SKCanvas copy = recorder.BeginRecording(SKRect.Create((float)clip.Width, (float)clip.Height));
        DrawingContextHelper.RenderAsync(copy, visual, clip, new Vector(96 * scaling, 96 * scaling))
            .GetAwaiter().GetResult();
        using SKPicture picture = recorder.EndRecording();

        SKMatrix placement = (Matrix.CreateTranslation(clip.Position) * slot).ToSKMatrix();
        canvas.Save();
        canvas.Concat(in placement);
        canvas.DrawPicture(picture);
        canvas.Restore();
    }

    private void Track(Visual visual)
    {
        // The anchor holds the frame's place and the target shows the frame, so the copy has nothing of either.
        if (ReferenceEquals(visual, _anchor) || ReferenceEquals(visual, _target) || !_tracked.Add(visual))
            return;

        visual.PropertyChanged += OnPropertyChanged;
        if (visual.GetVisualChildren() is INotifyCollectionChanged children)
            children.CollectionChanged += OnChildrenChanged;

        foreach (Visual child in visual.GetVisualChildren())
            Track(child);
    }

    private void Untrack(Visual visual)
    {
        if (!_tracked.Remove(visual))
            return;

        Unsubscribe(visual);
        foreach (Visual child in visual.GetVisualChildren())
            Untrack(child);
    }

    private void Unsubscribe(Visual visual)
    {
        visual.PropertyChanged -= OnPropertyChanged;
        if (visual.GetVisualChildren() is INotifyCollectionChanged children)
            children.CollectionChanged -= OnChildrenChanged;
    }

    // A root leaving its window changes the children of an ancestor it follows, which lets go of the window's visuals.
    private void OnRootAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e) => QueueFollowAboveRoot();

    // Whether the view's bounds meet another visual drawn over the frame changes as the view moves or resizes.
    private void OnTargetPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Visual.BoundsProperty)
            QueueFollowAboveRoot();
    }

    private void OnAboveRootPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (!_recording && IsPlacement(e.Property))
            QueueFollowAboveRoot();
    }

    private void OnAncestorChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e) => QueueFollowAboveRoot();

    // Follows the root's ancestors again once layout settles, so any number of changes take one pass, and invalidates
    // the copy: what lies above the root may have come, gone or moved over the frame.
    private void QueueFollowAboveRoot()
    {
        if (_aboveRootQueued || _disposed)
            return;

        _aboveRootQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _aboveRootQueued = false;
            if (_disposed)
                return;

            FollowAboveRoot(_root.IsAttachedToVisualTree());
            Invalidate();
        }, DispatcherPriority.Loaded);
    }

    // Above the root, the copy follows where the root's ancestors and the visuals they draw after its branch lie, and
    // what the latter draw only while their bounds meet the view's, as a window's layers do. A panel beside the view, such
    // as the timeline, draws nothing over the frame however often it changes.
    private void FollowAboveRoot(bool attached)
    {
        var ancestors = new List<Visual>();
        var later = new List<Visual>();
        if (attached)
        {
            Visual child = _root;
            for (Visual? parent = child.GetVisualParent(); parent != null; child = parent, parent = parent.GetVisualParent())
            {
                ancestors.Add(parent);
                later.AddRange(ChildrenAfter(parent, child));
            }
        }

        // A few dozen each, so the lists are searched rather than hashed.
        foreach (Visual ancestor in _ancestors)
        {
            if (ancestors.Contains(ancestor))
                continue;

            ancestor.PropertyChanged -= OnAboveRootPropertyChanged;
            if (ancestor.GetVisualChildren() is INotifyCollectionChanged children)
                children.CollectionChanged -= OnAncestorChildrenChanged;
        }

        foreach (Visual ancestor in ancestors)
        {
            if (_ancestors.Contains(ancestor))
                continue;

            ancestor.PropertyChanged += OnAboveRootPropertyChanged;
            if (ancestor.GetVisualChildren() is INotifyCollectionChanged children)
                children.CollectionChanged += OnAncestorChildrenChanged;
        }

        foreach (Visual visual in _later)
        {
            if (later.Contains(visual))
                continue;

            visual.PropertyChanged -= OnAboveRootPropertyChanged;
            Untrack(visual);
        }

        foreach (Visual visual in later)
        {
            if (!_later.Contains(visual))
                visual.PropertyChanged += OnAboveRootPropertyChanged;
        }

        var view = new Rect(_target.Bounds.Size);
        foreach (Visual visual in later)
        {
            if (visual.IsVisible && Meets(visual, view))
                Track(visual);
            else
                Untrack(visual);
        }

        _ancestors = ancestors;
        _later = later;
    }

    // Whether a visual's bounds meet the rectangle, in the view's coordinates.
    private bool Meets(Visual visual, Rect rect)
    {
        return rect.Width > 0 && rect.Height > 0
            && visual.TransformToVisual(_target) is { } toTarget
            && new Rect(visual.Bounds.Size).TransformToAABB(toTarget).Intersects(rect);
    }

    // Whether a property changes where a visual draws, or whether it does at all, rather than what it draws.
    private static bool IsPlacement(AvaloniaProperty? property)
    {
        return property == Visual.BoundsProperty || property == Visual.IsVisibleProperty
            || property == Visual.RenderTransformProperty || property == Visual.RenderTransformOriginProperty
            || property == Visual.OpacityProperty || property == Visual.ClipToBoundsProperty
            || property == Visual.ClipProperty || property == Visual.ZIndexProperty;
    }

    private void OnChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Where a removed visual was is not known any more. A new one that has no place yet shows when it takes one.
        bool shows = false;
        if (e.OldItems != null)
        {
            foreach (Visual visual in e.OldItems)
                shows |= _tracked.Contains(visual);

            foreach (Visual visual in e.OldItems)
                Untrack(visual);
        }

        if (e.NewItems != null)
        {
            foreach (Visual visual in e.NewItems)
            {
                Track(visual);
                shows |= _tracked.Contains(visual) && Shows(visual, null);
            }
        }

        if (shows)
            Invalidate();
    }

    private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (!_recording && sender is Visual visual && Shows(visual, e))
            Invalidate();
    }

    // Whether a change can show in the copy: the visual is seen, and it lies in the frame, or did before it moved.
    private bool Shows(Visual visual, AvaloniaPropertyChangedEventArgs? e)
    {
        Rect region = _target.FrameRegion;
        if (!(region.Width > 0 && region.Height > 0))
            return false;

        AvaloniaProperty? property = e?.Property;

        // The anchor's ancestors draw under the frame, so only where they put what lies over it counts. Most of what
        // changes on them, such as the playhead's time, changes nothing here.
        if (!IsPlacement(property) && visual.IsVisualAncestorOf(_anchor))
            return false;

        if (property != Visual.IsVisibleProperty && !visual.IsEffectivelyVisible)
            return false;

        // Where a visual was before its transform changed is not known.
        if (property == Visual.RenderTransformProperty || property == Visual.RenderTransformOriginProperty)
            return IsInDrawnLayer(visual, region);

        if (Meets(visual, region))
            return IsInDrawnLayer(visual, region);

        // A visual that moved out of the frame leaves its old place to draw again.
        return property == Visual.BoundsProperty && e!.OldValue is Rect old
            && visual.GetVisualParent()?.TransformToVisual(_target) is { } parentToTarget
            && old.TransformToAABB(parentToTarget).Intersects(region)
            && IsInDrawnLayer(visual, region);
    }

    // Whether the copy draws the layer a visual lies in. The copy draws a layer, with all it holds, only where the layer's
    // own bounds meet the frame, so nothing that reaches out of a layer into the frame is in the copy, and nothing it
    // changes shows there: the player's seek bar reaches out of the footer over the frame only to be easy to grab.
    private bool IsInDrawnLayer(Visual visual, Rect region)
    {
        return LayerOf(visual) is not { } layer || ReferenceEquals(layer, visual) || Meets(layer, region);
    }

    // The layer the copy draws a visual with: the child, on the visual's side, of its nearest ancestor in common with the
    // anchor, as Record walks the anchor's ancestors. Null for the anchor's ancestors and for a visual in another tree.
    private Visual? LayerOf(Visual visual)
    {
        Visual? layer = null;
        Visual? side = visual;
        Visual? anchorSide = _anchor;
        int depth = Depth(visual) - Depth(_anchor);
        for (; depth > 0; depth--)
        {
            layer = side;
            side = side!.GetVisualParent();
        }

        for (; depth < 0; depth++)
            anchorSide = anchorSide!.GetVisualParent();

        while (!ReferenceEquals(side, anchorSide))
        {
            layer = side;
            side = side!.GetVisualParent();
            anchorSide = anchorSide!.GetVisualParent();
        }

        return side == null ? null : layer;
    }

    private static int Depth(Visual visual)
    {
        int depth = 0;
        for (Visual? parent = visual.GetVisualParent(); parent != null; parent = parent.GetVisualParent())
            depth++;

        return depth;
    }

    private void Invalidate()
    {
        if (!_disposed)
            Invalidated?.Invoke(this, EventArgs.Empty);
    }
}
