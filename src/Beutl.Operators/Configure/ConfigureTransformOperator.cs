using Beutl.Graphics;
using Beutl.Graphics.Transformation;
using Beutl.Operation;
using Beutl.Styling;

namespace Beutl.Operators.Configure;

public sealed class ConfigureTransformOperator : SourceStyler
{
    public new Setter<ITransform?> Transform { get; set; } = new Setter<ITransform?>(Drawable.TransformProperty, new TransformGroup());

    protected override Style OnInitializeStyle(Func<IList<ISetter>> setters)
    {
        var style = new Style<Drawable>();
        style.Setters.AddRange(setters());
        return style;
    }
}
