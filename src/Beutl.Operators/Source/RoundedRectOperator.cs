using Beutl.Graphics;
using Beutl.Graphics.Filters;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.Operation;
using Beutl.Styling;

namespace Beutl.Operators.Source;

public sealed class RoundedRectOperator : DrawablePublishOperator<RoundedRectShape>
{
    public Setter<float> Width { get; set; } = new(Shape.WidthProperty, 100);

    public Setter<float> Height { get; set; } = new(Shape.HeightProperty, 100);
    
    public Setter<CornerRadius> CornerRadius { get; set; } = new(RoundedRectShape.CornerRadiusProperty, new(25));

    public Setter<ITransform?> Transform { get; set; } = new(Drawable.TransformProperty, null);

    public Setter<IPen?> Pen { get; set; } = new(Shape.PenProperty);

    public Setter<IBrush?> Fill { get; set; } = new(Drawable.ForegroundProperty, new SolidColorBrush(Colors.White));

    public Setter<IImageFilter?> Filter { get; set; } = new(Drawable.FilterProperty, null);
}
