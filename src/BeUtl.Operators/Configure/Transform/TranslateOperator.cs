using BeUtl.Graphics.Transformation;
using BeUtl.Language;
using BeUtl.Streaming;
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

    protected override void OnInitializeSetters(IList<ISetterDescription> initializing)
    {
        initializing.Add(new SetterDescription<float>(TranslateTransform.XProperty)
        {
            Header = StringResources.Common.XObservable,
            DefaultValue = 0,
            IsAnimatable = true,
        });
        initializing.Add(new SetterDescription<float>(TranslateTransform.YProperty)
        {
            Header = StringResources.Common.YObservable,
            DefaultValue = 0,
            IsAnimatable = true,
        });
    }
}
