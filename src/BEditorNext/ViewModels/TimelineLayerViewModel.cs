using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Media;

using BEditorNext.ProjectSystem;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditorNext.ViewModels;

public class TimelineLayerViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();

    public TimelineLayerViewModel(SceneLayer sceneLayer)
    {
        Model = sceneLayer;
        Margin = sceneLayer.GetObservable(SceneLayer.StartProperty)
            .CombineLatest(sceneLayer.GetObservable(SceneLayer.LayerProperty), Scene.GetObservable(Scene.TimelineOptionsProperty))
            .Select(item => new Thickness(item.First.ToPixel(item.Third.Scale), item.Second.ToLayerPixel(), 0, 0))
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        Width = sceneLayer.GetObservable(SceneLayer.LengthProperty)
            .CombineLatest(Scene.GetObservable(Scene.TimelineOptionsProperty))
            .Select(item => item.First.ToPixel(item.Second.Scale))
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        Color = sceneLayer.GetObservable(SceneLayer.AccentColorProperty)
            .Select(c => c.ToAvalonia())
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);
    }

    public SceneLayer Model { get; }

    public Scene Scene => (Scene)Model.Parent!;

    public ReadOnlyReactivePropertySlim<Thickness> Margin { get; }

    public ReadOnlyReactivePropertySlim<double> Width { get; }

    public ReadOnlyReactivePropertySlim<Avalonia.Media.Color> Color { get; }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}
