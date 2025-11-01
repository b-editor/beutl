using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Media.TextFormatting;

namespace Beutl.Graphics.Rendering;

public sealed class GraphicsContext2D(
    ContainerRenderNode container,
    PixelSize canvasSize = default)
    : IDisposable, IPopable
{
    private readonly Stack<(ContainerRenderNode, int)> _nodes = [];
    private int _drawOperationindex;

    private ContainerRenderNode _container = container;

    // 下位のノードで変更があったとき、上位に伝搬するためのフィールド。Pop時に上位ノードのHasChangesを変更する用。
    private bool _hasChanges;

    public PixelSize Size => canvasSize;

    internal Action<RenderNode>? OnUntracked { get; set; }

    private void Untracked(RenderNode? node)
    {
        if (node != null) OnUntracked?.Invoke(node);
    }

    private void Add(RenderNode node)
    {
        if (_drawOperationindex < _container.Children.Count)
        {
            Untracked(_container.Children[_drawOperationindex]);
            _container.SetChild(_drawOperationindex, node);
        }
        else
        {
            _container.AddChild(node);
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
        _nodes.Push((_container, _drawOperationindex + 1));

        _drawOperationindex = 0;
        _container = node;
    }

    private T? Next<T>() where T : RenderNode
    {
        return _drawOperationindex < _container.Children.Count ? _container.Children[_drawOperationindex] as T : null;
    }

    private RenderNode? Next()
    {
        return _drawOperationindex < _container.Children.Count ? _container.Children[_drawOperationindex] : null;
    }

    public void Dispose()
    {
        _container.RemoveRange(_drawOperationindex, _container.Children.Count - _drawOperationindex);
    }

    public void Reset()
    {
        _drawOperationindex = 0;
        _nodes.Clear();
    }

    public MemoryNode<T> UseMemory<T>(T defaultValue)
    {
        MemoryNode<T>? next = Next<MemoryNode<T>>();

        if (next == null)
        {
            next =  new MemoryNode<T>(defaultValue);
            Add(next);
        }

        ++_drawOperationindex;
        return next;
    }

    public MemoryNode<T?> UseMemory<T>()
    {
        return UseMemory<T?>(default);
    }

    public void Clear()
    {
        ClearRenderNode? next = Next<ClearRenderNode>();

        if (next == null || !next.Equals(default))
        {
            Add(new ClearRenderNode(default));
        }

        ++_drawOperationindex;
    }

    public void Clear(Color color)
    {
        ClearRenderNode? next = Next<ClearRenderNode>();

        if (next == null || !next.Equals(color))
        {
            Add(new ClearRenderNode(color));
        }

        ++_drawOperationindex;
    }

    public void DrawImageSource(IImageSource source, Brush.Resource? fill, Pen.Resource? pen)
    {
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
            _hasChanges = next.Update(source, fill, pen);
        }

        ++_drawOperationindex;
    }

    public void DrawVideoSource(IVideoSource source, TimeSpan frame, Brush.Resource? fill, Pen.Resource? pen)
    {
        if (fill != null) ObjectDisposedException.ThrowIf(fill.IsDisposed, fill);
        if (pen != null) ObjectDisposedException.ThrowIf(pen.IsDisposed, pen);
        ArgumentNullException.ThrowIfNull(source);
        ObjectDisposedException.ThrowIf(source.IsDisposed, source);

        Rational rate = source.FrameRate;
        double frameNum = frame.TotalSeconds * (rate.Numerator / (double)rate.Denominator);
        DrawVideoSource(source, (int)frameNum, fill, pen);
    }

    public void DrawVideoSource(IVideoSource source, int frame, Brush.Resource? fill, Pen.Resource? pen)
    {
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
            _hasChanges = next.Update(source, frame, fill, pen);
        }

        ++_drawOperationindex;
    }

    public void DrawEllipse(Rect rect, Brush.Resource? fill, Pen.Resource? pen)
    {
        if (fill != null) ObjectDisposedException.ThrowIf(fill.IsDisposed, fill);
        if (pen != null) ObjectDisposedException.ThrowIf(pen.IsDisposed, pen);

        EllipseRenderNode? next = Next<EllipseRenderNode>();

        if (next == null)
        {
            Add(new EllipseRenderNode(rect, fill, pen));
        }
        else
        {
            _hasChanges = next.Update(rect, fill, pen);
        }

        ++_drawOperationindex;
    }

    public void DrawGeometry(Geometry.Resource geometry, Brush.Resource? fill, Pen.Resource? pen)
    {
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
            _hasChanges = next.Update(geometry, fill, pen);
        }

        ++_drawOperationindex;
    }

    public void DrawRectangle(Rect rect, Brush.Resource? fill, Pen.Resource? pen)
    {
        if (fill != null) ObjectDisposedException.ThrowIf(fill.IsDisposed, fill);
        if (pen != null) ObjectDisposedException.ThrowIf(pen.IsDisposed, pen);

        RectangleRenderNode? next = Next<RectangleRenderNode>();

        if (next == null)
        {
            Add(new RectangleRenderNode(rect, fill, pen));
        }
        else
        {
            _hasChanges = next.Update(rect, fill, pen);
        }

        ++_drawOperationindex;
    }

    public void DrawText(FormattedText text, Brush.Resource? fill, Pen.Resource? pen)
    {
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
            _hasChanges = next.Update(text, fill, pen);
        }

        ++_drawOperationindex;
    }

    public void DrawDrawable(Drawable.Resource drawable)
    {
        ArgumentNullException.ThrowIfNull(drawable);
        ObjectDisposedException.ThrowIf(drawable.IsDisposed, drawable);

        DrawableRenderNode? next = Next<DrawableRenderNode>();

        if (next == null)
        {
            AddAndPush(new DrawableRenderNode(drawable));
        }
        else
        {
            _hasChanges = next.Update(drawable);
            Push(next);
        }

        int count = _nodes.Count;
        try
        {
            var obj = drawable.GetOriginal();
            obj.Render(this, drawable);
        }
        finally
        {
            Pop(count);
        }
    }

    public void DrawNode(RenderNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        ObjectDisposedException.ThrowIf(node.IsDisposed, node);

        RenderNode? next = Next();

        if (next == null || !node.Equals(next))
        {
            Add(node);
        }

        ++_drawOperationindex;
    }

    public void DrawNode<TNode, TParams>(in TParams parameters, Func<TParams, TNode> createNode, Func<TNode, TParams, bool> updateNode)
        where TNode : RenderNode
    {
        ArgumentNullException.ThrowIfNull(createNode);
        ArgumentNullException.ThrowIfNull(updateNode);

        TNode? next = Next<TNode>();

        if (next == null)
        {
            TNode node = createNode(parameters);
            Add(node);
        }
        else
        {
            _hasChanges = updateNode(next, parameters);
        }

        ++_drawOperationindex;
    }

    public void DrawBackdrop(IBackdrop backdrop)
    {
        ArgumentNullException.ThrowIfNull(backdrop);

        DrawBackdropRenderNode? next = Next<DrawBackdropRenderNode>();

        var b = new Rect(canvasSize.ToSize(1));
        if (next == null)
        {
            Add(new DrawBackdropRenderNode(backdrop, b));
        }
        else
        {
            _hasChanges = next.Update(backdrop, b);
        }

        ++_drawOperationindex;
    }

    public IBackdrop Snapshot()
    {
        SnapshotBackdropRenderNode? next = Next<SnapshotBackdropRenderNode>();

        if (next == null)
        {
            Add(next = new SnapshotBackdropRenderNode());
        }

        ++_drawOperationindex;
        return next;
    }

    public void Pop(int count = -1)
    {
        if (count < 0)
        {
            while (count < 0
                   && _nodes.TryPop(out (ContainerRenderNode, int) state))
            {
                foreach (RenderNode node in _container.Children.Take(_drawOperationindex..))
                {
                    _hasChanges = true;
                    node.Dispose();
                    Untracked(node);
                }

                _container.RemoveRange(_drawOperationindex, _container.Children.Count - _drawOperationindex);

                _container = state.Item1;
                _container.HasChanges = _container.HasChanges || _hasChanges;
                _drawOperationindex = state.Item2;

                count++;
            }
        }
        else
        {
            while (_nodes.Count >= count
                   && _nodes.TryPop(out (ContainerRenderNode, int) state))
            {
                foreach (RenderNode node in _container.Children.Take(_drawOperationindex..))
                {
                    _hasChanges = true;
                    node.Dispose();
                    Untracked(node);
                }

                _container.RemoveRange(_drawOperationindex, _container.Children.Count - _drawOperationindex);

                _container = state.Item1;
                _container.HasChanges = _container.HasChanges || _hasChanges;
                _drawOperationindex = state.Item2;
            }
        }
    }

    public PushedState Push()
    {
        PushRenderNode? next = Next<PushRenderNode>();

        if (next == null)
        {
            AddAndPush(new PushRenderNode());
        }
        else
        {
            Push(next);
        }

        return new(this, _nodes.Count);
    }

    public PushedState PushLayer(Rect limit = default)
    {
        LayerRenderNode? next = Next<LayerRenderNode>();

        if (next == null)
        {
            AddAndPush(new LayerRenderNode(limit));
        }
        else
        {
            _hasChanges = next.Update(limit);
            Push(next);
        }

        return new(this, _nodes.Count);
    }

    public PushedState PushBlendMode(BlendMode blendMode)
    {
        BlendModeRenderNode? next = Next<BlendModeRenderNode>();

        if (next == null)
        {
            AddAndPush(new BlendModeRenderNode(blendMode));
        }
        else
        {
            _hasChanges = next.Update(blendMode);
            Push(next);
        }

        return new(this, _nodes.Count);
    }

    public PushedState PushClip(Rect clip, ClipOperation operation = ClipOperation.Intersect)
    {
        RectClipRenderNode? next = Next<RectClipRenderNode>();

        if (next == null)
        {
            AddAndPush(new RectClipRenderNode(clip, operation));
        }
        else
        {
            _hasChanges = next.Update(clip, operation);
            Push(next);
        }

        return new(this, _nodes.Count);
    }

    public PushedState PushClip(Geometry.Resource geometry, ClipOperation operation = ClipOperation.Intersect)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ObjectDisposedException.ThrowIf(geometry.IsDisposed, geometry);

        GeometryClipRenderNode? next = Next<GeometryClipRenderNode>();

        if (next == null)
        {
            AddAndPush(new GeometryClipRenderNode(geometry, operation));
        }
        else
        {
            _hasChanges = next.Update(geometry, operation);
            Push(next);
        }

        return new(this, _nodes.Count);
    }

    public PushedState PushOpacity(float opacity)
    {
        OpacityRenderNode? next = Next<OpacityRenderNode>();

        if (next == null)
        {
            AddAndPush(new OpacityRenderNode(opacity));
        }
        else
        {
            _hasChanges = next.Update(opacity);
            Push(next);
        }

        return new(this, _nodes.Count);
    }

    public PushedState PushFilterEffect(FilterEffect.Resource effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ObjectDisposedException.ThrowIf(effect.IsDisposed, effect);

        switch (effect)
        {
            case FilterEffectGroup.Resource group:
                {
                    for (int i = group.Children.Count - 1; i >= 0; i--)
                    {
                        FilterEffect.Resource item = group.Children[i];
                        PushFilterEffect(item);
                    }

                    break;
                }
            default:
                {
                    FilterEffectRenderNode? next = Next<FilterEffectRenderNode>();

                    if (next == null)
                    {
                        AddAndPush(new FilterEffectRenderNode(effect));
                    }
                    else
                    {
                        _hasChanges = next.Update(effect);
                        Push(next);
                    }

                    break;
                }
        }

        return new(this, _nodes.Count);
    }

    public PushedState PushOpacityMask(Brush.Resource mask, Rect bounds, bool invert = false)
    {
        ArgumentNullException.ThrowIfNull(mask);
        ObjectDisposedException.ThrowIf(mask.IsDisposed, mask);

        OpacityMaskRenderNode? next = Next<OpacityMaskRenderNode>();

        if (next == null)
        {
            AddAndPush(new OpacityMaskRenderNode(mask, bounds, invert));
        }
        else
        {
            _hasChanges = next.Update(mask, bounds, invert);
            Push(next);
        }

        return new(this, _nodes.Count);
    }

    public PushedState PushTransform(Matrix matrix, TransformOperator transformOperator = TransformOperator.Prepend)
    {
        TransformRenderNode? next = Next<TransformRenderNode>();

        if (next == null)
        {
            AddAndPush(new TransformRenderNode(matrix, transformOperator));
        }
        else
        {
            _hasChanges = next.Update(matrix, transformOperator);
            Push(next);
        }

        return new(this, _nodes.Count);
    }

    public PushedState PushTransform(Transform.Resource transform,
        TransformOperator transformOperator = TransformOperator.Prepend)
    {
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
            _hasChanges = next.Update(matrix, transformOperator);
            Push(next);
        }

        return new(this, _nodes.Count);
    }

    public PushedState PushNode<TNode, TParams>(in TParams parameters, Func<TParams, TNode> createNode, Func<TNode, TParams, bool> updateNode)
        where TNode : ContainerRenderNode
    {
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
            _hasChanges = updateNode(next, parameters);
            Push(next);
        }

        return new(this, _nodes.Count);
    }
}
