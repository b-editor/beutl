namespace BEditorNext.Animation.Animators;

public sealed class FloatAnimator : Animator<float>
{
    public override float Multiply(float left, float right)
    {
        return left * right;
    }
}
