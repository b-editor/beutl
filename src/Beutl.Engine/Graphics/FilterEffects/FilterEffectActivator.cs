using Beutl.Media.Source;

using SkiaSharp;

namespace Beutl.Graphics.Effects;

public sealed class FilterEffectActivator(EffectTargets targets, SKImageFilterBuilder builder, IImmediateCanvasFactory factory) : IDisposable
{
    private readonly IImmediateCanvasFactory _factory = factory;

    public SKImageFilterBuilder Builder { get; } = builder;

    public EffectTargets CurrentTargets { get; private set; } = targets;

    public void Dispose()
    {
    }

    public void Flush(bool force = true)
    {
        if (force
            || Builder.HasFilter()
            || (CurrentTargets.Count == 1 && CurrentTargets[0].NodeOperation != null))
        {
            using var paint = new SKPaint
            {
                ImageFilter = Builder.GetFilter(),
            };

            for (int i = 0; i < CurrentTargets.Count; i++)
            {
                EffectTarget target = CurrentTargets[i];
                SKSurface? surface = _factory.CreateRenderTarget((int)target.OriginalBounds.Width, (int)target.OriginalBounds.Height);

                if (surface != null)
                {
                    using ImmediateCanvas canvas = _factory.CreateCanvas(surface, true);

                    using (canvas.PushTransform(Matrix.CreateTranslation(-target.OriginalBounds.X, -target.OriginalBounds.Y)))
                    using (canvas.PushPaint(paint))
                    {
                        target.Draw(canvas);
                    }

                    using var surfaceRef = Ref<SKSurface>.Create(surface);
                    var newTarget = new EffectTarget(surfaceRef, target.Bounds)
                    {
                        OriginalBounds = target.OriginalBounds
                    };
                    CurrentTargets[i] = newTarget;
                    target.Dispose();
                }
                else
                {
                    target?.Dispose();

                    CurrentTargets.RemoveAt(i);
                    i--;
                }

            }

            Builder.Clear();
        }
    }

    // 最小単位である'IFEItem'の数がわからないので 'count'は'nullable'
    public void Apply(FilterEffectContext context)
    {
        if (CurrentTargets.Count == 0) return;

        foreach (FEItemWrapper item in context._items)
        {
            if (item.Item is IFEItem_Skia skia)
            {
                skia.Accepts(this, Builder);
                foreach (EffectTarget t in CurrentTargets)
                {
                    t.Bounds = item.Item.TransformBounds(t.Bounds);
                    t.OriginalBounds = item.Item.TransformBounds(t.OriginalBounds);
                }
            }
            else if (item.Item is IFEItem_Custom custom)
            {
                Flush(true);
                if (CurrentTargets.Count == 0) return;

                var customContext = new CustomFilterEffectContext(_factory, CurrentTargets);
                custom.Accepts(customContext);

                foreach (EffectTarget t in CurrentTargets)
                {
                    //t.Bounds = item.TransformBounds(t.Bounds);
                    t.OriginalBounds = t.Bounds.WithX(0).WithY(0);
                }
            }
        }

        if (context._renderTimeItems.Count <= 0) return;

        {
            Flush(false);
            if (CurrentTargets.Count == 0) return;
            using var ctx = new FilterEffectContext(CurrentTargets.CalculateBounds());

            foreach (object item in context._renderTimeItems)
            {
                switch (item)
                {
                    case FilterEffect fe:
                        ctx.Apply(fe);
                        break;
                    case FEItemWrapper feitem:
                        ctx._items.Add(feitem);
                        break;
                }
            }

            Apply(ctx);
        }
    }

    public SKImageFilter? Activate(FilterEffectContext context)
    {
        SKImageFilter? filter;
        Flush(false);
        using (EffectTargets cloned = CurrentTargets.Clone())
        using (var builder = new SKImageFilterBuilder())
        using (var activator = new FilterEffectActivator(cloned, builder, _factory))
        {
            activator.Apply(context);

            activator.Flush(false);

            filter = builder.GetFilter();
            if (filter == null)
            {
                foreach (EffectTarget t in activator.CurrentTargets)
                {
                    if (t.Surface != null)
                    {
                        SKSurface innerSurface = t.Surface.Value;
                        using (SKImage skImage = innerSurface.Snapshot())
                        {
                            if (filter == null)
                            {
                                filter = SKImageFilter.CreateImage(skImage);
                            }
                            else
                            {
                                filter = SKImageFilter.CreateCompose(filter, SKImageFilter.CreateImage(skImage));
                            }
                        }
                    }
                }
            }
        }

        return filter;
    }
}
