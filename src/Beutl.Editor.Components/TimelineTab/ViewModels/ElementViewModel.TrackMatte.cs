using Beutl.Editor.Services;
using Beutl.Graphics;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.TimelineTab.ViewModels;

public sealed partial class ElementViewModel
{
    public ReadOnlyReactivePropertySlim<string?> MatteIndicator { get; private set; } = null!;

    public void SetMatteSource(MatteSourceKind kind, int? zIndex = null)
    {
        MatteMode mode = kind == MatteSourceKind.Layer && zIndex == null ? MatteMode.None
            : Model.MatteMode.Value == MatteMode.None ? MatteMode.Alpha : Model.MatteMode.Value;
        Timeline.EditorContext.GetRequiredService<IElementAttributeService>().SetTrackMatte(Model, kind, zIndex, mode);
    }

    public void SetMatteMode(MatteMode mode)
    {
        Timeline.EditorContext.GetRequiredService<IElementAttributeService>()
            .SetTrackMatte(Model, Model.MatteSourceKind.Value, Model.MatteSourceZIndex.Value, mode);
    }

    public void ToggleOutputVisibility()
    {
        Timeline.EditorContext.GetRequiredService<IElementAttributeService>()
            .SetVisibleInOutput(Model, !Model.IsVisibleInOutput.Value);
    }

    private void InitializeTrackMatte()
    {
        MatteIndicator = Model.MatteSourceZIndex.GetObservable()
            .CombineLatest(Model.MatteSourceKind.GetObservable(), Model.MatteMode.GetObservable(), Model.ZIndex.GetObservable(),
                (_, _, mode, own) => Model.GetMatteSourceZIndex() is { } z && mode != MatteMode.None
                    ? $"{(z < own ? "↑" : z > own ? "↓" : "↻")} {z}" : null)
            .ToReadOnlyReactivePropertySlim().DisposeWith(_disposables);
    }
}
