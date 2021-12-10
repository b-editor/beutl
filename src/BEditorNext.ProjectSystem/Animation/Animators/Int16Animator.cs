namespace BEditorNext.Animation.Animators;

public sealed class Int16Animator : Animator<short>
{
    public override short Multiply(short left, float right)
    {
        return (short)MathF.Round(left * right);
    }
}
