using Beutl.Media;

namespace Beutl.ProjectSystem;

/// <summary>
/// An empty interval between adjacent <see cref="Element"/>s on a single ZIndex
/// layer of a <see cref="Scene"/>. See <see cref="Scene.EnumerateGaps"/>.
/// </summary>
public readonly record struct SceneGap(int ZIndex, TimeRange Range);
