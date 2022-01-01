using BEditorNext.Graphics.Effects;

namespace BEditorNext.Operations.BitmapEffect;

public sealed class InvertOpacityOperation : BitmapEffectOperation<InvertOpacity>
{
    public override InvertOpacity Effect { get; } = new();
}
