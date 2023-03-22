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

    protected override void Process(Drawable target, T value)
    {
        (target.Transform as TransformGroup)?.Children.Add(value);
    }
}
