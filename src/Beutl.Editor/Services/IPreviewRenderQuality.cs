using Beutl.Models;

using Reactive.Bindings;

namespace Beutl.Editor.Services;

/// <summary>Per-edit-view preview render-quality selection.</summary>
public interface IPreviewRenderQuality
{
    /// <summary>The currently selected preview render scale.</summary>
    IReactiveProperty<RenderScale> PreviewScale { get; }

    /// <summary>The selectable preview render-scale options.</summary>
    IReadOnlyList<RenderScale> PreviewScaleOptions { get; }
}
