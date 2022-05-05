using System.Reactive.Disposables;
using System.Reactive.Linq;

using Avalonia;

using BeUtl.Collections;
using BeUtl.Models;
using BeUtl.ProjectSystem;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels;

//public interface ITimelineOptionsProvider
//{
//    public IReactiveProperty<float> Scale { get; }

//    public IReactiveProperty<System.Numerics.Vector2> Offset { get; }
//}

public sealed class TimelineViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();

    public TimelineViewModel(Scene scene, PlayerViewModel player)
    {
        Scene = scene;
        Player = player;
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
            var sLayer = new Layer()
            {
                Start = item.Start,
                Length = item.Length,
                ZIndex = item.Layer,
                FileName = Helper.RandomLayerFileName(Path.GetDirectoryName(Scene.FileName)!, "layer")
            };

            if (item.InitialOperation != null)
            {
                sLayer.AccentColor = item.InitialOperation.AccentColor;
                sLayer.AddChild((LayerOperation)(Activator.CreateInstance(item.InitialOperation.Type)!))
                    .DoAndRecord(CommandRecorder.Default);
            }

            sLayer.Save(sLayer.FileName);
            Scene.AddChild(sLayer).DoAndRecord(CommandRecorder.Default);
        }).AddTo(_disposables);

        scene.Children.ForEachItem(
            (idx, item) => Layers.Insert(idx, new TimelineLayerViewModel(item)),
            (idx, _) =>
            {
                Layers[idx].Dispose();
                Layers.RemoveAt(idx);
            },
            () =>
            {
                foreach (TimelineLayerViewModel? item in Layers.AsSpan())
                {
                    item.Dispose();
                }
                Layers.Clear();
            })
            .AddTo(_disposables);
    }

    public Scene Scene { get; }

    public PlayerViewModel Player { get; }

    public ReadOnlyReactivePropertySlim<double> PanelWidth { get; }

    public ReadOnlyReactivePropertySlim<Thickness> SeekBarMargin { get; }

    public ReadOnlyReactivePropertySlim<Thickness> EndingBarMargin { get; }

    public ReactiveCommand<LayerDescription> AddLayer { get; } = new();

    public CoreList<TimelineLayerViewModel> Layers { get; } = new();

    public ReactiveCommand Paste { get; } = new();

    public TimeSpan ClickedFrame { get; set; }

    public int ClickedLayer { get; set; }

    public void Dispose()
    {
        _disposables.Dispose();
        foreach (TimelineLayerViewModel? item in Layers.AsSpan())
        {
            item.Dispose();
        }
    }
}
