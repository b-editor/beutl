using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

/// <summary>
/// Slip: shifts the source-media window of elements without changing their geometry, committing one history entry.
/// Roll and Slide in <see cref="IElementResizeService"/> shift media windows through the same <c>SlippableMedia</c>.
/// </summary>
public interface IElementSlipService
{
    /// <summary>
    /// Shifts the source window of every slip-able stream by one shared delta, converted to source time and clamped
    /// so that no stream leaves its source, without moving or resizing elements. Sharing the delta keeps linked media
    /// in sync. Locked, duplicated or off-scene elements are skipped, and an unsupported speed expression or time
    /// mapping rejects the whole slip before anything changes. Returns <see langword="false"/> without a commit when
    /// nothing moves.
    /// </summary>
    bool Slip(Scene scene, IReadOnlyList<Element> elements, TimeSpan delta);
}
