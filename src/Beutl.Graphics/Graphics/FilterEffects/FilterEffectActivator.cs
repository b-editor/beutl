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
            SKSurface surface = _canvas.CreateRenderTarget((int)_bounds.Width, (int)_bounds.Height);

            using ImmediateCanvas canvas = _canvas.CreateCanvas(surface, true);
            using var paint = new SKPaint
            {
                ImageFilter = _builder.GetFilter(),
            };

            int restoreCount = surface.Canvas.SaveLayer(paint);
            using (canvas.PushTransform(Matrix.CreateTranslation(-_bounds.X, -_bounds.Y)))
            {
                _target.Draw(canvas);
            }

            surface.Canvas.RestoreToCount(restoreCount);

            _target?.Dispose();
            _target = new EffectTarget(Ref<SKSurface>.Create(surface), _bounds.Size);
            _builder.Clear();

            _originalBounds = _bounds;
        }
    }

    public void Apply(FilterEffectContext context)
    {
        foreach (IFEItem item in context._items)
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
