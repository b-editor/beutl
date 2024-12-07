using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Media.TextFormatting;

namespace Beutl.Graphics.Rendering.V2;

public sealed class GraphicsContext2D(ContainerRenderNode container, PixelSize canvasSize = default)
    : IDisposable, IPopable
{
    private readonly Stack<(ContainerRenderNode, int)> _nodes = [];
    private int _drawOperationindex;
    private ContainerRenderNode _container = container;

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

    public void DrawImageSource(IImageSource source, IBrush? fill, IPen? pen)
    {
        ImageSourceRenderNode? next = Next<ImageSourceRenderNode>();

        if (next == null || !next.Equals(source, fill, pen))
        {
            Add(new ImageSourceRenderNode(source, ConvertBrush(fill), pen));
        }

        ++_drawOperationindex;
    }

    public void DrawVideoSource(IVideoSource source, TimeSpan frame, IBrush? fill, IPen? pen)
    {
        Rational rate = source.FrameRate;
        double frameNum = frame.TotalSeconds * (rate.Numerator / (double)rate.Denominator);
        DrawVideoSource(source, (int)frameNum, fill, pen);
    }

    public void DrawVideoSource(IVideoSource source, int frame, IBrush? fill, IPen? pen)
    {
        VideoSourceRenderNode? next = Next<VideoSourceRenderNode>();

        if (next == null || !next.Equals(source, frame, fill, pen))
        {
            Add(new VideoSourceRenderNode(source, frame, ConvertBrush(fill), pen));
        }

        ++_drawOperationindex;
    }

    public void DrawEllipse(Rect rect, IBrush? fill, IPen? pen)
    {
        EllipseRenderNode? next = Next<EllipseRenderNode>();

        if (next == null || !next.Equals(rect, fill, pen))
        {
            Add(new EllipseRenderNode(rect, ConvertBrush(fill), pen));
        }

        ++_drawOperationindex;
    }

    public void DrawGeometry(Geometry geometry, IBrush? fill, IPen? pen)
    {
        GeometryRenderNode? next = Next<GeometryRenderNode>();

        if (next == null || !next.Equals(geometry, fill, pen))
        {
            Add(new GeometryRenderNode(geometry, ConvertBrush(fill), pen));
        }

        ++_drawOperationindex;
    }

    public void DrawRectangle(Rect rect, IBrush? fill, IPen? pen)
    {
        RectangleRenderNode? next = Next<RectangleRenderNode>();

        if (next == null || !next.Equals(rect, fill, pen))
        {
            Add(new RectangleRenderNode(rect, ConvertBrush(fill), pen));
        }

        ++_drawOperationindex;
    }

    public void DrawText(FormattedText text, IBrush? fill, IPen? pen)
    {
        TextRenderNode? next = Next<TextRenderNode>();

        if (next == null || !next.Equals(text, fill, pen))
        {
            Add(new TextRenderNode(text, ConvertBrush(fill), pen));
        }

        ++_drawOperationindex;
    }

    public void DrawDrawable(Drawable drawable)
    {
        DrawableRenderNode? next = Next<DrawableRenderNode>();

        if (next == null || !ReferenceEquals(next.Drawable, drawable))
        {
            AddAndPush(new DrawableRenderNode(drawable), next);
        }
        else
        {
            Push(next);
        }

        int count = _nodes.Count;
        try
        {
            drawable.Render(this);
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
        if (next == null || !next.Equals(backdrop, b))
        {
            Add(new DrawBackdropRenderNode(backdrop, b));
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
                    node.Dispose();
                    Untracked(node);
                }

                _container.RemoveRange(_drawOperationindex, _container.Children.Count - _drawOperationindex);

                _container = state.Item1;
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
                    node.Dispose();
                    Untracked(node);
                }

                _container.RemoveRange(_drawOperationindex, _container.Children.Count - _drawOperationindex);

                _container = state.Item1;
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

        if (next == null || next.Limit != limit)
        {
            AddAndPush(new LayerRenderNode(limit), next);
        }
        else
        {
            Push(next);
        }

        return new(this, _nodes.Count);
    }

    public PushedState PushBlendMode(BlendMode blendMode)
    {
        BlendModeRenderNode? next = Next<BlendModeRenderNode>();

        if (next == null || !next.Equals(blendMode))
        {
            AddAndPush(new BlendModeRenderNode(blendMode), next);
        }
        else
        {
            Push(next);
        }

        return new(this, _nodes.Count);
    }

    public PushedState PushClip(Rect clip, ClipOperation operation = ClipOperation.Intersect)
    {
        RectClipRenderNode? next = Next<RectClipRenderNode>();

        if (next == null || !next.Equals(clip, operation))
        {
            AddAndPush(new RectClipRenderNode(clip, operation), next);
        }
        else
        {
            Push(next);
        }

        return new(this, _nodes.Count);
    }

    public PushedState PushClip(Geometry geometry, ClipOperation operation = ClipOperation.Intersect)
    {
        GeometryClipRenderNode? next = Next<GeometryClipRenderNode>();

        if (next == null || !next.Equals(geometry, operation))
        {
            AddAndPush(new GeometryClipRenderNode(geometry, operation), next);
        }
        else
        {
            Push(next);
        }

        return new(this, _nodes.Count);
    }

    public PushedState PushOpacity(float opacity)
    {
        OpacityRenderNode? next = Next<OpacityRenderNode>();

        if (next == null || !next.Equals(opacity))
        {
            AddAndPush(new OpacityRenderNode(opacity), next);
        }
        else
        {
            Push(next);
        }

        return new(this, _nodes.Count);
    }

    public PushedState PushFilterEffect(FilterEffect effect)
    {
        switch (effect)
        {
            case FilterEffectGroup group:
                {
                    for (int i = group.Children.Count - 1; i >= 0; i--)
                    {
                        FilterEffect item = group.Children[i];
                        PushFilterEffect(item);
                    }

                    break;
                }
#pragma warning disable CS0618
            case CombinedFilterEffect combined:
#pragma warning restore CS0618
                {
                    if (combined.Second != null)
                        PushFilterEffect(combined.Second);

                    if (combined.First != null)
                        PushFilterEffect(combined.First);

                    break;
                }
            default:
                {
                    FilterEffectRenderNode? next = Next<FilterEffectRenderNode>();

                    if (next == null || !next.Equals(effect))
                    {
                        AddAndPush(new FilterEffectRenderNode(effect), next);
                    }
                    else
                    {
                        Push(next);
                    }

                    break;
                }
        }

        return new(this, _nodes.Count);
    }

    public PushedState PushOpacityMask(IBrush mask, Rect bounds, bool invert = false)
    {
        OpacityMaskRenderNode? next = Next<OpacityMaskRenderNode>();

        if (next == null || !next.Equals(mask, bounds, invert))
        {
            AddAndPush(new OpacityMaskRenderNode(mask, bounds, invert), next);
        }
        else
        {
            Push(next);
        }

        return new(this, _nodes.Count);
    }

    public PushedState PushTransform(Matrix matrix, TransformOperator transformOperator = TransformOperator.Prepend)
    {
        TransformRenderNode? next = Next<TransformRenderNode>();

        if (next == null || !next.Equals(matrix, transformOperator))
        {
            AddAndPush(new TransformRenderNode(matrix, transformOperator), next);
        }
        else
        {
            Push(next);
        }

        return new(this, _nodes.Count);
    }

    public PushedState PushTransform(ITransform transform,
        TransformOperator transformOperator = TransformOperator.Prepend)
    {
        switch (transform)
        {
            case TransformGroup group:
                {
                    for (int i = group.Children.Count - 1; i >= 0; i--)
                    {
                        ITransform item = group.Children[i];
                        PushTransform(item, transformOperator);
                    }

                    break;
                }
#pragma warning disable CS0618
            case MultiTransform multi:
#pragma warning restore CS0618
                {
                    if (multi.Left != null)
                        PushTransform(multi.Left, transformOperator);

                    if (multi.Right != null)
                        PushTransform(multi.Right, transformOperator);

                    break;
                }

            default:
                {
                    TransformRenderNode? next = Next<TransformRenderNode>();
                    var matrix = transform.Value;
                    if (next == null || !next.Equals(matrix, transformOperator))
                    {
                        AddAndPush(new TransformRenderNode(matrix, transformOperator), next);
                    }
                    else
                    {
                        Push(next);
                    }

                    break;
                }
        }

        return new(this, _nodes.Count);
    }

    private static IBrush? ConvertBrush(IBrush? brush)
    {
        if (brush is IDrawableBrush drawableBrush)
        {
            RenderScene? scene = null;
            Rect bounds = default;
            if (drawableBrush is { Drawable: { IsVisible: true } drawable })
            {
                drawable.Measure(Graphics.Size.Infinity);

                bounds = drawable.Bounds;
                scene = new RenderScene(bounds.Size.Ceiling());
                scene[0].UpdateAll([drawable]);
            }

            return new RenderSceneBrush(drawableBrush, scene, bounds);
        }
        else
        {
            return brush;
        }
    }
}
