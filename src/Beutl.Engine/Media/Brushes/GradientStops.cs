using Beutl.Collections;

namespace Beutl.Media;

/// <summary>
/// A collection of <see cref="GradientStop"/>s.
/// </summary>
public sealed class GradientStops : HierarchicalList<GradientStop>
{
    public GradientStops(IModifiableHierarchical parent) : base(parent)
    {
    }

    public GradientStops()
    {
    }
}
