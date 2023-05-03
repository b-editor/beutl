using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Operation;
using Beutl.Styling;

namespace Beutl.Operators.Source;

public sealed class RectOperator : DrawablePublishOperator<RectShape>
{
    public Setter<float> Width { get; set; } = new(Shape.WidthProperty, 100);

    public Setter<float> Height { get; set; } = new(Shape.HeightProperty, 100);

    public Setter<IPen?> Pen { get; set; } = new(Shape.PenProperty);

    public Setter<IBrush?> Fill { get; set; } = new(Drawable.ForegroundProperty, new SolidColorBrush(Colors.White));
}
