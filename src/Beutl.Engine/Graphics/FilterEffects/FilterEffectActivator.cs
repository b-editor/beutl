using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Media.Source;

using SkiaSharp;

namespace Beutl.Graphics.Effects;

public sealed class FilterEffectActivator : IDisposable
{
    private readonly SKImageFilterBuilder _builder;
    private readonly ImmediateCanvas _canvas;
    private readonly EffectTarget _initialTarget;

    private EffectTarget _target;
    private Rect _originalBounds;
    private Rect _bounds;

    public FilterEffectActivator(Rect bounds, EffectTarget target, SKImageFilterBuilder builder, ImmediateCanvas canvas)
    {
        _bounds = _originalBounds = bounds;
        _initialTarget = target;
        _target = target;
        _builder = builder;
        _canvas = canvas;
    }

    public Rect OriginalBounds => _originalBounds;

    public Rect Bounds => _bounds;

    public SKImageFilterBuilder Builder => _builder;

    public EffectTarget CurrentTarget => _target;

    public void Dispose()
    {
        if (_initialTarget != _target)
        {
            _target.Dispose();
        }
    }

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
            SKSurface surface = _canvas.CreateRenderTarget((int)_originalBounds.Width, (int)_originalBounds.Height);

            using ImmediateCanvas canvas = _canvas.CreateCanvas(surface, true);
            using var paint = new SKPaint
            {
                ImageFilter = _builder.GetFilter(),
            };

            using (canvas.PushTransform(Matrix.CreateTranslation(-_originalBounds.X, -_originalBounds.Y)))
            {
                int restoreCount = surface.Canvas.SaveLayer(paint);
                _target.Draw(canvas);
                surface.Canvas.RestoreToCount(restoreCount);
            }


            _target?.Dispose();

            using var surfaceRef = Ref<SKSurface>.Create(surface);
            _target = new EffectTarget(surfaceRef, _originalBounds.Size);
            _builder.Clear();
        }
    }

    public void Apply(FilterEffectContext context, Range range)
    {
        (int offset, int count) = range.GetOffsetAndLength(context._items.Count);
        int endAt = offset + count;

        int index = 0;
        foreach (IFEItem item in context._items.Span)
        {
            if (offset <= index && index < endAt)
            {
                if (item is IFEItem_Skia skia)
                {
                    skia.Accepts(this, _builder);
                    _bounds = item.TransformBounds(_bounds);
                    _originalBounds = item.TransformBounds(_originalBounds);
                }
                else if (item is IFEItem_Custom custom)
                {
                    Flush(true);
                    var customContext = new FilterEffectCustomOperationContext(_canvas, _target);
                    custom.Accepts(customContext);
                    if (_target != customContext.Target)
                    {
                        _target?.Dispose();
                        _target = customContext.Target;
                    }
                    _bounds = item.TransformBounds(_bounds);
                    _originalBounds = _bounds.WithX(0).WithY(0);
                }
            }

            index++;
        }
    }

    public void Apply(FilterEffectContext context)
    {
        foreach (IFEItem item in context._items.Span)
        {
            ApplyFEItem(item);
        }
    }

    private void ApplyFEItem(IFEItem item)
    {
        if (item is IFEItem_Skia skia)
        {
            skia.Accepts(this, _builder);
            _bounds = item.TransformBounds(_bounds);
        }
        else if (item is IFEItem_Custom custom)
        {
            Flush(true);
            var customContext = new FilterEffectCustomOperationContext(_canvas, _target);
            custom.Accepts(customContext);
            if (_target != customContext.Target)
            {
                _target?.Dispose();
                _target = customContext.Target;
            }
            _bounds = item.TransformBounds(_bounds);
        }
    }

    public SKImageFilter? Activate(FilterEffectContext context)
    {
        SKImageFilter? filter;
        Flush(false);
        using (EffectTarget cloned = _target.Clone())
        using (var builder = new SKImageFilterBuilder())
        using (var activator = new FilterEffectActivator(_bounds, cloned, builder, _canvas))
        {
            activator.Apply(context);

            activator.Flush(false);

            filter = builder.GetFilter();
            if (filter == null && activator._target.Surface != null)
            {
                SKSurface innerSurface = activator._target.Surface.Value;
                using (SKImage skImage = innerSurface.Snapshot())
                {
                    filter = SKImageFilter.CreateImage(skImage);
                }
            }
        }

        return filter;
    }
}
