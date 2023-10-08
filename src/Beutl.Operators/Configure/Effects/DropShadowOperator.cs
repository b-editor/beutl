using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Media;
using Beutl.Styling;

namespace Beutl.Operators.Configure.Effects;

public sealed class DropShadowOperator : FilterEffectOperator<DropShadow>
{
    public Setter<Point> Position { set; get; } = new(DropShadow.PositionProperty, new(10, 10));

    public Setter<Size> Sigma { set; get; } = new(DropShadow.SigmaProperty, new(10, 10));

    public Setter<Color> Color { set; get; } = new(DropShadow.ColorProperty, Colors.Black);

    public Setter<bool> ShadowOnly { set; get; } = new(DropShadow.ShadowOnlyProperty, false);
}
