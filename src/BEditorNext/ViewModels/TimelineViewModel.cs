using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

using Avalonia;

using BEditorNext.ProjectSystem;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditorNext.ViewModels;

public class TimelineViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();
    public record struct LayerDescription(TimeSpan Start, TimeSpan Length, int Layer, RenderOperationRegistry.RegistryItem? InitialOperation = null);

    public TimelineViewModel(Scene scene)
    {
        Scene = scene;
        PanelWidth = scene.GetObservable(Scene.DurationProperty)
            .CombineLatest(scene.GetObservable(Scene.TimelineOptionsProperty))
            .Select(item => item.First.ToPixel(item.Second.Scale))
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        SeekBarMargin = scene.GetObservable(Scene.CurrentFrameProperty)
            .CombineLatest(scene.GetObservable(Scene.TimelineOptionsProperty))
            .Select(item => new Thickness(item.First.ToPixel(item.Second.Scale), 0, 0, 0))
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        EndingBarMargin = PanelWidth.Select(p => new Thickness(p, 0, 0, 0))
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        AddLayer.Subscribe(item =>
        {
            var sLayer = new SceneLayer(Scene)
            {
                Start = item.Start,
                Length = item.Length
            };

            if(item.InitialOperation != null)
            {
                sLayer.AddChild((RenderOperation)(Activator.CreateInstance(item.InitialOperation.Type)!));
            }

            Scene.InsertChild(item.Layer, sLayer);
        }).AddTo(_disposables);
    }

    public Scene Scene { get; }

    public ReadOnlyReactivePropertySlim<double> PanelWidth { get; }

    public ReadOnlyReactivePropertySlim<Thickness> SeekBarMargin { get; }

    public ReadOnlyReactivePropertySlim<Thickness> EndingBarMargin { get; }

    public ReactiveCommand<LayerDescription> AddLayer { get; } = new();

    public void Dispose()
    {
        _disposables.Dispose();
    }
}
