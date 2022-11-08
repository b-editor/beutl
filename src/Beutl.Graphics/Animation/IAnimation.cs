using Beutl.Animation.Easings;
using Beutl.Collections;
using Beutl.Styling;

namespace Beutl.Animation;

public interface IAnimation : IJsonSerializable
{
    CoreProperty Property { get; }

    ICoreReadOnlyList<IAnimationSpan> Children { get; }

    event EventHandler? Invalidated;

    void ApplyTo(ICoreObject obj, TimeSpan ts);
}
