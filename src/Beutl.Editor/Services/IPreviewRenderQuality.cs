using Beutl.Models;

using Reactive.Bindings;

namespace Beutl.Editor.Services;

/// <summary>
/// Per-edit-view preview render-quality selection (feature 003, US4). Resolved via
/// <see cref="System.IServiceProvider"/> so tool tabs can bind the selector without
/// depending on the app-layer EditViewModel. The selection is non-persisted and changing
/// it rebuilds the renderer/frame-cache at the new output scale.
/// </summary>
public interface IPreviewRenderQuality
{
    /// <summary>The currently selected preview render scale.</summary>
    IReactiveProperty<RenderScale> PreviewScale { get; }

    /// <summary>The selectable preview render-scale options.</summary>
    IReadOnlyList<RenderScale> PreviewScaleOptions { get; }
}
