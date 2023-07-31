using Beutl.Graphics.Effects;

namespace Beutl.Operators.Configure.Effects;

public sealed class BlurOperator : FilterEffectOperator<Blur>
{
    protected override IEnumerable<CoreProperty> GetProperties()
    {
        yield return Blur.SigmaProperty;
    }
}
