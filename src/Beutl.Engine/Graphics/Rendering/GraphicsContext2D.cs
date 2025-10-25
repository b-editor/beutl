using Beutl.Animation;
using Beutl.Engine;
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
    //
    // public T Get<T>(IProperty<T> property)
    // {
    //     return property.GetValue(Clock);
    // }
    //
    // public (T1, T2) Get<T1, T2>(IProperty<T1> property1, IProperty<T2> property2)
    // {
    //     return (property1.GetValue(Clock), property2.GetValue(Clock));
    // }
    //
    // public (T1, T2, T3) Get<T1, T2, T3>(IProperty<T1> property1, IProperty<T2> property2, IProperty<T3> property3)
    // {
    //     return (property1.GetValue(Clock), property2.GetValue(Clock), property3.GetValue(Clock));
    // }
    //
    // public (T1, T2, T3, T4) Get<T1, T2, T3, T4>(IProperty<T1> property1, IProperty<T2> property2,
    //     IProperty<T3> property3, IProperty<T4> property4)
    // {
    //     return (property1.GetValue(Clock), property2.GetValue(Clock), property3.GetValue(Clock),
    //         property4.GetValue(Clock));
    // }

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

    private void AddAndPush(ContainerRenderNode node, ContainerRenderNode? old)
    {
        if (old != null)
        {
            node.BringFrom(old);
        }

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
        Rational rate = source.FrameRate;
        double frameNum = frame.TotalSeconds * (rate.Numerator / (double)rate.Denominator);
        DrawVideoSource(source, (int)frameNum, fill, pen);
    }

    public void DrawVideoSource(IVideoSource source, int frame, Brush.Resource? fill, Pen.Resource? pen)
    {
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
        DrawableRenderNode? next = Next<DrawableRenderNode>();

        if (next == null)
        {
            AddAndPush(new DrawableRenderNode(drawable), next);
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
        RenderNode? next = Next();

        if (next == null || !node.Equals(next))
        {
            Add(node);
        }

        ++_drawOperationindex;
    }

    public void DrawBackdrop(IBackdrop backdrop)
    {
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
            AddAndPush(new PushRenderNode(), next);
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
            AddAndPush(new LayerRenderNode(limit), next);
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
            AddAndPush(new BlendModeRenderNode(blendMode), next);
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
            AddAndPush(new RectClipRenderNode(clip, operation), next);
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
        GeometryClipRenderNode? next = Next<GeometryClipRenderNode>();

        if (next == null)
        {
            AddAndPush(new GeometryClipRenderNode(geometry, operation), next);
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
            AddAndPush(new OpacityRenderNode(opacity), next);
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
                        AddAndPush(new FilterEffectRenderNode(effect), next);
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
        OpacityMaskRenderNode? next = Next<OpacityMaskRenderNode>();

        if (next == null)
        {
            AddAndPush(new OpacityMaskRenderNode(mask, bounds, invert), next);
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
            AddAndPush(new TransformRenderNode(matrix, transformOperator), next);
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
        TransformRenderNode? next = Next<TransformRenderNode>();
        var matrix = transform.Matrix;
        if (next == null)
        {
            AddAndPush(new TransformRenderNode(matrix, transformOperator), next);
        }
        else
        {
            _hasChanges = next.Update(matrix, transformOperator);
            Push(next);
        }

        return new(this, _nodes.Count);
    }

    public PushedState PushBoundaryTransform(Transform.Resource? transform,
        RelativePoint transformOrigin,
        Size screenSize,
        AlignmentX alignmentX,
        AlignmentY alignmentY)
    {
        BoundaryTransformRenderNode? next = Next<BoundaryTransformRenderNode>();

        if (next == null)
        {
            AddAndPush(new BoundaryTransformRenderNode(
                transform, transformOrigin, screenSize, alignmentX, alignmentY, false), next);
        }
        else
        {
            _hasChanges = next.Update(transform, transformOrigin, screenSize, alignmentX, alignmentY, true);
            Push(next);
        }

        return new(this, _nodes.Count);
    }

    public PushedState PushSplittedTransform(Transform.Resource? transform,
        RelativePoint transformOrigin,
        Size screenSize,
        AlignmentX alignmentX,
        AlignmentY alignmentY)
    {
        BoundaryTransformRenderNode? next = Next<BoundaryTransformRenderNode>();

        if (next == null)
        {
            AddAndPush(new BoundaryTransformRenderNode(
                transform, transformOrigin, screenSize, alignmentX, alignmentY, true), next);
        }
        else
        {
            _hasChanges = next.Update(transform, transformOrigin, screenSize, alignmentX, alignmentY, true);
            Push(next);
        }

        return new(this, _nodes.Count);
    }
}
