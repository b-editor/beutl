using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Styling;

namespace Beutl.Operators.Configure.Effects;

public sealed class BlurOperator : FilterEffectOperator<Blur>
{
    public Setter<Size> Sigma { get; set; } = new(Blur.SigmaProperty, new(10, 10));
}
