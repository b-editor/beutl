using Beutl.Editor.Services;
using Beutl.Graphics;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.TimelineTab.ViewModels;

public sealed partial class ElementViewModel
{
    public ReadOnlyReactivePropertySlim<string?> MatteIndicator { get; private set; } = null!;

    public void SetMatteSource(int? zIndex)
    {
        MatteMode mode = zIndex == null ? MatteMode.None
            : Model.MatteMode.Value == MatteMode.None ? MatteMode.Alpha : Model.MatteMode.Value;
        Timeline.EditorContext.GetRequiredService<IElementAttributeService>().SetTrackMatte(Model, zIndex, mode);
    }

    public void SetMatteMode(MatteMode mode)
    {
        Timeline.EditorContext.GetRequiredService<IElementAttributeService>()
            .SetTrackMatte(Model, Model.MatteSourceZIndex.Value, mode);
    }

    public void ToggleOutputVisibility()
    {
        Timeline.EditorContext.GetRequiredService<IElementAttributeService>()
            .SetVisibleInOutput(Model, !Model.IsVisibleInOutput.Value);
    }

    private void InitializeTrackMatte()
    {
        MatteIndicator = Model.MatteSourceZIndex.GetObservable()
            .CombineLatest(Model.MatteMode.GetObservable(), Model.ZIndex.GetObservable(),
                (source, mode, own) => source is { } z && mode != MatteMode.None
                    ? $"{(z < own ? "↑" : z > own ? "↓" : "↻")} {z}" : null)
            .ToReadOnlyReactivePropertySlim().DisposeWith(_disposables);
    }
}
