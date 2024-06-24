using System.Collections.Specialized;
using System.Text.Json.Nodes;
using Avalonia.Media;
using Beutl.Commands;
using Beutl.ProjectSystem;
using Beutl.Reactive;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels;

public sealed class LayerHeaderViewModel : IDisposable, IJsonSerializable
{
    private readonly CompositeDisposable _disposables = [];
    private readonly List<ElementViewModel> _elements = [];
    private IDisposable? _elementsSubscription;
    private bool _skipSubscription;

    public LayerHeaderViewModel(int num, TimelineViewModel timeline)
    {
        Number = new(num);
        Timeline = timeline;
        Name.Value = num.ToString();

        HasItems = ItemsCount.Select(i => i > 0)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        SwitchEnabledCommand = new ReactiveCommand()
            .WithSubscribe(() =>
            {
                CommandRecorder recorder = Timeline.EditorContext.CommandRecorder;
                try
                {
                    _skipSubscription = true;
                    IsEnabled.Value = !IsEnabled.Value;
                    Timeline.Scene.Children.Where(i => i.ZIndex == Number.Value && i.IsEnabled != IsEnabled.Value)
                        .Select(item => RecordableCommands.Edit(item, Element.IsEnabledProperty, IsEnabled.Value).WithStoables([item]))
                        .ToArray()
                        .ToCommand()
                        .DoAndRecord(recorder);
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

    public ReactiveProperty<Color> Color { get; } = new();

    public ReactiveProperty<string> Name { get; } = new();

    public ReactiveProperty<bool> IsEnabled { get; } = new(true);

    public ReactiveProperty<int> ItemsCount { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> HasItems { get; }

    public ReactiveProperty<double> Height { get; } = new(FrameNumberHelper.LayerHeight);

    public CoreList<InlineAnimationLayerViewModel> Inlines { get; } = new() { ResetBehavior = ResetBehavior.Remove };

    public ReactiveCommand SwitchEnabledCommand { get; }

    public void AnimationRequest(int layerNum, bool affectModel = true)
    {
        if (affectModel)
            Number.Value = layerNum;

        //await AnimationRequested(0, cancellationToken);
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

    public bool ShouldSaveState()
    {
        return Number.Value.ToString() != Name.Value;
    }

    public void WriteToJson(JsonObject obj)
    {
        obj[nameof(Name)] = Name.Value;
        obj[nameof(Color)] = Color.Value.ToString();
    }

    public void ReadFromJson(JsonObject obj)
    {
        if (obj.TryGetPropertyValueAsJsonValue(nameof(Name), out string? name))
        {
            Name.Value = name;
        }

        if (obj.TryGetPropertyValueAsJsonValue(nameof(Color), out string? colorStr)
            && Avalonia.Media.Color.TryParse(colorStr, out Color color))
        {
            Color.Value = color;
        }
    }

    public void SetColor(Color color)
    {
        CommandRecorder recorder = Timeline.EditorContext.CommandRecorder;
        var (newValue, oldValue) = (Color.Value, color);
        RecordableCommands.Create()
            .OnDo(() => Color.Value = newValue)
            .OnUndo(() => Color.Value = oldValue)
            .ToCommand()
            .DoAndRecord(recorder);
    }
}
