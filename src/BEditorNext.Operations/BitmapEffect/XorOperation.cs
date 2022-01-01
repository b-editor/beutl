using BEditorNext.Graphics.Effects;

namespace BEditorNext.Operations.BitmapEffect;

public sealed class XorOperation : BitmapEffectOperation<Xor>
{
    public override Xor Effect { get; } = new();
}
