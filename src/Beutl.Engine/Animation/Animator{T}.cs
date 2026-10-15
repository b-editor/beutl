namespace Beutl.Animation;

public abstract class Animator<T>
{
    public abstract T Interpolate(float progress, T oldValue, T newValue);
}
