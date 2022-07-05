using BeUtl.Graphics.Transformation;
using BeUtl.Language;
using BeUtl.Streaming;
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

    protected override void OnInitializeSetters(IList<ISetterDescription> initializing)
    {
        initializing.Add(new SetterDescription<float>(SkewTransform.SkewXProperty)
        {
            Header = StringResources.Common.SkewXObservable,
            DefaultValue = 0,
            IsAnimatable = true,
        });
        initializing.Add(new SetterDescription<float>(SkewTransform.SkewYProperty)
        {
            Header = StringResources.Common.SkewYObservable,
            DefaultValue = 0,
            IsAnimatable = true,
        });
    }
}
