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
        Margin = sceneLayer.GetObservable(SceneLayer.LayerProperty)
            .Select(item => new Thickness(0, item.ToLayerPixel(), 0, 0))
            .ToReactiveProperty()
            .AddTo(_disposables);

        BorderMargin = sceneLayer.GetObservable(SceneLayer.StartProperty)
            .CombineLatest(Scene.GetObservable(Scene.TimelineOptionsProperty))
            .Select(item => new Thickness(item.First.ToPixel(item.Second.Scale), 0, 0, 0))
            .ToReactiveProperty()
            .AddTo(_disposables);

        Width = sceneLayer.GetObservable(SceneLayer.LengthProperty)
            .CombineLatest(Scene.GetObservable(Scene.TimelineOptionsProperty))
            .Select(item => item.First.ToPixel(item.Second.Scale))
            .ToReactiveProperty()
            .AddTo(_disposables);

        Color = sceneLayer.GetObservable(SceneLayer.AccentColorProperty)
            .Select(c => c.ToAvalonia())
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);
    }

    ~TimelineLayerViewModel()
    {
        _disposables.Dispose();
    }

    public SceneLayer Model { get; }

    public Scene Scene => (Scene)Model.Parent!;

    public ReactiveProperty<Thickness> Margin { get; }

    public ReactiveProperty<Thickness> BorderMargin { get; }

    public ReactiveProperty<double> Width { get; }

    public ReadOnlyReactivePropertySlim<Avalonia.Media.Color> Color { get; }

    public void Dispose()
    {
        _disposables.Dispose();
        GC.SuppressFinalize(this);
    }

    public void SyncModelToViewModel()
    {
        float scale = Scene.TimelineOptions.Scale;
        Model.UpdateTime(BorderMargin.Value.Left.ToTimeSpan(scale), Width.Value.ToTimeSpan(scale), CommandRecorder.Default);

        Scene.MoveChild(Margin.Value.ToLayerNumber(), Model, CommandRecorder.Default);
    }
}
