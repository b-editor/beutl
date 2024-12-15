using System.Numerics;
using Beutl.Animation;
using Beutl.Serialization;

namespace Beutl.Graphics3D.Transformation;

public sealed class TransformGroup3D : Transform3D
{
    public static readonly CoreProperty<Transforms3D> ChildrenProperty;
    private readonly Transforms3D _children;

    static TransformGroup3D()
    {
        ChildrenProperty = ConfigureProperty<Transforms3D, TransformGroup3D>(nameof(Children))
            .Accessor(o => o.Children, (o, v) => o.Children = v)
            .Register();
    }

    public TransformGroup3D()
    {
        _children = new Transforms3D(this);
        _children.Invalidated += (_, e) => RaiseInvalidated(e);
    }

    [NotAutoSerialized]
    public Transforms3D Children
    {
        get => _children;
        set => _children.Replace(value);
    }

    public override Matrix4x4 Value
    {
        get
        {
            Matrix4x4 value = Matrix4x4.Identity;

            foreach (ITransform3D item in _children.GetMarshal().Value)
            {
                if ((item as Transform3D)?.IsEnabled != false)
                    value = item.Value * value;
            }

            return value;
        }
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue(nameof(Children), Children);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        if (context.GetValue<Transforms3D>(nameof(Children)) is { } children)
        {
            Children = children;
        }
    }

    public override void ApplyAnimations(IClock clock)
    {
        base.ApplyAnimations(clock);
        foreach (ITransform3D item in Children.GetMarshal().Value)
        {
            (item as Animatable)?.ApplyAnimations(clock);
        }
    }
}
