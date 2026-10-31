using Beutl.Editor.Services;
using Beutl.Graphics;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.TimelineTab.ViewModels;

public sealed partial class ElementViewModel
{
    public ReactivePropertySlim<string?> MatteIndicator { get; } = new();

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
        Model.MatteSourceZIndex.GetObservable()
            .CombineLatest(Model.MatteSourceKind.GetObservable(), Model.MatteMode.GetObservable(), Model.ZIndex.GetObservable(),
                (source, kind, mode, own) =>
                {
                    if (mode == MatteMode.None) return null;
                    return kind switch
                    {
                        MatteSourceKind.Above => $"↑ {Strings.MatteSourceAbove}",
                        MatteSourceKind.Below => $"↓ {Strings.MatteSourceBelow}",
                        MatteSourceKind.Layer when source is { } z => $"{(z < own ? "↑" : z > own ? "↓" : "↻")} {z}",
                        _ => null,
                    };
                })
            .Subscribe(v => MatteIndicator.Value = v).DisposeWith(_disposables);
    }
}
