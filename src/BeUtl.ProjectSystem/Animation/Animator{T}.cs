namespace BeUtl.Animation;

public abstract class Animator<T> : Animator
    where T : struct
{
    public abstract T Interpolate(float progress, T oldValue, T newValue);
}
