using Beutl.Graphics;
using Beutl.Media.Source;
using Beutl.Streaming;
using Beutl.Styling;

namespace Beutl.Operators.Source;

public sealed class SourceImageOperator : StreamStyledSource
{
    protected override Style OnInitializeStyle(Func<IList<ISetter>> setters)
    {
        var style = new Style<SourceImage>();
        style.Setters.AddRange(setters());
        return style;
    }

    protected override void OnInitializeSetters(IList<ISetter> initializing)
    {
        initializing.Add(new Setter<IImageSource?>(SourceImage.SourceProperty, null));
    }
}
