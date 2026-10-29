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
        MatteIndicator = Observable.FromEventPattern(h => Scene.Edited += h, h => Scene.Edited -= h)
            .Select(_ => Timeline.CurrentTime.Value)
            .Merge(Timeline.CurrentTime)
            .ObserveOnUIDispatcher()
            .Select(time =>
            {
                if (Model.MatteMode.Value == MatteMode.None) return null;
                if (Model.GetMatteSourceZIndex(Scene, time) is { } z)
                    return $"{(z < Model.ZIndex.Value ? "↑" : z > Model.ZIndex.Value ? "↓" : "↻")} {z}";
                return Model.MatteSourceKind.Value switch
                {
                    MatteSourceKind.Above => "↑",
                    MatteSourceKind.Below => "↓",
                    _ => null,
                };
            })
            .ToReadOnlyReactivePropertySlim().DisposeWith(_disposables);
    }
}
