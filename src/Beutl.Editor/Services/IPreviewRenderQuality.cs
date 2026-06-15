using Beutl.Models;

using Reactive.Bindings;

namespace Beutl.Editor.Services;

/// <summary>
/// Per-edit-view preview render-quality selection (feature 003, US4). Resolved via
/// <see cref="System.IServiceProvider"/> so tool tabs bind it without referencing the app-layer
/// EditViewModel. Non-persisted; changing it rebuilds the renderer/frame-cache at the new output scale.
/// </summary>
public interface IPreviewRenderQuality
{
    /// <summary>The currently selected preview render scale.</summary>
    IReactiveProperty<RenderScale> PreviewScale { get; }

    /// <summary>The selectable preview render-scale options.</summary>
    IReadOnlyList<RenderScale> PreviewScaleOptions { get; }
}
