using Reactive.Bindings;

namespace Beutl.Extensibility;

/// <summary>A tool tab that the user can pin to the target it shows.</summary>
/// <remarks>
/// A pinned tab keeps its target: it ignores the editor selection, and the host opens another tab instead
/// of showing a different target in it. This is unrelated to Dock's pinning, which auto-hides a tab.
/// </remarks>
public interface IPinnableToolContext : IToolContext
{
    /// <summary>Gets whether the tab is pinned to its target.</summary>
    /// <remarks>Clear it when the target goes away, so a pinned tab is never left empty.</remarks>
    IReactiveProperty<bool> IsPinned { get; }

    /// <summary>Gets whether the tab shows a target it can be pinned to.</summary>
    IReadOnlyReactiveProperty<bool> HasTarget { get; }
}
