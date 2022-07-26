using BeUtl.Graphics.Transformation;
using BeUtl.Styling;

namespace BeUtl.Operators.Configure.Transform;

public sealed class Rotation3DOperator : TransformOperator
{
    protected override Style OnInitializeStyle(Func<IList<ISetter>> setters)
    {
        var style = new Style<Rotation3DTransform>();
        style.Setters.AddRange(setters());
        return style;
    }

    protected override void OnInitializeSetters(IList<ISetter> initializing)
    {
        initializing.Add(new Setter<float>(Rotation3DTransform.RotationXProperty, 0));
        initializing.Add(new Setter<float>(Rotation3DTransform.RotationYProperty, 0));
        initializing.Add(new Setter<float>(Rotation3DTransform.RotationZProperty, 0));
        initializing.Add(new Setter<float>(Rotation3DTransform.CenterXProperty, 0));
        initializing.Add(new Setter<float>(Rotation3DTransform.CenterYProperty, 0));
        initializing.Add(new Setter<float>(Rotation3DTransform.CenterZProperty, 0));
        initializing.Add(new Setter<float>(Rotation3DTransform.DepthProperty, 0));
    }
}
