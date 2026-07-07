using System.Collections.Specialized;
using System.Text.Json.Nodes;
using Avalonia.Media;
using Beutl.Controls;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.TimelineTab.ViewModels;

public sealed class LayerHeaderViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = [];
    private readonly List<ElementViewModel> _elements = [];
    private readonly ReactivePropertySlim<TimelineLayer?> _model = new();
    private IDisposable? _elementsSubscription;
    private bool _skipSubscription;

    public LayerHeaderViewModel(int num, TimelineTabViewModel timeline)
    {
        Timeline = timeline;
        _model.Value = timeline.Scene.Layers.FirstOrDefault(i => i.ZIndex == num);

        // Never (not ReturnThenNever(num)): when the model is pruned after this
        // header moved rows, re-emitting the construction-time num would snap
        // Number back to the original row.
        Number = _model.Select(i => i?.GetObservable(TimelineLayer.ZIndexProperty) ?? Observable.Never<int>())
            .Switch()
            .ToReactiveProperty(num);
        Name = _model.Select(i => i?.GetObservable(CoreObject.NameProperty) ?? Number.Select(n => n.ToString()))
            .Switch()
            .Select(s => string.IsNullOrEmpty(s) ? $"{Number.Value}" : s)
            .ToReactiveProperty($"{num}");
        Color = _model.Select(i =>
                i?.GetObservable(TimelineLayer.ColorProperty) ?? Observable.ReturnThenNever(Media.Colors.Transparent))
            .Switch()
            .Select(c => c.ToAvaColor())
            .ToReactiveProperty(Colors.Transparent);

        IsLocked = _model.Select(i =>
                i?.GetObservable(TimelineLayer.IsLockedProperty) ?? Observable.ReturnThenNever(false))
            .Switch()
            .ToReactiveProperty(false);
        IsAudioMuted = _model.Select(i =>
                i?.GetObservable(TimelineLayer.IsAudioMutedProperty) ?? Observable.ReturnThenNever(false))
            .Switch()
            .ToReactiveProperty(false);
        IsVideoMuted = _model.Select(i =>
                i?.GetObservable(TimelineLayer.IsVideoMutedProperty) ?? Observable.ReturnThenNever(false))
            .Switch()
            .ToReactiveProperty(false);
        IsSolo = _model.Select(i =>
                i?.GetObservable(TimelineLayer.IsSoloProperty) ?? Observable.ReturnThenNever(false))
            .Switch()
            .ToReactiveProperty(false);

        HasItems = ItemsCount.Select(i => i > 0)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        SwitchEnabledCommand = new ReactiveCommand()
            .WithSubscribe(() =>
            {
                try
                {
                    _skipSubscription = true;
                    bool target = !IsEnabled.Value;
                    IsEnabled.Value = target;
                    Timeline.EditorContext.GetRequiredService<ILayerAttributeService>()
                        .SetEnabled(Timeline.Scene, Number.Value, target);
                }
                finally
                {
                    _skipSubscription = false;
                }
            });

        ToggleLockCommand = new ReactiveCommand()
            .WithSubscribe(() => ToggleLayerFlag(IsLocked, (s, scene, n, v) => s.SetLocked(scene, n, v)));
        ToggleAudioMuteCommand = new ReactiveCommand()
            .WithSubscribe(() => ToggleLayerFlag(IsAudioMuted, (s, scene, n, v) => s.SetAudioMuted(scene, n, v)));
        ToggleVideoMuteCommand = new ReactiveCommand()
            .WithSubscribe(() => ToggleLayerFlag(IsVideoMuted, (s, scene, n, v) => s.SetVideoMuted(scene, n, v)));
        ToggleSoloCommand = new ReactiveCommand()
            .WithSubscribe(() => ToggleLayerFlag(IsSolo, (s, scene, n, v) => s.SetSolo(scene, n, v)));

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

        // Undo/redo inserts or removes TimelineLayer models directly in
        // Scene.Layers (e.g. restoring a pruned model), which no command-side
        // resync covers — track the collection so the header rebinds.
        Timeline.Scene.Layers.CollectionChangedAsObservable()
            .Subscribe(_ => _model.Value = Timeline.Scene.Layers.FirstOrDefault(l => l.ZIndex == Number.Value))
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

    public TimelineTabViewModel Timeline { get; private set; }

    public ReactivePropertySlim<double> PosY { get; } = new(0);

    public ReactiveProperty<Color> Color { get; }

    public ReactiveProperty<string> Name { get; }

    public ReactiveProperty<bool> IsEnabled { get; } = new(true);

    public ReactiveProperty<bool> IsLocked { get; }

    public ReactiveProperty<bool> IsAudioMuted { get; }

    public ReactiveProperty<bool> IsVideoMuted { get; }

    public ReactiveProperty<bool> IsSolo { get; }

    public ReactiveProperty<int> ItemsCount { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> HasItems { get; }

    public ReactiveProperty<double> Height { get; } = new(FrameNumberHelper.LayerHeight);

    public CoreList<InlineAnimationLayerViewModel> Inlines { get; } = new() { ResetBehavior = ResetBehavior.Remove };

    public ReactiveCommand SwitchEnabledCommand { get; }

    public ReactiveCommand ToggleLockCommand { get; }

    public ReactiveCommand ToggleAudioMuteCommand { get; }

    public ReactiveCommand ToggleVideoMuteCommand { get; }

    public ReactiveCommand ToggleSoloCommand { get; }

    public void UpdateZIndex(int layerNum)
    {
        // For model-backed headers, LayerMoveService already wrote ZIndex (in its
        // committed transaction) and Number reflects it; re-touching the model here
        // would leak into the next history entry and double-apply the shift. Only a
        // model-less header needs its Number pushed manually.
        if (_model.Value is null)
        {
            Number.Value = layerNum;
        }

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
            GetOrCreateModel().Color = color.ToBtlColor();
        }
    }

    public void SetColor(Color color)
    {
        Timeline.EditorContext.GetRequiredService<ILayerAttributeService>()
            .SetColor(Timeline.Scene, Number.Value, color.ToBtlColor(), Name.Value);

        // Re-sync the local TimelineLayer from the scene so Name/Color bindings track
        // the model the service just created or updated.
        _model.Value = Timeline.Scene.Layers.FirstOrDefault(l => l.ZIndex == Number.Value);
    }

    private void ToggleLayerFlag(ReactiveProperty<bool> flag, Func<ILayerAttributeService, Scene, int, bool, bool> apply)
    {
        bool target = !flag.Value;
        ILayerAttributeService service = Timeline.EditorContext.GetRequiredService<ILayerAttributeService>();
        apply(service, Timeline.Scene, Number.Value, target);

        // The service may have materialized or pruned the TimelineLayer; re-sync so
        // the flag's source observable tracks the current model.
        _model.Value = Timeline.Scene.Layers.FirstOrDefault(l => l.ZIndex == Number.Value);
    }

    private TimelineLayer GetOrCreateModel()
    {
        if (_model.Value != null) return _model.Value;

        _model.Value = new TimelineLayer { Name = Name.Value, Color = Color.Value.ToBtlColor(), ZIndex = Number.Value };
        Timeline.Scene.Layers.Add(_model.Value);
        return _model.Value;
    }
}
