namespace BEditorNext.Animation.Animators;

public sealed class Int32Animator : Animator<int>
{
    public override int Multiply(int left, float right)
    {
        return (int)MathF.Round(left * right);
    }
}
