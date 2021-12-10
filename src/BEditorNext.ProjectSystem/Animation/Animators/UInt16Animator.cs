namespace BEditorNext.Animation.Animators;

public sealed class UInt16Animator : Animator<ushort>
{
    public override ushort Multiply(ushort left, float right)
    {
        return (ushort)MathF.Round(left * right);
    }
}
