using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Beutl.Animation;
using Beutl.Media;
using Beutl.Media.Pixel;

namespace Beutl.Graphics.Effects;

public class ComposedBitmapEffect : BitmapEffect
{
    public static readonly CoreProperty<IBitmapEffect?> FirstProperty;
    public static readonly CoreProperty<IBitmapEffect?> SecondProperty;
    private IBitmapEffect? _first;
    private IBitmapEffect? _second;

    static ComposedBitmapEffect()
    {
        FirstProperty = ConfigureProperty<IBitmapEffect?, ComposedBitmapEffect>(nameof(First))
            .Accessor(o => o.First, (o, v) => o.First = v)
            .Register();

        SecondProperty = ConfigureProperty<IBitmapEffect?, ComposedBitmapEffect>(nameof(Second))
            .Accessor(o => o.Second, (o, v) => o.Second = v)
            .Register();

        AffectsRender<ComposedBitmapEffect>(FirstProperty, SecondProperty);
    }

    public ComposedBitmapEffect()
    {
        Processor = new _Processor(this);
    }

    public ComposedBitmapEffect(IBitmapEffect? first, IBitmapEffect? second)
        : this()
    {
        First = first;
        Second = second;
    }

    public IBitmapEffect? First
    {
        get => _first;
        set => SetAndRaise(FirstProperty, ref _first, value);
    }

    public IBitmapEffect? Second
    {
        get => _second;
        set => SetAndRaise(SecondProperty, ref _second, value);
    }

    public override IBitmapProcessor Processor { get; }

    public override void ApplyAnimations(IClock clock)
    {
        base.ApplyAnimations(clock);
        (First as IAnimatable)?.ApplyAnimations(clock);
        (Second as IAnimatable)?.ApplyAnimations(clock);
    }

    public override Rect TransformBounds(Rect rect)
    {
        switch ((First, Second))
        {
            case (null or { IsEnabled: false }, { IsEnabled: true }):
                return Second.TransformBounds(rect);

            case ({ IsEnabled: true }, null or { IsEnabled: false }):
                return First.TransformBounds(rect);

            case ({ IsEnabled: true }, { IsEnabled: true }):
                return Second.TransformBounds(First.TransformBounds(rect));

            default:
                return rect;
        }
    }

    private sealed class _Processor : IBitmapProcessor
    {
        private readonly ComposedBitmapEffect _obj;

        public _Processor(ComposedBitmapEffect obj) => _obj = obj;

        public void Process(in Bitmap<Bgra8888> src, out Bitmap<Bgra8888> dst)
        {
            IBitmapEffect? first = _obj.First;
            IBitmapEffect? second = _obj.Second;

            switch ((first, second))
            {
                case (null or { IsEnabled: false }, { IsEnabled: true }):
                    second.Processor.Process(in src, out dst);
                    break;
                case ({ IsEnabled: true }, null or { IsEnabled: false }):
                    first.Processor.Process(in src, out dst);
                    break;

                case ({ IsEnabled: true }, { IsEnabled: true }):
                    first.Processor.Process(src, out Bitmap<Bgra8888>? tmpDst);
                    second.Processor.Process(tmpDst, out dst);

                    if (tmpDst != dst)
                    {
                        tmpDst.Dispose();
                    }
                    break;

                default:
                    dst = src;
                    break;
            }
        }
    }
}
