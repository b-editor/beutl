namespace Beutl.Animation;

public abstract class Animator<T> : Animator
{
    public abstract T Interpolate(float progress, T oldValue, T newValue);

    public virtual T? DefaultValue()
    {
        return default;
    }
}
