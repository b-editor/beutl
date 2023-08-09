using System.Collections.Specialized;
using System.Numerics;
using System.Reactive.Subjects;
using System.Text.Json.Nodes;

using Avalonia;

using Beutl.Extensibility;
using Beutl.Models;
using Beutl.Operation;
using Beutl.ProjectSystem;
using Beutl.Reactive;
using Beutl.Services.PrimitiveImpls;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels;

public interface ITimelineOptionsProvider
{
    Scene Scene { get; }

    IReactiveProperty<TimelineOptions> Options { get; }

    IObservable<float> Scale { get; }

    IObservable<Vector2> Offset { get; }
}

public sealed class TimelineViewModel : IToolContext
{
    private readonly CompositeDisposable _disposables = new();
    private readonly Subject<LayerHeaderViewModel> _layerHeightChanged = new();

    public TimelineViewModel(EditViewModel editViewModel)
    {
        EditorContext = editViewModel;
        Scene = editViewModel.Scene;
        Player = editViewModel.Player;
        PanelWidth = Scene.GetObservable(Scene.DurationProperty)
            .CombineLatest(editViewModel.Scale)
            .Select(item => item.First.ToPixel(item.Second))
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        SeekBarMargin = Scene.GetObservable(Scene.CurrentFrameProperty)
            .CombineLatest(editViewModel.Scale)
            .Select(item => new Thickness(item.First.ToPixel(item.Second), 0, 0, 0))
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        EndingBarMargin = PanelWidth.Select(p => new Thickness(p, 0, 0, 0))
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        IsCacheEnabled = editViewModel.Scene.GetObservable(Scene.CacheOptionsProperty)
            .Select(v => v.IsEnabled)
            .ToReactiveProperty()
            .AddTo(_disposables);
        IsCacheEnabled.Skip(1)
            .Subscribe(v => Scene.CacheOptions = Scene.CacheOptions with { IsEnabled = v })
            .AddTo(_disposables);

        AddLayer.Subscribe(item =>
        {
            var sLayer = new Element()
            {
                Start = item.Start,
                Length = item.Length,
                ZIndex = item.Layer,
                FileName = Helper.RandomLayerFileName(Path.GetDirectoryName(Scene.FileName)!, Constants.ElementFileExtension)
            };

            if (item.InitialOperator != null)
            {
                //Todo: レイヤーのアクセントカラー
                //sLayer.AccentColor = item.InitialOperator.AccentColor;
                sLayer.Operation.AddChild((SourceOperator)Activator.CreateInstance(item.InitialOperator)!).Do();
            }

            sLayer.Save(sLayer.FileName);
            Scene.AddChild(sLayer).DoAndRecord(CommandRecorder.Default);
        }).AddTo(_disposables);

        LayerHeaders.AddRange(Enumerable.Range(0, 100).Select(num => new LayerHeaderViewModel(num, this)));
        Scene.Children.ForEachItem(
            (idx, item) => Layers.Insert(idx, new ElementViewModel(item, this)),
            (idx, _) =>
            {
                ElementViewModel layer = Layers[idx];
                this.GetService<ISupportCloseAnimation>()?.Close(layer.Model);
                layer.Dispose();
                Layers.RemoveAt(idx);
            },
            () =>
            {
                foreach (ElementViewModel? item in Layers.GetMarshal().Value)
                {
                    item.Dispose();
                }
                Layers.Clear();
            })
            .AddTo(_disposables);

        Header = new ReactivePropertySlim<string>(Strings.Timeline);
    }

    public Scene Scene { get; private set; }

    public PlayerViewModel Player { get; private set; }

    public EditViewModel EditorContext { get; private set; }

    public ReadOnlyReactivePropertySlim<double> PanelWidth { get; }

    public ReactiveProperty<bool> IsCacheEnabled { get; }

    public ReadOnlyReactivePropertySlim<Thickness> SeekBarMargin { get; }

    public ReadOnlyReactivePropertySlim<Thickness> EndingBarMargin { get; }

    public ReactiveCommand<ElementDescription> AddLayer { get; } = new();

    public CoreList<ElementViewModel> Layers { get; } = new();

    public CoreList<InlineAnimationLayerViewModel> Inlines { get; } = new();

    public CoreList<LayerHeaderViewModel> LayerHeaders { get; } = new();

    public ReactiveCommand Paste { get; } = new();

    public TimeSpan ClickedFrame { get; set; }

    public int ClickedLayer { get; set; }

    public IReactiveProperty<TimelineOptions> Options => EditorContext.Options;

    public ToolTabExtension Extension => TimelineTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

    public IReadOnlyReactiveProperty<string> Header { get; }

    public ToolTabExtension.TabPlacement Placement => ToolTabExtension.TabPlacement.Bottom;

    public IObservable<LayerHeaderViewModel> LayerHeightChanged => _layerHeightChanged;

    public void Dispose()
    {
        _disposables.Dispose();
        foreach (ElementViewModel? item in Layers.GetMarshal().Value)
        {
            item.Dispose();
        }
        foreach (LayerHeaderViewModel item in LayerHeaders)
        {
            item.Dispose();
        }
        foreach (InlineAnimationLayerViewModel item in Inlines)
        {
            item.Dispose();
        }

        _layerHeightChanged.Dispose();

        Inlines.Clear();
        LayerHeaders.Clear();
        Layers.Clear();
        Scene = null!;
        Player = null!;
        EditorContext = null!;
    }

