using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Media.TextFormatting;
using Microsoft.Extensions.Logging;

namespace Beutl.Graphics.Rendering;

public sealed class GraphicsContext2D(
    ContainerRenderNode container,
    Size canvasSize = default,
    float outputScale = 1f)
    : IDisposable, IPopable
{
    private readonly Stack<(ContainerRenderNode Container, int OperationIndex, bool HasChanges)> _nodes = [];
    private int _drawOperationindex;

    private readonly ContainerRenderNode _rootContainer = container;
    private ContainerRenderNode _container = container;

    // Belongs to the innermost open scope, not to the pass: it accumulates until that scope closes.
    private bool _hasChanges;
    private bool _faulted;

    /// <summary>The logical viewport size (float, not rounded to device pixels).</summary>
    public Size Size => canvasSize;

    /// <summary>The output scale <c>s_out</c> this context was built for.</summary>
    public float OutputScale => outputScale;

    internal Action<RenderNode>? OnUntracked { get; set; }

    internal void MarkChanges()
    {
        _hasChanges = true;
    }

    private void Untracked(RenderNode? node)
    {
        if (node != null) OnUntracked?.Invoke(node);
    }

    private void Add(RenderNode node)
    {
        RenderNode? previous = null;
        bool replacementAttempted = false;
        try
        {
            if (_drawOperationindex < _container.Children.Count)
            {
                previous = _container.Children[_drawOperationindex];
                replacementAttempted = true;
                _container.SetChild(_drawOperationindex, node);
                Untracked(previous);
            }
            else
            {
                _container.AddChild(node);
            }
        }
        catch
        {
            if (replacementAttempted
                && previous is not null
                && ReferenceEquals(_container.Children[_drawOperationindex], node))
            {
                try
                {
                    _container.SetChild(_drawOperationindex, previous);
                }
                catch (Exception cleanupFailure)
                {
                    ReportCleanupFailure(cleanupFailure, "rolling back a rejected render node");
                }
            }

            try
            {
                node.Dispose();
            }
            catch (Exception cleanupFailure)
            {
                // Preserve the recording failure that prevented ownership transfer.
                ReportCleanupFailure(cleanupFailure, "disposing a rejected render node");
            }

            throw;
        }

        _hasChanges = true;
    }

    private void AddAndPush(ContainerRenderNode node)
    {
        Add(node);
        Push(node);
    }

    private void Push(ContainerRenderNode node)
    {
        _nodes.Push((_container, _drawOperationindex + 1, _hasChanges));

        _drawOperationindex = 0;
        _container = node;
        _hasChanges = false;
    }

    private void CloseScope(in (ContainerRenderNode Container, int OperationIndex, bool HasChanges) state)
    {
        if (!_faulted)
        {
            TrimTrailingNodes(_container, _drawOperationindex);
            _container.HasChanges |= _hasChanges;
        }

        bool scopeChanges = _hasChanges;
        _container = state.Container;
        _drawOperationindex = state.OperationIndex;
        _hasChanges = state.HasChanges | scopeChanges;
    }

    private T? Next<T>() where T : RenderNode
    {
        if (_drawOperationindex < _container.Children.Count)
        {
            var node = _container.Children[_drawOperationindex];
            // is-asだと、Next<FilterEffectRenderNode>()のような呼び出しで継承されていないノードが欲しいのにNodeGraphFilterEffectRenderNodeが返ってきてしまうので、
            // GetType() == typeof(T)で厳密に型を比較する。
            if (node.GetType() == typeof(T))
            {
                return (T)node;
            }
        }

        return null;
    }

    private RenderNode? Next()
    {
        return _drawOperationindex < _container.Children.Count ? _container.Children[_drawOperationindex] : null;
    }

    public void Dispose()
    {
        if (_faulted)
            return;

        TrimTrailingNodes(_container, _drawOperationindex);
        _container.HasChanges |= _hasChanges;
    }

    private bool BeginRecordingOperation()
    {
        bool wasFaulted = _faulted;
        _faulted = true;
        return wasFaulted;
    }

    private void CompleteRecordingOperation(bool wasFaulted)
    {
        _faulted = wasFaulted;
    }

    private void TrimTrailingNodes(ContainerRenderNode container, int start)
    {
        RenderNode[] removed;
        try
        {
            int count = container.Children.Count - start;
            if (count == 0)
                return;

            removed = [.. container.Children.Skip(start)];
            container.RemoveRange(start, count);
            container.HasChanges = true;
            _hasChanges = true;
        }
        catch (Exception cleanupFailure)
        {
            // Recording cleanup must not replace an exception already leaving the caller.
            ReportCleanupFailure(cleanupFailure, "detaching trailing render nodes");
            return;
        }

        foreach (RenderNode node in removed)
        {
            try
            {
                node.Dispose();
            }
            catch (Exception cleanupFailure)
            {
                // Continue discharging every detached node.
                ReportCleanupFailure(cleanupFailure, "disposing a detached render node");
            }

            try
            {
                Untracked(node);
            }
            catch (Exception cleanupFailure)
            {
                // Untracking notifications are cleanup and must not escape Dispose/Pop.
                ReportCleanupFailure(cleanupFailure, "notifying a detached render node");
            }
        }
    }

    private static void ReportCleanupFailure(Exception exception, string operation)
    {
        try
        {
            Log.CreateLogger<GraphicsContext2D>().LogWarning(
                exception,
                "GraphicsContext2D cleanup failed while {Operation}; continuing cleanup.",
                operation);
        }
        catch
        {
            // Logging must not replace either the recording failure or the cleanup failure being suppressed.
        }
    }

    public void Reset()
    {
        _drawOperationindex = 0;
        _nodes.Clear();
        _container = _rootContainer;
        _faulted = false;
        _hasChanges = false;
    }

    public MemoryNode<T> UseMemory<T>(T defaultValue)
    {
        bool wasFaulted = BeginRecordingOperation();
        MemoryNode<T>? next = Next<MemoryNode<T>>();

        if (next == null)
        {
            next = new MemoryNode<T>(defaultValue);
            Add(next);
        }

        ++_drawOperationindex;
        CompleteRecordingOperation(wasFaulted);
        return next;
    }

    public MemoryNode<T?> UseMemory<T>()
    {
        return UseMemory<T?>(default);
    }

    public void Clear()
    {
        bool wasFaulted = BeginRecordingOperation();
        ClearRenderNode? next = Next<ClearRenderNode>();

        if (next == null || !next.Equals(default))
        {
            Add(new ClearRenderNode(default));
        }

        ++_drawOperationindex;
        CompleteRecordingOperation(wasFaulted);
    }

    public void Clear(Color color)
    {
        bool wasFaulted = BeginRecordingOperation();
        ClearRenderNode? next = Next<ClearRenderNode>();

        if (next == null || !next.Equals(color))
        {
            Add(new ClearRenderNode(color));
        }

        ++_drawOperationindex;
        CompleteRecordingOperation(wasFaulted);
    }

    public void DrawImageSource(ImageSource.Resource source, Brush.Resource? fill, Pen.Resource? pen)
    {
        bool wasFaulted = BeginRecordingOperation();
        if (fill != null) ObjectDisposedException.ThrowIf(fill.IsDisposed, fill);
        if (pen != null) ObjectDisposedException.ThrowIf(pen.IsDisposed, pen);
        ArgumentNullException.ThrowIfNull(source);
        ObjectDisposedException.ThrowIf(source.IsDisposed, source);

        ImageSourceRenderNode? next = Next<ImageSourceRenderNode>();

        if (next == null)
        {
            Add(new ImageSourceRenderNode(source, fill, pen));
        }
        else
        {
            _hasChanges |= next.Update(source, fill, pen);
        }

        ++_drawOperationindex;
        CompleteRecordingOperation(wasFaulted);
    }

    public void DrawVideoSource(VideoSource.Resource source, TimeSpan frame, Brush.Resource? fill, Pen.Resource? pen)
    {
        bool wasFaulted = BeginRecordingOperation();
        if (fill != null) ObjectDisposedException.ThrowIf(fill.IsDisposed, fill);
        if (pen != null) ObjectDisposedException.ThrowIf(pen.IsDisposed, pen);
        ArgumentNullException.ThrowIfNull(source);
        ObjectDisposedException.ThrowIf(source.IsDisposed, source);

        Rational rate = source.FrameRate;
        double frameNum = frame.TotalSeconds * (rate.Numerator / (double)rate.Denominator);
        DrawVideoSource(source, (int)frameNum, fill, pen);
        CompleteRecordingOperation(wasFaulted);
    }

    public void DrawVideoSource(VideoSource.Resource source, int frame, Brush.Resource? fill, Pen.Resource? pen)
    {
        bool wasFaulted = BeginRecordingOperation();
        if (fill != null) ObjectDisposedException.ThrowIf(fill.IsDisposed, fill);
        if (pen != null) ObjectDisposedException.ThrowIf(pen.IsDisposed, pen);
        ArgumentNullException.ThrowIfNull(source);
        ObjectDisposedException.ThrowIf(source.IsDisposed, source);

        VideoSourceRenderNode? next = Next<VideoSourceRenderNode>();

        if (next == null)
        {
            Add(new VideoSourceRenderNode(source, frame, fill, pen));
        }
        else
        {
            _hasChanges |= next.Update(source, frame, fill, pen);
        }

        ++_drawOperationindex;
        CompleteRecordingOperation(wasFaulted);
    }

    public void DrawEllipse(Rect rect, Brush.Resource? fill, Pen.Resource? pen)
    {
        bool wasFaulted = BeginRecordingOperation();
        if (fill != null) ObjectDisposedException.ThrowIf(fill.IsDisposed, fill);
        if (pen != null) ObjectDisposedException.ThrowIf(pen.IsDisposed, pen);

        EllipseRenderNode? next = Next<EllipseRenderNode>();

        if (next == null)
        {
            Add(new EllipseRenderNode(rect, fill, pen));
        }
        else
        {
            _hasChanges |= next.Update(rect, fill, pen);
        }

        ++_drawOperationindex;
        CompleteRecordingOperation(wasFaulted);
    }

    public void DrawGeometry(Geometry.Resource geometry, Brush.Resource? fill, Pen.Resource? pen)
    {
        bool wasFaulted = BeginRecordingOperation();
        if (fill != null) ObjectDisposedException.ThrowIf(fill.IsDisposed, fill);
        if (pen != null) ObjectDisposedException.ThrowIf(pen.IsDisposed, pen);
        ArgumentNullException.ThrowIfNull(geometry);
        ObjectDisposedException.ThrowIf(geometry.IsDisposed, geometry);

        GeometryRenderNode? next = Next<GeometryRenderNode>();

        if (next == null)
        {
            Add(new GeometryRenderNode(geometry, fill, pen));
        }
        else
        {
            _hasChanges |= next.Update(geometry, fill, pen);
        }

        ++_drawOperationindex;
        CompleteRecordingOperation(wasFaulted);
    }

    public void DrawRectangle(Rect rect, Brush.Resource? fill, Pen.Resource? pen)
    {
        bool wasFaulted = BeginRecordingOperation();
        if (fill != null) ObjectDisposedException.ThrowIf(fill.IsDisposed, fill);
        if (pen != null) ObjectDisposedException.ThrowIf(pen.IsDisposed, pen);

        RectangleRenderNode? next = Next<RectangleRenderNode>();

        if (next == null)
        {
            Add(new RectangleRenderNode(rect, fill, pen));
        }
        else
        {
            _hasChanges |= next.Update(rect, fill, pen);
        }

        ++_drawOperationindex;
        CompleteRecordingOperation(wasFaulted);
    }

    public void DrawText(FormattedText text, Brush.Resource? fill, Pen.Resource? pen)
    {
        bool wasFaulted = BeginRecordingOperation();
        if (fill != null) ObjectDisposedException.ThrowIf(fill.IsDisposed, fill);
        if (pen != null) ObjectDisposedException.ThrowIf(pen.IsDisposed, pen);
        ArgumentNullException.ThrowIfNull(text);

        TextRenderNode? next = Next<TextRenderNode>();

        if (next == null)
        {
            Add(new TextRenderNode(text, fill, pen));
        }
        else
        {
            _hasChanges |= next.Update(text, fill, pen);
        }

        ++_drawOperationindex;
        CompleteRecordingOperation(wasFaulted);
    }

    public void DrawDrawable(Drawable.Resource drawable)
    {
        bool wasFaulted = BeginRecordingOperation();
        ArgumentNullException.ThrowIfNull(drawable);
        ObjectDisposedException.ThrowIf(drawable.IsDisposed, drawable);

        ContainerRenderNode parent = _container;
        int operationIndex = _drawOperationindex;
        DrawableRenderNode? next = Next<DrawableRenderNode>();

        if (next == null)
        {
            AddAndPush(new DrawableRenderNode(drawable));
        }
        else
        {
            _hasChanges |= next.Update(drawable);
            Push(next);
        }

        int count = _nodes.Count;
        CompleteRecordingOperation(wasFaulted);
        try
        {
            var obj = drawable.GetOriginal();
            obj.Render(this, drawable);
        }
        catch
        {
            _faulted = true;
            TrimTrailingNodes(parent, operationIndex);
            throw;
        }
        finally
        {
            Pop(count);
        }
    }

    public void DrawNode(RenderNode node)
    {
        bool wasFaulted = BeginRecordingOperation();
        ArgumentNullException.ThrowIfNull(node);
        ObjectDisposedException.ThrowIf(node.IsDisposed, node);

        RenderNode? next = Next();

        if (next == null || !node.Equals(next))
        {
            Add(node);
        }

        ++_drawOperationindex;
        CompleteRecordingOperation(wasFaulted);
    }

    public void DrawNode<TNode, TParams>(in TParams parameters, Func<TParams, TNode> createNode,
        Func<TNode, TParams, bool> updateNode)
        where TNode : RenderNode
    {
        bool wasFaulted = BeginRecordingOperation();
        ArgumentNullException.ThrowIfNull(createNode);
        ArgumentNullException.ThrowIfNull(updateNode);

        TNode? next = Next<TNode>();

        try
        {
            if (next == null)
            {
                TNode node = createNode(parameters);
                Add(node);
            }
            else
            {
                _hasChanges |= updateNode(next, parameters);
            }
        }
        catch
        {
            _faulted = true;
            throw;
        }

        ++_drawOperationindex;
        CompleteRecordingOperation(wasFaulted);
    }

    public void DrawBackdrop(IBackdrop backdrop)
    {
        bool wasFaulted = BeginRecordingOperation();
        ArgumentNullException.ThrowIfNull(backdrop);

        DrawBackdropRenderNode? next = Next<DrawBackdropRenderNode>();

        var b = new Rect(canvasSize);
        if (next == null)
        {
            Add(new DrawBackdropRenderNode(backdrop, b));
        }
        else
        {
            _hasChanges |= next.Update(backdrop, b);
        }

        ++_drawOperationindex;
        CompleteRecordingOperation(wasFaulted);
    }

    public IBackdrop Snapshot()
    {
        bool wasFaulted = BeginRecordingOperation();
        SnapshotBackdropRenderNode? next = Next<SnapshotBackdropRenderNode>();

        if (next == null)
        {
            Add(next = new SnapshotBackdropRenderNode());
        }

        ++_drawOperationindex;
        CompleteRecordingOperation(wasFaulted);
        return next;
    }

    public void Pop(int count = -1)
    {
        if (count < 0)
        {
            while (count < 0
                   && _nodes.TryPop(out (ContainerRenderNode, int, bool) state))
            {
                CloseScope(state);
                count++;
            }
        }
        else
        {
            while (_nodes.Count >= count
                   && _nodes.TryPop(out (ContainerRenderNode, int, bool) state))
            {
                CloseScope(state);
            }
        }
    }

    public PushedState Push()
    {
        bool wasFaulted = BeginRecordingOperation();
        PushRenderNode? next = Next<PushRenderNode>();

        if (next == null)
        {
            AddAndPush(new PushRenderNode());
        }
        else
        {
            Push(next);
        }

        var result = new PushedState(this, _nodes.Count);
        CompleteRecordingOperation(wasFaulted);
        return result;
    }

    public PushedState PushLayer(Rect limit = default)
    {
        bool wasFaulted = BeginRecordingOperation();
        LayerRenderNode? next = Next<LayerRenderNode>();

        if (next == null)
        {
            AddAndPush(new LayerRenderNode(limit));
        }
        else
        {
            _hasChanges |= next.Update(limit);
            Push(next);
        }

        var result = new PushedState(this, _nodes.Count);
        CompleteRecordingOperation(wasFaulted);
        return result;
    }

    public PushedState PushBlendMode(BlendMode blendMode)
    {
        bool wasFaulted = BeginRecordingOperation();
        BlendModeRenderNode? next = Next<BlendModeRenderNode>();

        if (next == null)
        {
            AddAndPush(new BlendModeRenderNode(blendMode));
        }
        else
        {
            _hasChanges |= next.Update(blendMode);
            Push(next);
        }

        var result = new PushedState(this, _nodes.Count);
        CompleteRecordingOperation(wasFaulted);
        return result;
    }

    public PushedState PushClip(Rect clip, ClipOperation operation = ClipOperation.Intersect)
    {
        bool wasFaulted = BeginRecordingOperation();
        RectClipRenderNode? next = Next<RectClipRenderNode>();

        if (next == null)
        {
            AddAndPush(new RectClipRenderNode(clip, operation));
        }
        else
        {
            _hasChanges |= next.Update(clip, operation);
            Push(next);
        }

        var result = new PushedState(this, _nodes.Count);
        CompleteRecordingOperation(wasFaulted);
        return result;
    }

    public PushedState PushClip(Geometry.Resource geometry, ClipOperation operation = ClipOperation.Intersect)
    {
        bool wasFaulted = BeginRecordingOperation();
        ArgumentNullException.ThrowIfNull(geometry);
        ObjectDisposedException.ThrowIf(geometry.IsDisposed, geometry);

        GeometryClipRenderNode? next = Next<GeometryClipRenderNode>();

        if (next == null)
        {
            AddAndPush(new GeometryClipRenderNode(geometry, operation));
        }
        else
        {
            _hasChanges |= next.Update(geometry, operation);
            Push(next);
        }

        var result = new PushedState(this, _nodes.Count);
        CompleteRecordingOperation(wasFaulted);
        return result;
    }

    public PushedState PushOpacity(float opacity)
    {
        bool wasFaulted = BeginRecordingOperation();
        OpacityRenderNode? next = Next<OpacityRenderNode>();

        if (next == null)
        {
            AddAndPush(new OpacityRenderNode(opacity));
        }
        else
        {
            _hasChanges |= next.Update(opacity);
            Push(next);
        }

        var result = new PushedState(this, _nodes.Count);
        CompleteRecordingOperation(wasFaulted);
        return result;
    }

    public PushedState PushFilterEffect(FilterEffect.Resource effect)
    {
        bool wasFaulted = BeginRecordingOperation();
        ArgumentNullException.ThrowIfNull(effect);
        ObjectDisposedException.ThrowIf(effect.IsDisposed, effect);

        PushedState result;
        switch (effect)
        {
            case FilterEffectGroup.Resource group:
                for (int i = group.Children.Count - 1; i >= 0; i--)
                {
                    FilterEffect.Resource item = group.Children[i];
                    PushFilterEffect(item);
                }

                result = new PushedState(this, _nodes.Count);
                break;
            default:
                result = effect.Push(this);
                break;
        }

        CompleteRecordingOperation(wasFaulted);
        return result;
    }

    public PushedState PushOpacityMask(Brush.Resource mask, Rect bounds, bool invert = false)
    {
        bool wasFaulted = BeginRecordingOperation();
        ArgumentNullException.ThrowIfNull(mask);
        ObjectDisposedException.ThrowIf(mask.IsDisposed, mask);

        OpacityMaskRenderNode? next = Next<OpacityMaskRenderNode>();

        if (next == null)
        {
            AddAndPush(new OpacityMaskRenderNode(mask, bounds, invert));
        }
        else
        {
            _hasChanges |= next.Update(mask, bounds, invert);
            Push(next);
        }

        var result = new PushedState(this, _nodes.Count);
        CompleteRecordingOperation(wasFaulted);
        return result;
    }

    public PushedState PushTransform(Matrix matrix, TransformOperator transformOperator = TransformOperator.Prepend)
    {
        bool wasFaulted = BeginRecordingOperation();
        TransformRenderNode? next = Next<TransformRenderNode>();

        if (next == null)
        {
            AddAndPush(new TransformRenderNode(matrix, transformOperator));
        }
        else
        {
            _hasChanges |= next.Update(matrix, transformOperator);
            Push(next);
        }

        var result = new PushedState(this, _nodes.Count);
        CompleteRecordingOperation(wasFaulted);
        return result;
    }

    public PushedState PushTransform(Transform.Resource transform,
        TransformOperator transformOperator = TransformOperator.Prepend)
    {
        bool wasFaulted = BeginRecordingOperation();
        ArgumentNullException.ThrowIfNull(transform);
        ObjectDisposedException.ThrowIf(transform.IsDisposed, transform);

        TransformRenderNode? next = Next<TransformRenderNode>();
        var matrix = transform.Matrix;
        if (next == null)
        {
            AddAndPush(new TransformRenderNode(matrix, transformOperator));
        }
        else
        {
            _hasChanges |= next.Update(matrix, transformOperator);
            Push(next);
        }

        var result = new PushedState(this, _nodes.Count);
        CompleteRecordingOperation(wasFaulted);
        return result;
    }

    public PushedState PushNode<TNode, TParams>(in TParams parameters, Func<TParams, TNode> createNode,
        Func<TNode, TParams, bool> updateNode)
        where TNode : ContainerRenderNode
    {
        bool wasFaulted = BeginRecordingOperation();
        ArgumentNullException.ThrowIfNull(createNode);
        ArgumentNullException.ThrowIfNull(updateNode);

        TNode? next = Next<TNode>();

        if (next == null)
        {
            TNode node = createNode(parameters);
            AddAndPush(node);
        }
        else
        {
            _hasChanges |= updateNode(next, parameters);
            Push(next);
        }

        var result = new PushedState(this, _nodes.Count);
        CompleteRecordingOperation(wasFaulted);
        return result;
    }
}
