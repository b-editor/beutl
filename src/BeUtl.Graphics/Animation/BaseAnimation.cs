using System.Text.Json.Nodes;

using BeUtl.Animation.Easings;

namespace BeUtl.Animation;

public abstract class BaseAnimation
{
    protected BaseAnimation(CoreProperty property)
    {
        Property = property;
    }

    public CoreProperty Property { get; }
}
