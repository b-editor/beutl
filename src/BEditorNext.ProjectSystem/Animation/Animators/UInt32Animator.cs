namespace BEditorNext.Animation.Animators;

public sealed class UInt32Animator : Animator<uint>
{
    public override uint Multiply(uint left, float right)
    {
        return (uint)MathF.Round(left * right);
    }
}
