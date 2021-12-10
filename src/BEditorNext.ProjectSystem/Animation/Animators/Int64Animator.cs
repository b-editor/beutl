namespace BEditorNext.Animation.Animators;

public sealed class Int64Animator : Animator<long>
{
    public override long Multiply(long left, float right)
    {
        return (long)MathF.Round(left * right);
    }
}
