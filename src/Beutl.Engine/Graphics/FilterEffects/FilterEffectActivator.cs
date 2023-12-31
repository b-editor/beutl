using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Media.Source;

using SkiaSharp;

namespace Beutl.Graphics.Effects;

public sealed class FilterEffectActivator(Rect bounds, EffectTarget target, SKImageFilterBuilder builder, ImmediateCanvas canvas) : IDisposable
{
    private readonly ImmediateCanvas _canvas = canvas;
    private readonly EffectTarget _initialTarget = target;

    public Rect OriginalBounds { get; private set; } = bounds;

    public Rect Bounds { get; private set; } = bounds;

    public SKImageFilterBuilder Builder { get; } = builder;

    public EffectTarget CurrentTarget { get; private set; } = target;

    public void Dispose()
    {
        if (_initialTarget != CurrentTarget)
        {
            CurrentTarget.Dispose();
        }
    }

    public Bitmap<Bgra8888> Snapshot()
    {
        Flush(true);
        if (CurrentTarget.Surface != null)
        {
            return CurrentTarget.Surface.Value.Snapshot().ToBitmap();
        }
        else
        {
            throw new InvalidOperationException();
        }
    }

    public void Flush(bool force = true)
    {
        if (force || Builder.HasFilter())
        {
            SKSurface? surface = _canvas.CreateRenderTarget((int)OriginalBounds.Width, (int)OriginalBounds.Height);

            if (surface != null)
            {
                using ImmediateCanvas canvas = _canvas.CreateCanvas(surface, true);
                using var paint = new SKPaint
                {
                    ImageFilter = Builder.GetFilter(),
                };

                using (canvas.PushTransform(Matrix.CreateTranslation(-OriginalBounds.X, -OriginalBounds.Y)))
                using (canvas.PushPaint(paint))
                {
                    CurrentTarget.Draw(canvas);
                }

                CurrentTarget?.Dispose();

                using var surfaceRef = Ref<SKSurface>.Create(surface);
                CurrentTarget = new EffectTarget(surfaceRef, OriginalBounds.Size);
            }
            else
            {
                CurrentTarget?.Dispose();

                CurrentTarget = EffectTarget.Empty;
            }

            Builder.Clear();
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
                    skia.Accepts(this, Builder);
                    Bounds = item.TransformBounds(Bounds);
                    OriginalBounds = item.TransformBounds(OriginalBounds);
                }
                else if (item is IFEItem_Custom custom)
                {
                    Flush(true);
                    var customContext = new FilterEffectCustomOperationContext(_canvas, CurrentTarget);
                    custom.Accepts(customContext);
                    if (CurrentTarget != customContext.Target)
                    {
                        CurrentTarget?.Dispose();
                        CurrentTarget = customContext.Target;
                    }
                    Bounds = item.TransformBounds(Bounds);
                    OriginalBounds = Bounds.WithX(0).WithY(0);
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
            skia.Accepts(this, Builder);
            Bounds = item.TransformBounds(Bounds);
        }
        else if (item is IFEItem_Custom custom)
        {
            Flush(true);
            var customContext = new FilterEffectCustomOperationContext(_canvas, CurrentTarget);
            custom.Accepts(customContext);
            if (CurrentTarget != customContext.Target)
            {
                CurrentTarget?.Dispose();
                CurrentTarget = customContext.Target;
            }
            Bounds = item.TransformBounds(Bounds);
        }
    }

    public SKImageFilter? Activate(FilterEffectContext context)
    {
        SKImageFilter? filter;
        Flush(false);
        using (EffectTarget cloned = CurrentTarget.Clone())
        using (var builder = new SKImageFilterBuilder())
        using (var activator = new FilterEffectActivator(Bounds, cloned, builder, _canvas))
        {
            activator.Apply(context);

            activator.Flush(false);

            filter = builder.GetFilter();
            if (filter == null && activator.CurrentTarget.Surface != null)
            {
                SKSurface innerSurface = activator.CurrentTarget.Surface.Value;
                using (SKImage skImage = innerSurface.Snapshot())
                {
                    filter = SKImageFilter.CreateImage(skImage);
                }
            }
        }

        return filter;
    }
}
