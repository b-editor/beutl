using BeUtl.Graphics.Transformation;
using BeUtl.Styling;

namespace BeUtl.Operators.Configure.Transform;

public sealed class ScaleOperator : TransformOperator
{
    protected override Style OnInitializeStyle(Func<IList<ISetter>> setters)
    {
        var style = new Style<ScaleTransform>();
        style.Setters.AddRange(setters());
        return style;
    }

    protected override void OnInitializeSetters(IList<ISetter> initializing)
    {
        initializing.Add(new Setter<float>(ScaleTransform.ScaleProperty, 1));
        initializing.Add(new Setter<float>(ScaleTransform.ScaleXProperty, 1));
        initializing.Add(new Setter<float>(ScaleTransform.ScaleYProperty, 1));
    }
}
