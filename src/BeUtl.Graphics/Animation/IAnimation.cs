using BeUtl.Animation.Easings;
using BeUtl.Collections;

namespace BeUtl.Animation;

public interface IAnimation : IJsonSerializable
{
    CoreProperty Property { get; }

    ICoreReadOnlyList<IAnimationSpan> Children { get; }

    event EventHandler? Invalidated;
}
