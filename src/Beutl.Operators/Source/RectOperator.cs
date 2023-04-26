using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Operation;
using Beutl.Styling;

namespace Beutl.Operators.Source;

public sealed class RectOperator : DrawablePublishOperator<RectShape>
{
    protected override void OnInitializeSetters(IList<ISetter> initializing)
    {
        initializing.Add(new Setter<float>(Drawable.WidthProperty, 100));
        initializing.Add(new Setter<float>(Drawable.HeightProperty, 100));
        initializing.Add(new Setter<float>(RectShape.StrokeWidthProperty, 4000));
        initializing.Add(new Setter<IBrush?>(Drawable.ForegroundProperty, new SolidColorBrush(Colors.White)));
    }
}
