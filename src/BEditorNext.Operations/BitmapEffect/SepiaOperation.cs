using BEditorNext.Graphics.Effects;

namespace BEditorNext.Operations.BitmapEffect;

public sealed class SepiaOperation : BitmapEffectOperation<Sepia>
{
    public override Sepia Effect { get; } = new();
}
