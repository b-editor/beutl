using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Skia.Helpers;
using Avalonia.VisualTree;
using SkiaSharp;

namespace Beutl.Controls;

/// <summary>
/// A copy of what the UI draws over the frame of an <see cref="HdrBitmapView"/>: the visuals that come after an anchor,
/// the control holding the frame's place in the layout, in drawing order up to a root. The native view covers what the
/// UI draws in its place, so it draws this copy over the frame itself. Around the frame the UI shows as it is.
/// </summary>
/// <remarks>
/// The copy follows the property changes of the visuals under the root, which is how controls change what they draw.
/// A control whose drawing changes without one calls <see cref="InvalidateVisual"/> instead of
/// <see cref="Visual.InvalidateVisual"/>.
/// </remarks>
internal sealed class HdrOverlay : IDisposable
{
    private static readonly ConditionalWeakTable<Visual, HdrOverlay> s_roots = new();

    private readonly Visual _anchor;
    private readonly Visual _root;
    private readonly HdrBitmapView _target;
    private readonly HashSet<Visual> _tracked = [];
    private bool _recording;
    private bool _disposed;

    public HdrOverlay(Visual anchor, Visual root, HdrBitmapView target)
    {
        _anchor = anchor;
        _root = root;
        _target = target;
        s_roots.AddOrUpdate(root, this);
        Track(root);
    }

    /// <summary>Raised when the copy may differ from what the UI draws now.</summary>
    public event EventHandler? Invalidated;

    /// <summary>
    /// Draws <paramref name="visual"/> again, and the copy an overlay over a frame holds of it.
    /// </summary>
    public static void InvalidateVisual(Visual visual)
    {
        visual.InvalidateVisual();
        for (Visual? ancestor = visual; ancestor != null; ancestor = ancestor.GetVisualParent())
        {
            if (s_roots.TryGetValue(ancestor, out HdrOverlay? overlay) && overlay._tracked.Contains(visual)
                && overlay.Shows(visual, null))
            {
                overlay.Invalidate();
            }
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
                    DrawChildrenAfter(canvas, parent, child, parentToTarget, region, scaling);

                if (ReferenceEquals(parent, _root))
                    break;
            }
        }
        finally
        {
            _recording = false;
        }

        return recorder.EndRecording();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (s_roots.TryGetValue(_root, out HdrOverlay? overlay) && ReferenceEquals(overlay, this))
            s_roots.Remove(_root);

        foreach (Visual visual in _tracked)
            Unsubscribe(visual);

        _tracked.Clear();
        Invalidated = null;
    }

    // The children of parent drawn after child, in the order the renderer draws them: by ZIndex, then as listed.
    private void DrawChildrenAfter(SKCanvas canvas, Visual parent, Visual child, Matrix parentToTarget, Rect region,
        double scaling)
    {
        bool after = false;
        foreach (Visual sibling in parent.GetVisualChildren().OrderBy(visual => visual.ZIndex))
        {
            if (ReferenceEquals(sibling, child))
                after = true;
            else if (after && !ReferenceEquals(sibling, _target) && sibling.IsVisible)
                DrawCopy(canvas, sibling, parentToTarget, region, scaling);
        }
    }

    private void DrawCopy(SKCanvas canvas, Visual visual, Matrix parentToTarget, Rect region, double scaling)
    {
        // Nothing of a visual outside the frame shows, such as the player's footer under it.
        if (visual.TransformToVisual(_target) is not { } toTarget
            || !new Rect(visual.Bounds.Size).TransformToAABB(toTarget).Intersects(region))
        {
            return;
        }

        // Where the visual's layout slot lies in the target. The renderer applies the visual's own transform.
        Matrix slot = Matrix.CreateTranslation(visual.Bounds.Position) * parentToTarget;
        if (!slot.TryInvert(out Matrix inverse))
            return;

        // The part of the slot the region covers, which DrawingContextHelper draws from the canvas's top left corner.
        Rect clip = region.TransformToAABB(inverse);
        using var recorder = new SKPictureRecorder();
        SKCanvas copy = recorder.BeginRecording(SKRect.Create((float)clip.Width, (float)clip.Height));
        DrawingContextHelper.RenderAsync(copy, visual, clip, new Vector(96 * scaling, 96 * scaling))
            .GetAwaiter().GetResult();
        using SKPicture picture = recorder.EndRecording();

        SKMatrix placement = ToSKMatrix(Matrix.CreateTranslation(clip.Position) * slot);
        canvas.Save();
        canvas.Concat(in placement);
        canvas.DrawPicture(picture);
        canvas.Restore();
    }

    private static SKMatrix ToSKMatrix(Matrix m)
    {
        return new SKMatrix(
            (float)m.M11, (float)m.M21, (float)m.M31,
            (float)m.M12, (float)m.M22, (float)m.M32,
            (float)m.M13, (float)m.M23, (float)m.M33);
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
        bool placement = property == Visual.BoundsProperty || property == Visual.IsVisibleProperty
            || property == Visual.RenderTransformProperty || property == Visual.RenderTransformOriginProperty
            || property == Visual.OpacityProperty || property == Visual.ClipToBoundsProperty
            || property == Visual.ClipProperty || property == Visual.ZIndexProperty;

        // The anchor's ancestors draw under the frame, so only where they put what lies over it counts. Most of what
        // changes on them, such as the playhead's time, changes nothing here.
        if (!placement && visual.IsVisualAncestorOf(_anchor))
            return false;

        if (property != Visual.IsVisibleProperty && !visual.IsEffectivelyVisible)
            return false;

        // Where a visual was before its transform changed is not known.
        if (property == Visual.RenderTransformProperty || property == Visual.RenderTransformOriginProperty)
            return true;

        if (visual.TransformToVisual(_target) is not { } toTarget)
            return false;

        if (new Rect(visual.Bounds.Size).TransformToAABB(toTarget).Intersects(region))
            return true;

        // A visual that moved out of the frame leaves its old place to draw again.
        return property == Visual.BoundsProperty && e!.OldValue is Rect old
            && visual.GetVisualParent()?.TransformToVisual(_target) is { } parentToTarget
            && old.TransformToAABB(parentToTarget).Intersects(region);
    }

    private void Invalidate()
    {
        if (!_disposed)
            Invalidated?.Invoke(this, EventArgs.Empty);
    }
}
