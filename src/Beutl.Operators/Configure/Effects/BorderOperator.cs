using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Media;
using Beutl.Styling;

namespace Beutl.Operators.Configure.Effects;

public sealed class BorderOperator : FilterEffectOperator<Border>
{
    public Setter<Point> Offset { get; set; } = new(Border.OffsetProperty, default);

    public Setter<int> Thickness { get; set; } = new(Border.ThicknessProperty, 5);

    public Setter<Color> Color { get; set; } = new(Border.ColorProperty, Colors.Black);

    public Setter<Border.MaskTypes> MaskType { get; set; } = new(Border.MaskTypeProperty, Border.MaskTypes.None);

    public new Setter<Border.BorderStyles> Style { get; set; } = new(Border.StyleProperty, Border.BorderStyles.Background);
}
