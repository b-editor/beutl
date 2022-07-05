
using BeUtl.Graphics.Transformation;
using BeUtl.Language;
using BeUtl.Streaming;
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

    protected override void OnInitializeSetters(IList<ISetterDescription> initializing)
    {
        initializing.Add(new SetterDescription<float>(RotationTransform.RotationProperty)
        {
            Header = StringResources.Common.RotationObservable,
            DefaultValue = 0,
            IsAnimatable = true,
        });
    }
}