    public void ReadFromJson(JsonObject json)
    {
    }

    public void WriteToJson(JsonObject json)
    {
    }

    public void AttachInline(IAbstractAnimatableProperty property, Element layer)
    {
        if (!Inlines.Any(x => x.Layer.Model == layer && x.Property == property)
            && Layers.FirstOrDefault(x => x.Model == layer) is { } viewModel)
        {
            // タイムラインのタブを開く
            Type type = typeof(InlineAnimationLayerViewModel<>).MakeGenericType(property.PropertyType);
            if (Activator.CreateInstance(type, property, this, viewModel) is InlineAnimationLayerViewModel anmTimelineViewModel)
            {
                Inlines.Add(anmTimelineViewModel);
            }
        }
    }

    public void DetachInline(InlineAnimationLayerViewModel item)
    {
        if (item.LayerHeader.Value is { } layerHeader)
        {
            layerHeader.Inlines.Remove(item);
        }

        Inlines.Remove(item);

        item.Dispose();
    }

    public IObservable<double> GetTrackedLayerTopObservable(IObservable<int> layer)
    {
        return new TrackedLayerTopObservable(layer, this);
    }

    public double CalculateLayerTop(int layer)
    {
        double sum = 0;
        for (int i = 0; i < layer; i++)
        {
            sum += LayerHeaders[i].Height.Value;
        }

        return sum;
    }

    public int ToLayerNumber(double pixel)
    {
        double sum = 0;

        for (int i = 0; i < LayerHeaders.Count; i++)
        {
            LayerHeaderViewModel cur = LayerHeaders[i];
            if (sum <= pixel && pixel <= (sum += cur.Height.Value))
            {
                return i;
            }
        }

        return -1;
    }

    public int ToLayerNumber(Thickness thickness)
    {
        double sum = 0;

        for (int i = 0; i < LayerHeaders.Count; i++)
        {
            LayerHeaderViewModel cur = LayerHeaders[i];
            double top = thickness.Top + (Helper.LayerHeight / 2);
            if (sum <= top && top <= (sum += cur.Height.Value))
            {
                return i;
            }
        }

        return -1;
    }

    public bool AnySelected(ElementViewModel? exclude = null)
    {
        foreach (ElementViewModel item in Layers)
        {
            if ((exclude == null || exclude != item) && item.IsSelected.Value)
            {
                return true;
            }
        }

        return false;
    }

    public IEnumerable<ElementViewModel> GetSelected(ElementViewModel? exclude = null)
    {
        foreach (ElementViewModel item in Layers)
        {
            if ((exclude == null || exclude != item) && item.IsSelected.Value)
            {
                yield return item;
            }
        }
    }

    internal void RaiseLayerHeightChanged(LayerHeaderViewModel value)
    {
        _layerHeightChanged.OnNext(value);
    }

    public object? GetService(Type serviceType)
    {
        return EditorContext.GetService(serviceType);
    }

    private sealed class TrackedLayerTopObservable : LightweightObservableBase<double>
    {
        private readonly TimelineViewModel _timeline;
        private readonly IObservable<int> _layerNum;
        private IDisposable? _disposable1;
        private IDisposable? _disposable2;
        private IDisposable? _disposable3;
        private int _prevLayerNum = -1;

        public TrackedLayerTopObservable(IObservable<int> layerNum, TimelineViewModel timeline)
        {
            _layerNum = layerNum;
            _timeline = timeline;
        }

        protected override void Deinitialize()
        {
            _disposable1?.Dispose();
            _disposable2?.Dispose();
            _disposable3?.Dispose();
        }

        protected override void Initialize()
        {
            _disposable1 = _timeline.LayerHeaders.CollectionChangedAsObservable()
                .Subscribe(OnCollectionChanged);

            _disposable2 = _timeline.LayerHeightChanged.Subscribe(OnLayerHeightChanged);

            _disposable3 = _layerNum.Subscribe(OnLayerNumChanged);
        }

        private void OnLayerNumChanged(int obj)
        {
            _prevLayerNum = obj;
            PublishNext(_timeline.CalculateLayerTop(_prevLayerNum));
        }

        private void OnLayerHeightChanged(LayerHeaderViewModel obj)
        {
            if (obj.Number.Value < _prevLayerNum)
            {
                PublishNext(_timeline.CalculateLayerTop(_prevLayerNum));
            }
        }

        protected override void Subscribed(IObserver<double> observer, bool first)
        {
            observer.OnNext(_timeline.CalculateLayerTop(_prevLayerNum));
        }

        private void OnCollectionChanged(NotifyCollectionChangedEventArgs obj)
        {
            if (obj.Action == NotifyCollectionChangedAction.Move)
            {
                if (_prevLayerNum != obj.OldStartingIndex
                    && ((_prevLayerNum > obj.OldStartingIndex && _prevLayerNum <= obj.NewStartingIndex)
                    || (_prevLayerNum < obj.OldStartingIndex && _prevLayerNum >= obj.NewStartingIndex)))
                {
                    PublishNext(_timeline.CalculateLayerTop(_prevLayerNum));
                }
            }
        }
    }
}
