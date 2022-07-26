using BeUtl.Graphics;
using BeUtl.Graphics.Filters;
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

    protected override void OnInitializeSetters(IList<ISetter> initializing)
    {
        initializing.Add(new Setter<Vector>(Blur.SigmaProperty, new Vector(25, 25)));
    }
}
