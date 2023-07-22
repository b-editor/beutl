using Beutl.Graphics.Effects;

namespace Beutl.Graphics.Rendering;

public sealed class FilterEffectNode : ContainerNode
{
    //private RecentContext _recentContext = new();

    // DeferredCanvasでFilterEffectContextを作成できない。Boundsが未知なので。
    public FilterEffectNode(FilterEffect filterEffect)
    {
        FilterEffect = filterEffect;
    }

    public FilterEffect FilterEffect { get; }

    public override void Dispose()
    {
        base.Dispose();
        //_recentContext.Dispose();
        //_recentContext = null!;
    }

    public bool Equals(FilterEffect filterEffect)
    {
        return FilterEffect == filterEffect;
    }

    protected override Rect TransformBounds(Rect bounds)
    {
        return FilterEffect.TransformBounds(bounds);
    }

    public override void Render(ImmediateCanvas canvas)
    {
        var context = new FilterEffectContext(OriginalBounds);
        context.Apply(FilterEffect);
        //_recentContext.Add(context);

        using (var builder = new SKImageFilterBuilder())
        using (var target = new EffectTarget(this))
        using (var activator = new FilterEffectActivator(OriginalBounds, target, builder, canvas))
        {
            activator.Apply(context);

#if false
            if (builder.HasFilter())
            {
                using (var paint = new SKPaint())
                {
                    paint.ImageFilter = builder.GetFilter();
                    int count = canvas.Canvas.SaveLayer(Bounds.ToSKRect(), paint);
                    canvas.Canvas.Translate(activator.OriginalBounds.X, activator.OriginalBounds.Y);

                    activator.CurrentTarget.Draw(canvas);

                    canvas.Canvas.RestoreToCount(count);
                }
            }
            else
#else
            // 上のコードは、フレームバッファごと回転してしまう。
            // (SaveLayerでlilmitを指定しても)
            activator.Flush(true);
#endif
            if (activator.CurrentTarget.Surface != null)
            {
                canvas.Canvas.DrawSurface(activator.CurrentTarget.Surface.Value, activator.Bounds.X, activator.Bounds.Y);
            }
            else
            {
                base.Render(canvas);
            }
        }
    }

    //private class RecentContext : IDisposable
    //{
    //    private FilterEffectContext? _slot0;
    //    private FilterEffectContext? _slot1;
    //    private FilterEffectContext? _slot2;
    //    private int _index;

    //    private ref FilterEffectContext? GetRef(int index)
    //    {
    //        switch (index)
    //        {
    //            case 0: return ref _slot0;
    //            case 1: return ref _slot1;
    //            case 2: return ref _slot2;
    //        }

    //        return ref Unsafe.NullRef<FilterEffectContext?>();
    //    }

    //    public void Add(FilterEffectContext context)
    //    {
    //        FilterEffectContext? slot = GetRef(_index);
    //        if (!Unsafe.IsNullRef(ref slot))
    //        {
    //            slot?.Dispose();
    //            slot = context;

    //            _index++;
    //            // 折り返す
    //            _index %= 3;
    //        }
    //    }

    //    public bool EqualsAll()
    //    {
    //        EqualityComparer<FilterEffectContext> comparer = EqualityComparer<FilterEffectContext>.Default;
    //        return comparer.Equals(_slot0, _slot1) && comparer.Equals(_slot1, _slot2);
    //    }

    //    public void Dispose()
    //    {
    //        _index = 0;
    //        _slot0?.Dispose();
    //        _slot0 = null;
    //        _slot1?.Dispose();
    //        _slot1 = null;
    //        _slot2?.Dispose();
    //        _slot2 = null;
    //    }
    //}
}
