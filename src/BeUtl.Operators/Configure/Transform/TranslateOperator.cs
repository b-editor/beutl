using BeUtl.Graphics.Transformation;
using BeUtl.Styling;

namespace BeUtl.Operators.Configure.Transform;

public sealed class TranslateOperator : TransformOperator
{
    protected override Style OnInitializeStyle(Func<IList<ISetter>> setters)
    {
        var style = new Style<TranslateTransform>();
        style.Setters.AddRange(setters());
        return style;
    }

    protected override void OnInitializeSetters(IList<ISetter> initializing)
    {
        initializing.Add(new Setter<float>(TranslateTransform.XProperty, 0));
        initializing.Add(new Setter<float>(TranslateTransform.YProperty, 0));
    }
}
