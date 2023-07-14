using System.ComponentModel;

using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Media.Source;

using SkiaSharp;

namespace Beutl.Graphics.Effects;

public class FilterEffectBuilder : IDisposable
{
    private SKImageFilter? _filter;

    public void AppendSkiaFilter<T>(T data, Func<T, SKImageFilter?, SKImageFilter?> factory)
    {
        SKImageFilter? input = _filter;
        _filter = factory(data, input);
        input?.Dispose();
    }

    public bool HasFilter() => _filter != null;

    public SKImageFilter? GetFilter()
    {
        return _filter;
    }

    public void Clear()
    {
        _filter?.Dispose();
        _filter = null;
    }

    public void Dispose()
    {
        Clear();
    }
}

public sealed class EffectTarget : IDisposable
{
    public EffectTarget(FilterEffectNode node)
    {
        Node = node;
    }

    public EffectTarget(Ref<SKSurface> surface)
    {
        Surface = surface;
    }

    public FilterEffectNode? Node { get; }

    public Ref<SKSurface>? Surface { get; }

    public EffectTarget Clone()
    {
        if (Node != null)
        {
            return this;
        }
        else
        {
            return new EffectTarget(Surface!.Clone());
        }
    }

    public void Dispose()
    {
        Surface?.Dispose();
    }

    public void Draw(ImmediateCanvas canvas)
    {
        if (Node != null)
        {
            // ImageEffectContextの処理はImageEffectNode.Renderで
            // 行うので以下は循環する
            // _node.Render(canvas);
            foreach (IGraphicNode item in Node.Children)
            {
                item.Render(canvas);
            }
        }
        else if (Surface != null)
        {
            canvas._canvas.DrawSurface(Surface.Value, default);
        }
    }
}

public sealed class FilterEffectContext : IDisposable
{
    private readonly FilterEffectBuilder _builder;
    private readonly EffectTarget _initialTarget;

    private EffectTarget _target;
    private Rect _originalBounds;
    private Rect _bounds;

    public FilterEffectContext(Rect bounds, EffectTarget target, FilterEffectBuilder builder)
    {
        _bounds = _originalBounds = bounds;
        _initialTarget = target;
        _target = target;
        _builder = builder;
    }

    public Rect OriginalBounds => _originalBounds;

    public Rect Bounds => _bounds;

    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public FilterEffectBuilder Builder => _builder;

    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public EffectTarget CurrentTarget => _target;

    public void Dispose()
    {
        if (_initialTarget != _target)
        {
            _target.Dispose();
        }
    }

    //public ICanvas Open()
    //{
    //    throw new NotImplementedException();
    //}

    public Bitmap<Bgra8888> Snapshot()
    {
        Flush(true);
        if (_target.Surface != null)
        {
            return _target.Surface.Value.Snapshot().ToBitmap();
        }
        else
        {
            throw new InvalidOperationException();
        }
    }

    public void Flush(bool force = true)
    {
        if (force || _builder.HasFilter())
        {
            var surface = SKSurface.Create(
                new SKImageInfo((int)_bounds.Width, (int)_bounds.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul));

            using var canvas = new ImmediateCanvas(surface, true);
            using var paint = new SKPaint
            {
                ImageFilter = _builder.GetFilter(),
            };

            int restoreCount = surface.Canvas.SaveLayer(paint);
            using (canvas.PushTransform(Matrix.CreateTranslation(_originalBounds.X - _bounds.X, _originalBounds.Y - _bounds.Y)))
            {
                _target.Draw(canvas);
            }

            surface.Canvas.RestoreToCount(restoreCount);

            _target?.Dispose();
            _target = new EffectTarget(Ref<SKSurface>.Create(surface));
            _builder.Clear();

            _originalBounds = _bounds;
        }
    }

    public void DropShadow(Point position, Vector sigma, Color color, bool shadowOnly)
    {
        Rect shadowBounds = Bounds
            .Translate(position)
            .Inflate(new Thickness(sigma.X * 3, sigma.Y * 3));

        _bounds = shadowOnly ? shadowBounds : _bounds.Union(shadowBounds);

        if (shadowOnly)
        {
            _builder.AppendSkiaFilter((position, sigma, color), (t, input) =>
            {
                return SKImageFilter.CreateDropShadowOnly(t.position.X, t.position.Y, t.sigma.X, t.sigma.Y, t.color.ToSKColor(), input);
            });
        }
        else
        {
            _builder.AppendSkiaFilter((position, sigma, color), (t, input) =>
            {
                return SKImageFilter.CreateDropShadow(t.position.X, t.position.Y, t.sigma.X, t.sigma.Y, t.color.ToSKColor(), input);
            });
        }
    }

    public void Blur(Vector sigma)
    {
        _bounds = _bounds.Inflate(new Thickness(sigma.X * 3, sigma.Y * 3));

        _builder.AppendSkiaFilter(sigma, (t, input) =>
        {
            return SKImageFilter.CreateBlur(t.X, t.Y, input);
        });
    }

    public void DisplacementMap(SKColorChannel xChannelSelector, SKColorChannel yChannelSelector, float scale, FilterEffect displacement)
    {
        SKImageFilter? skDisplacement;
        Flush(false);
        using (EffectTarget cloned = _target.Clone())
        using (var builder = new FilterEffectBuilder())
        using (var context = new FilterEffectContext(_bounds, cloned, builder))
        {
            displacement.ApplyTo(context);

            context.Flush(false);

            skDisplacement = builder.GetFilter();
            if (skDisplacement == null && context._target.Surface != null)
            {
                SKSurface innerSurface = context._target.Surface.Value;
                using (SKImage skImage = innerSurface.Snapshot())
                {
                    skDisplacement = SKImageFilter.CreateImage(skImage);
                }
            }
        }

        _builder.AppendSkiaFilter((xChannelSelector, yChannelSelector, scale, skDisplacement), (t, input) =>
        {
            return SKImageFilter.CreateDisplacementMapEffect(t.xChannelSelector, t.yChannelSelector, t.scale, t.skDisplacement, input);
        });

        _bounds = _bounds.Inflate(scale / 2);
    }

    public void Compose(FilterEffect outer, FilterEffect inner)
    {
    }
}

