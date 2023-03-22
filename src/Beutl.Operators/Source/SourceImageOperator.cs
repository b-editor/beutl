using Beutl.Graphics;
using Beutl.Media.Source;
using Beutl.Operation;
using Beutl.Styling;

namespace Beutl.Operators.Source;

public sealed class SourceImageOperator : DrawablePublishOperator<SourceImage>
{
    protected override void OnInitializeSetters(IList<ISetter> initializing)
    {
        initializing.Add(new Setter<IImageSource?>(SourceImage.SourceProperty, null));
    }
}
