namespace BEditorNext.Animation;

public abstract class Animator<T> : Animator
    where T : struct
{
    public abstract T Multiply(T left, float right);
}
