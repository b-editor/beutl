using BeUtl.Graphics.Transformation;
using BeUtl.Styling;

namespace BeUtl.Operators.Configure.Transform;

public sealed class RotationOperator : TransformOperator
{
    protected override Style OnInitializeStyle(Func<IList<ISetter>> setters)
    {
        var style = new Style<RotationTransform>();
        style.Setters.AddRange(setters());
        return style;
    }

    protected override void OnInitializeSetters(IList<ISetter> initializing)
    {
        initializing.Add(new Setter<float>(RotationTransform.RotationProperty, 0));
    }
}
