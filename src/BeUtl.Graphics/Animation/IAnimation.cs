using BeUtl.Animation.Easings;
using BeUtl.Collections;
using BeUtl.Styling;

namespace BeUtl.Animation;

public interface IAnimation : IJsonSerializable
{
    CoreProperty Property { get; }

    ICoreReadOnlyList<IAnimationSpan> Children { get; }

    event EventHandler? Invalidated;

    void ApplyTo(ICoreObject obj, TimeSpan ts);
}
