namespace BEditorNext.Animation.Animators;

public sealed class SByteAnimator : Animator<sbyte>
{
    public override sbyte Multiply(sbyte left, float right)
    {
        return (sbyte)MathF.Round(left * right);
    }
}
