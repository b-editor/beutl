using Beutl.Media;

namespace Beutl.ProjectSystem;

/// <summary>
/// An empty interval between adjacent <see cref="Element"/>s on a single ZIndex
/// layer of a <see cref="Scene"/>. See <see cref="Scene.EnumerateGaps"/>.
/// </summary>
/// <param name="Anchor">
/// The element ending at <see cref="Range"/>'s start — the last element of the covered run before the
/// gap. Passing it to <see cref="Scene.CloseGapAfter"/> closes this gap, and it is the element gap
/// navigation selects so a follow-up Close Gap acts on the gap the playhead moved to.
/// </param>
public readonly record struct SceneGap(int ZIndex, TimeRange Range, Element Anchor);
