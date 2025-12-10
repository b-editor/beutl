using System.Collections.Specialized;
using System.Text.Json.Nodes;
using Avalonia.Media;
using Beutl.Editor;
using Beutl.ProjectSystem;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels;

public sealed class LayerHeaderViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = [];
    private readonly List<ElementViewModel> _elements = [];
    private readonly ReactivePropertySlim<TimelineLayer?> _model = new();
    private IDisposable? _elementsSubscription;
    private bool _skipSubscription;

    public LayerHeaderViewModel(int num, TimelineViewModel timeline)
    {
        Timeline = timeline;
        _model.Value = timeline.Scene.Layers.FirstOrDefault(i => i.ZIndex == num);

        Number = _model.Select(i => i?.GetObservable(TimelineLayer.ZIndexProperty) ?? Observable.ReturnThenNever(num))
            .Switch()
            .ToReactiveProperty();
        Name = _model.Select(i => i?.GetObservable(CoreObject.NameProperty) ?? Observable.ReturnThenNever($"{num}"))
            .Switch()
            .Select(s => string.IsNullOrEmpty(s) ? $"{num}" : s)
            .ToReactiveProperty($"{num}");
        Color = _model.Select(i =>
                i?.GetObservable(TimelineLayer.ColorProperty) ?? Observable.ReturnThenNever(Media.Colors.Transparent))
            .Switch()
            .Select(c => c.ToAvalonia())
            .ToReactiveProperty(Colors.Transparent);

        HasItems = ItemsCount.Select(i => i > 0)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        SwitchEnabledCommand = new ReactiveCommand()
            .WithSubscribe(() =>
            {
                HistoryManager history = Timeline.EditorContext.HistoryManager;
                try
                {
                    _skipSubscription = true;
                    IsEnabled.Value = !IsEnabled.Value;
                    foreach (Element element in Timeline.Scene.Children.Where(i =>
                                 i.ZIndex == Number.Value && i.IsEnabled != IsEnabled.Value))
                    {
                        element.IsEnabled = IsEnabled.Value;
                    }

                    history.Commit(CommandNames.ChangeLayerEnabled);
                }
                finally
                {
                    _skipSubscription = false;
                }
            });

        Height.Subscribe(_ => Timeline.RaiseLayerHeightChanged(this)).DisposeWith(_disposables);

        Inlines.ForEachItem(
                (idx, x) =>
                {
                    Height.Value += FrameNumberHelper.LayerHeight;
                    x.Index.Value = idx;
                },
                (_, x) =>
                {
                    Height.Value -= FrameNumberHelper.LayerHeight;
                    x.Index.Value = -1;
                },
                () => { })
            .DisposeWith(_disposables);

        Inlines.CollectionChangedAsObservable()
            .Subscribe(OnInlinesCollectionChanged)
            .AddTo(_disposables);
    }

    private void OnInlinesCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        void OnAdded()
        {
            for (int i = e.NewStartingIndex; i < Inlines.Count; i++)
            {
                InlineAnimationLayerViewModel item = Inlines[i];
                item.Index.Value = i;
            }
        }

        void OnRemoved()
        {
            for (int i = e.OldStartingIndex; i < Inlines.Count; i++)
            {
                InlineAnimationLayerViewModel item = Inlines[i];
                item.Index.Value = i;
            }
        }

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                OnAdded();
                break;

            case NotifyCollectionChangedAction.Move:
                OnRemoved();
                OnAdded();
                break;

            case NotifyCollectionChangedAction.Replace:
            case NotifyCollectionChangedAction.Reset:
                throw new Exception("Not supported action (Move, Replace, Reset).");

            case NotifyCollectionChangedAction.Remove:
                OnRemoved();
                break;
        }
    }

    public ReactiveProperty<int> Number { get; }

    public TimelineViewModel Timeline { get; private set; }

    public ReactivePropertySlim<double> PosY { get; } = new(0);

    public ReactiveProperty<Color> Color { get; }

    public ReactiveProperty<string> Name { get; }

    public ReactiveProperty<bool> IsEnabled { get; } = new(true);

    public ReactiveProperty<int> ItemsCount { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> HasItems { get; }

    public ReactiveProperty<double> Height { get; } = new(FrameNumberHelper.LayerHeight);

    public CoreList<InlineAnimationLayerViewModel> Inlines { get; } = new() { ResetBehavior = ResetBehavior.Remove };

    public ReactiveCommand SwitchEnabledCommand { get; }

    public void UpdateZIndex(int layerNum)
    {
        Number.Value = layerNum;
        _model.Value?.ZIndex = layerNum;

        PosY.Value = 0;
    }

    public void ElementAdded(ElementViewModel element)
    {
        ItemsCount.Value++;
        _elements.Add(element);
        BuildSubscription();
    }

    public void ElementRemoved(ElementViewModel element)
    {
        ItemsCount.Value--;
        _elements.Remove(element);
        BuildSubscription();
    }

    private void BuildSubscription()
    {
        _elementsSubscription?.Dispose();
        _elementsSubscription = null;
        if (_elements.Count == 0)
        {
            IsEnabled.Value = true;
            return;
        }

        _elementsSubscription = _elements.Select(obj => obj.IsEnabled.Select(b => (bool?)b))
            .Aggregate((x, y) => x.CombineLatest(y)
                .Select(t => t.First == t.Second ? t.First : null))
            .Where(b => b.HasValue && !_skipSubscription)
            .Subscribe(b => IsEnabled.Value = b!.Value);
    }

    public void Dispose()
    {
        _disposables.Dispose();
        Inlines.Clear();
        Timeline = null!;
    }

    public double CalculateInlineTop(int index)
    {
        return FrameNumberHelper.LayerHeight * index;
    }

    public void ReadFromJson(JsonObject obj)
    {
        if (obj.TryGetPropertyValueAsJsonValue(nameof(Name), out string? name))
        {
            GetOrCreateModel().Name = name;
        }

        if (obj.TryGetPropertyValueAsJsonValue(nameof(Color), out string? colorStr)
            && Avalonia.Media.Color.TryParse(colorStr, out Color color))
        {
            GetOrCreateModel().Color = color.ToMedia();
        }
    }

    public void SetColor(Color color)
    {
        HistoryManager history = Timeline.EditorContext.HistoryManager;
        var model = GetOrCreateModel();
        model.Color = color.ToMedia();
        history.Commit(CommandNames.ChangeLayerColor);
    }

    private TimelineLayer GetOrCreateModel()
    {
        if (_model.Value != null) return _model.Value;

        _model.Value = new TimelineLayer
        {
            Name = Name.Value, Color = Color.Value.ToMedia(), ZIndex = Number.Value
        };
        Timeline.Scene.Layers.Add(_model.Value);
        return _model.Value;
    }
}
