using BeUtl.Graphics.Transformation;
using BeUtl.Styling;

namespace BeUtl.Operators.Configure.Transform;

public sealed class SkewOperator : TransformOperator
{
    protected override Style OnInitializeStyle(Func<IList<ISetter>> setters)
    {
        var style = new Style<SkewTransform>();
        style.Setters.AddRange(setters());
        return style;
    }

    protected override void OnInitializeSetters(IList<ISetter> initializing)
    {
        initializing.Add(new Setter<float>(SkewTransform.SkewXProperty, 0));
        initializing.Add(new Setter<float>(SkewTransform.SkewYProperty, 0));
    }
}
