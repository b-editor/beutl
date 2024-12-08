using System.ComponentModel;
using Beutl.Animation;

namespace Beutl.Graphics.Transformation;

[Obsolete("Use TransformGroup instead.")]
public sealed class MultiTransform : Transform
{
    public static readonly CoreProperty<ITransform?> LeftProperty;
    public static readonly CoreProperty<ITransform?> RightProperty;
    private ITransform? _left;
    private ITransform? _right;

    static MultiTransform()
    {
        LeftProperty = ConfigureProperty<ITransform?, MultiTransform>(nameof(Left))
            .Accessor(o => o.Left, (o, v) => o.Left = v)
            .Register();

        RightProperty = ConfigureProperty<ITransform?, MultiTransform>(nameof(Right))
            .Accessor(o => o.Right, (o, v) => o.Right = v)
            .Register();

        AffectsRender<MultiTransform>(LeftProperty, RightProperty);
        Hierarchy<MultiTransform>(LeftProperty, RightProperty);
    }

    public MultiTransform()
    {
    }

    public MultiTransform(ITransform? left, ITransform? right)
    {
        Left = left;
        Right = right;
    }

    public ITransform? Left
    {
        get => _left;
        set => SetAndRaise(LeftProperty, ref _left, value);
    }

    // 元のTransform、Inner
    public ITransform? Right
    {
        get => _right;
        set => SetAndRaise(RightProperty, ref _right, value);
    }

    public override Matrix Value
    {
        get
        {
            return (Left, Right) switch
            {
                (null or { IsEnabled: false }, { IsEnabled: true })
                    => Right.Value,
                ({ IsEnabled: true }, null or { IsEnabled: false })
                    => Left.Value,
                ({ IsEnabled: true }, { IsEnabled: true })
                    => Left.Value * Right.Value,
                _ => Matrix.Identity,
            };
        }
    }

    public override void ApplyAnimations(IClock clock)
    {
        base.ApplyAnimations(clock);
        (Left as IAnimatable)?.ApplyAnimations(clock);
        (Right as IAnimatable)?.ApplyAnimations(clock);
    }
}
