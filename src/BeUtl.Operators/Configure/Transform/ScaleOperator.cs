
using BeUtl.Graphics.Transformation;
using BeUtl.Language;
using BeUtl.Streaming;
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

    protected override void OnInitializeSetters(IList<ISetterDescription> initializing)
    {
        initializing.Add(new SetterDescription<float>(ScaleTransform.ScaleProperty)
        {
            Header = StringResources.Common.ScaleObservable,
            DefaultValue = 1,
            IsAnimatable = true,
            Formatter = Format,
            Parser = Parse
        });
        initializing.Add(new SetterDescription<float>(ScaleTransform.ScaleXProperty)
        {
            Header = StringResources.Common.ScaleXObservable,
            DefaultValue = 1,
            IsAnimatable = true,
            Formatter = Format,
            Parser = Parse
        });
        initializing.Add(new SetterDescription<float>(ScaleTransform.ScaleYProperty)
        {
            Header = StringResources.Common.ScaleYObservable,
            DefaultValue = 1,
            IsAnimatable = true,
            Formatter = Format,
            Parser = Parse
        });
    }

    private string Format(float value)
    {
        return (value * 100).ToString();
    }

    private (float, bool) Parse(string s)
    {
        if (float.TryParse(s, out var v))
        {
            return (v / 100, true);
        }
        else
        {
            return (0, false);
        }
    }
}
