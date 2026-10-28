using Beutl.Graphics;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.TimelineTab.ViewModels;

public sealed partial class ElementViewModel
{
    public ReadOnlyReactivePropertySlim<string?> MatteIndicator { get; private set; } = null!;

    private void InitializeTrackMatte()
    {
        MatteIndicator = Model.MatteSourceZIndex.GetObservable()
            .CombineLatest(Model.MatteMode.GetObservable(), Model.ZIndex.GetObservable(),
                (source, mode, own) => source is { } z && mode != MatteMode.None
                    ? $"{(z < own ? "↑" : z > own ? "↓" : "↻")} {z}" : null)
            .ToReadOnlyReactivePropertySlim().DisposeWith(_disposables);
    }
}
