namespace Beutl.Animation;

public abstract class BaseAnimation
{
    protected BaseAnimation(CoreProperty property)
    {
        Property = property;
    }

    public CoreProperty Property { get; }
}
