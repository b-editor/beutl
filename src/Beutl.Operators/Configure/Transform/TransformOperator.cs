using Beutl.Graphics;
using Beutl.Graphics.Transformation;

namespace Beutl.Operators.Configure.Transform;

#pragma warning disable IDE0065
using Transform = Graphics.Transformation.Transform;

public abstract class TransformOperator<T> : ConfigureOperator<Drawable, T>
    where T : Transform, new()
{
    protected override void PreProcess(Drawable target, T value)
    {
        value.IsEnabled = IsEnabled;
    }

    protected override void OnAttached(Drawable target, T value)
    {
        if (target.Transform is not TransformGroup group)
        {
            target.Transform = group = new TransformGroup();
        }

        group.Children.Add(value);
    }

    protected override void OnDetached(Drawable target, T value)
    {
        if (target.Transform is TransformGroup group)
        {
            group.Children.Remove(value);
        }
    }
}
