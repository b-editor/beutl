using System.Numerics;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using Avalonia;

using BeUtl.Collections;
using BeUtl.Framework;
using BeUtl.Models;
using BeUtl.ProjectSystem;
using BeUtl.Services.PrimitiveImpls;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels;

public interface ITimelineOptionsProvider
{
    Scene Scene { get; }

    IReactiveProperty<TimelineOptions> Options { get; }

    IObservable<float> Scale { get; }

    IObservable<Vector2> Offset { get; }
}

public sealed class TimelineViewModel : IDisposable, ITimelineOptionsProvider, IToolContext
{
    private readonly CompositeDisposable _disposables = new();

    public TimelineViewModel(EditViewModel editViewModel)
    {
        EditorContext = editViewModel;
        Scene = editViewModel.Scene;
        Player = editViewModel.Player;
        Scale = Options.Select(o => o.Scale);
        Offset = Options.Select(o => o.Offset);
        PanelWidth = Scene.GetObservable(Scene.DurationProperty)
            .CombineLatest(Scale)
            .Select(item => item.First.ToPixel(item.Second))
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        SeekBarMargin = Scene.GetObservable(Scene.CurrentFrameProperty)
            .CombineLatest(Scale)
            .Select(item => new Thickness(item.First.ToPixel(item.Second), 0, 0, 0))
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
                FileName = Helper.RandomLayerFileName(Path.GetDirectoryName(Scene.FileName)!, Constants.LayerFileExtension)
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

        LayerHeaders.AddRange(Enumerable.Range(0, 100).Select(num => new LayerHeaderViewModel(num, this)));
        Scene.Children.ForEachItem(
            (idx, item) => Layers.Insert(idx, new TimelineLayerViewModel(item, this)),
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

        Header = StringResources.Common.TimelineObservable
            .ToReadOnlyReactivePropertySlim(StringResources.Common.Timeline)
            .AddTo(_disposables);
    }

    public Scene Scene { get; }

    public PlayerViewModel Player { get; }

    public EditViewModel EditorContext { get; }

    public ReadOnlyReactivePropertySlim<double> PanelWidth { get; }

    public ReadOnlyReactivePropertySlim<Thickness> SeekBarMargin { get; }

    public ReadOnlyReactivePropertySlim<Thickness> EndingBarMargin { get; }

    public ReactiveCommand<LayerDescription> AddLayer { get; } = new();

    public CoreList<TimelineLayerViewModel> Layers { get; } = new();

    public CoreList<LayerHeaderViewModel> LayerHeaders { get; } = new();

    public ReactiveCommand Paste { get; } = new();

    public TimeSpan ClickedFrame { get; set; }

    public int ClickedLayer { get; set; }

    public IReactiveProperty<TimelineOptions> Options { get; } = new ReactiveProperty<TimelineOptions>(new TimelineOptions());

    public IObservable<float> Scale { get; }

    public IObservable<Vector2> Offset { get; }

    public ToolTabExtension Extension => TimelineTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

    public IReadOnlyReactiveProperty<string> Header { get; }

    public ToolTabExtension.TabPlacement Placement => ToolTabExtension.TabPlacement.Bottom;

    public void Dispose()
    {
        _disposables.Dispose();
        foreach (TimelineLayerViewModel? item in Layers.AsSpan())
        {
            item.Dispose();
        }
    }
}
