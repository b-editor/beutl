using Beutl.Graphics.Effects;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Media.Source;
using Beutl.Media.TextFormatting;
using Beutl.Rendering;

namespace Beutl.Graphics.Rendering;

public sealed class DeferradCanvas : ICanvas
{
    private readonly Stack<(ContainerNode, int)> _nodes;
    private ContainerNode _container;
    private int _drawOperationindex;

    public DeferradCanvas(ContainerNode container, PixelSize canvasSize = default)
    {
        _container = container;
        _nodes = new Stack<(ContainerNode, int)>();
        Size = canvasSize;
    }

    public PixelSize Size { get; }

    public bool IsDisposed { get; }

    public BlendMode BlendMode { get; } = BlendMode.SrcOver;

    public Matrix Transform { get; } = Matrix.Identity;

    private void Add(IGraphicNode node)
    {
        if (_drawOperationindex < _container.Children.Count)
        {
            _container.SetChild(_drawOperationindex, node);
        }
        else
        {
            _container.AddChild(node);
        }
    }

    private void AddAndPush(ContainerNode node, ContainerNode? old)
    {
        if (old != null)
        {
            node.BringFrom(old);
        }

        Add(node);
        Push(node);
    }

    private void Push(ContainerNode node)
    {
        _nodes.Push((_container, _drawOperationindex + 1));

        _drawOperationindex = 0;
        _container = node;
    }

    private T? Next<T>() where T : class, IGraphicNode
    {
        return _drawOperationindex < _container.Children.Count ? _container.Children[_drawOperationindex] as T : null;
    }

    public void Dispose()
    {
    }

    public void Reset()
    {
        _drawOperationindex = 0;
        _nodes.Clear();
    }

    public void Clear()
    {
        ClearNode? next = Next<ClearNode>();

        if (next == null || !next.Equals(default))
        {
            Add(new ClearNode(default));
        }

        ++_drawOperationindex;
    }

    public void Clear(Color color)
    {
        ClearNode? next = Next<ClearNode>();

        if (next == null || !next.Equals(color))
        {
            Add(new ClearNode(color));
        }

        ++_drawOperationindex;
    }

    public void DrawImageSource(IImageSource source, IBrush? fill, IPen? pen)
    {
        ImageSourceNode? next = Next<ImageSourceNode>();

        if (next == null || !next.Equals(source, fill, pen))
        {
            Add(new ImageSourceNode(source, ConvertBrush(fill), pen));
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
        VideoSourceNode? next = Next<VideoSourceNode>();

        if (next == null || !next.Equals(source, frame, fill, pen))
        {
            Add(new VideoSourceNode(source, frame, ConvertBrush(fill), pen));
        }

        ++_drawOperationindex;
    }

    public void DrawEllipse(Rect rect, IBrush? fill, IPen? pen)
    {
        EllipseNode? next = Next<EllipseNode>();

        if (next == null || !next.Equals(rect, fill, pen))
        {
            Add(new EllipseNode(rect, ConvertBrush(fill), pen));
        }

        ++_drawOperationindex;
    }

    public void DrawGeometry(Geometry geometry, IBrush? fill, IPen? pen)
    {
        GeometryNode? next = Next<GeometryNode>();

        if (next == null || !next.Equals(geometry, fill, pen))
        {
            Add(new GeometryNode(geometry, ConvertBrush(fill), pen));
        }

        ++_drawOperationindex;
    }

    public void DrawRectangle(Rect rect, IBrush? fill, IPen? pen)
    {
        RectangleNode? next = Next<RectangleNode>();

        if (next == null || !next.Equals(rect, fill, pen))
        {
            Add(new RectangleNode(rect, ConvertBrush(fill), pen));
        }

        ++_drawOperationindex;
    }

    public void DrawText(FormattedText text, IBrush? fill, IPen? pen)
    {
        TextNode? next = Next<TextNode>();

        if (next == null || !next.Equals(text, fill, pen))
        {
            Add(new TextNode(text, ConvertBrush(fill), pen));
        }

        ++_drawOperationindex;
    }

    public Bitmap<Bgra8888> GetBitmap()
    {
        throw new NotImplementedException();
    }

    public void Pop(int count = -1)
    {
        if (count < 0)
        {
            while (count < 0
                && _nodes.TryPop(out (ContainerNode, int) state))
            {
                _container.RemoveRange(_drawOperationindex, _container.Children.Count - _drawOperationindex);

                _container = state.Item1;
                _drawOperationindex = state.Item2;

                count++;
            }
        }
        else
        {
            while (_nodes.Count >= count
                && _nodes.TryPop(out (ContainerNode, int) state))
            {
                _container.RemoveRange(_drawOperationindex, _container.Children.Count - _drawOperationindex);

                _container = state.Item1;
                _drawOperationindex = state.Item2;
            }
        }
    }

    public PushedState Push()
    {
        PushNode? next = Next<PushNode>();

        if (next == null)
        {
            AddAndPush(new PushNode(), next);
        }
        else
        {
            Push(next);
        }

        return new(this, _nodes.Count);
    }

    public PushedState PushBlendMode(BlendMode blendMode)
    {
        BlendModeNode? next = Next<BlendModeNode>();

        if (next == null || !next.Equals(blendMode))
        {
            AddAndPush(new BlendModeNode(blendMode), next);
        }
        else
        {
            Push(next);
        }

        return new(this, _nodes.Count);
    }

    public PushedState PushClip(Rect clip, ClipOperation operation = ClipOperation.Intersect)
    {
        RectClipNode? next = Next<RectClipNode>();

        if (next == null || !next.Equals(clip, operation))
        {
            AddAndPush(new RectClipNode(clip, operation), next);
        }
        else
        {
            Push(next);
        }

        return new(this, _nodes.Count);
    }

    public PushedState PushClip(Geometry geometry, ClipOperation operation = ClipOperation.Intersect)
    {
        GeometryClipNode? next = Next<GeometryClipNode>();

        if (next == null || !next.Equals(geometry, operation))
        {
            AddAndPush(new GeometryClipNode(geometry, operation), next);
        }
        else
        {
            Push(next);
        }

        return new(this, _nodes.Count);
    }

    public PushedState PushFilterEffect(FilterEffect effect)
    {
        FilterEffectNode? next = Next<FilterEffectNode>();

        if (next == null || !next.Equals(effect))
        {
            AddAndPush(new FilterEffectNode(effect), next);
        }
        else
        {
            Push(next);
        }

        return new(this, _nodes.Count);
    }

    public PushedState PushOpacityMask(IBrush mask, Rect bounds, bool invert = false)
    {
        OpacityMaskNode? next = Next<OpacityMaskNode>();

        if (next == null || !next.Equals(mask, bounds, invert))
        {
            AddAndPush(new OpacityMaskNode(mask, bounds, invert), next);
        }
        else
        {
            Push(next);
        }

        return new(this, _nodes.Count);
    }

    public PushedState PushTransform(Matrix matrix, TransformOperator transformOperator = TransformOperator.Prepend)
    {
        TransformNode? next = Next<TransformNode>();

        if (next == null || !next.Equals(matrix, transformOperator))
        {
            AddAndPush(new TransformNode(matrix, transformOperator), next);
        }
        else
        {
            Push(next);
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
                scene[0].UpdateAll(new[] { drawable });
            }

            return new RenderSceneBrush(drawableBrush, scene, bounds);
        }
        else
        {
            return brush;
        }
    }
}
