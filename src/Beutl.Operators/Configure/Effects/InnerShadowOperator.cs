using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Media;
using Beutl.Styling;

namespace Beutl.Operators.Configure.Effects;

public sealed class InnerShadowOperator : FilterEffectOperator<InnerShadow>
{
    public Setter<Point> Position { set; get; } = new(InnerShadow.PositionProperty, new(10, 10));

    public Setter<Size> Sigma { set; get; } = new(InnerShadow.SigmaProperty, new(10, 10));

    public Setter<Color> Color { set; get; } = new(InnerShadow.ColorProperty, Colors.Black);

    public Setter<bool> ShadowOnly { set; get; } = new(InnerShadow.ShadowOnlyProperty, false);
}
