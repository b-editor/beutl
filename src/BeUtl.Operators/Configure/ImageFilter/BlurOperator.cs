using BeUtl.Graphics;
using BeUtl.Graphics.Filters;
using BeUtl.Language;
using BeUtl.Streaming;
using BeUtl.Styling;

namespace BeUtl.Operators.Configure.ImageFilter;

public sealed class BlurOperator : ImageFilterOperator
{
    protected override Style OnInitializeStyle(Func<IList<ISetter>> setters)
    {
        var style = new Style<Blur>();
        style.Setters.AddRange(setters());
        return style;
    }

    protected override void OnInitializeSetters(IList<ISetterDescription> initializing)
    {
        initializing.Add(new SetterDescription<Vector>(Blur.SigmaProperty)
        {
            DefaultValue = new Vector(25, 25),
            Minimum = Vector.Zero,
            Header = StringResources.Common.SigmaObservable,
            IsAnimatable = true
        });
    }
}
