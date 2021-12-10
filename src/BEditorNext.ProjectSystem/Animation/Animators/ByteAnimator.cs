namespace BEditorNext.Animation.Animators;

public sealed class ByteAnimator : Animator<byte>
{
    public override byte Multiply(byte left, float right)
    {
        return (byte)MathF.Round(left * right);
    }
}
