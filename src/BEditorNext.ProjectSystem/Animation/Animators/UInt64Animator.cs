namespace BEditorNext.Animation.Animators;

public sealed class UInt64Animator : Animator<ulong>
{
    public override ulong Multiply(ulong left, float right)
    {
        return (ulong)MathF.Round(left * right);
    }
}
