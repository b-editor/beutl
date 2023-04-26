using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Operation;
using Beutl.Styling;

namespace Beutl.Operators.Source;

public sealed class RoundedRectOperator : DrawablePublishOperator<RoundedRectShape>
{
    protected override void OnInitializeSetters(IList<ISetter> initializing)
    {
        initializing.Add(new Setter<float>(Drawable.WidthProperty, 100));
        initializing.Add(new Setter<float>(Drawable.HeightProperty, 100));
        initializing.Add(new Setter<float>(RoundedRectShape.StrokeWidthProperty, 4000));
        initializing.Add(new Setter<IBrush?>(Drawable.ForegroundProperty, new SolidColorBrush(Colors.White)));
        initializing.Add(new Setter<CornerRadius>(RoundedRectShape.CornerRadiusProperty, new CornerRadius(25)));
    }
}
