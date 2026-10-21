using System.Collections.Specialized;
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
    private readonly ReactivePropertySlim<TimelineLayer?> _model = new();

    public LayerHeaderViewModel(int num, TimelineTabViewModel timeline)
    {
        Timeline = timeline;
        _model.Value = timeline.Scene.Layers.FirstOrDefault(i => i.ZIndex == num);

        // A header is a fixed row: Number is its index in the timeline's LayerHeaders, and it shows the
        // TimelineLayer whose ZIndex is that row. The timeline has every header look its model up again when a
        // model changes rows or is added or removed, as a layer move and its undo and redo do.
        Number = num;
        Name = _model.Select(i => i?.GetObservable(CoreObject.NameProperty) ?? Observable.Return(num.ToString()))
            .Switch()
            .Select(s => string.IsNullOrEmpty(s) ? $"{num}" : s)
            .ToReactiveProperty($"{num}");
        Color = _model.Select(i =>
                i?.ColorProperty.GetObservable() ?? Observable.ReturnThenNever(Media.Colors.Transparent))
            .Switch()
            .Select(c => c.ToAvaColor())
            .ToReactiveProperty(Colors.Transparent);

        IsLocked = ObserveLayerFlag(layer => layer.IsLockedProperty);
        IsAudioMuted = ObserveLayerFlag(layer => layer.IsAudioMutedProperty);
        IsVideoMuted = ObserveLayerFlag(layer => layer.IsVideoMutedProperty);
        IsSolo = ObserveLayerFlag(layer => layer.IsSoloProperty);

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
    }

    // A missing model reads as false: the flag is off until a TimelineLayer exists for this row.
    private ReactiveProperty<bool> ObserveLayerFlag(Func<TimelineLayer, IProperty<bool>> property)
    {
        return _model.Select(i =>
                i != null ? property(i).GetObservable() : Observable.ReturnThenNever(false))
            .Switch()
            .ToReactiveProperty(false);
    }

    private void OnInlinesCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                ReindexInlines(e.NewStartingIndex);
                break;

            case NotifyCollectionChangedAction.Move:
                ReindexInlines(e.OldStartingIndex);
                ReindexInlines(e.NewStartingIndex);
                break;

            case NotifyCollectionChangedAction.Replace:
            case NotifyCollectionChangedAction.Reset:
                throw new Exception("Not supported action (Replace, Reset).");

            case NotifyCollectionChangedAction.Remove:
                ReindexInlines(e.OldStartingIndex);
                break;
        }
    }

    private void ReindexInlines(int start)
    {
        for (int i = start; i < Inlines.Count; i++)
        {
            InlineAnimationLayerViewModel item = Inlines[i];
            item.Index.Value = i;
        }
    }

    public int Number { get; }

    public TimelineTabViewModel Timeline { get; private set; }

    public ReactivePropertySlim<double> PosY { get; } = new(0);

    public ReactiveProperty<Color> Color { get; }

    public ReactiveProperty<string> Name { get; }

    public ReactiveProperty<bool> IsLocked { get; }

    public ReactiveProperty<bool> IsAudioMuted { get; }

    public ReactiveProperty<bool> IsVideoMuted { get; }

    public ReactiveProperty<bool> IsSolo { get; }

    public ReactiveProperty<int> ItemsCount { get; } = new();

    public ReactiveProperty<double> Height { get; } = new(FrameNumberHelper.LayerHeight);

    public CoreList<InlineAnimationLayerViewModel> Inlines { get; } = new() { ResetBehavior = ResetBehavior.Remove };

    public ReactiveCommand ToggleLockCommand { get; }

    public ReactiveCommand ToggleAudioMuteCommand { get; }

    public ReactiveCommand ToggleVideoMuteCommand { get; }

    public ReactiveCommand ToggleSoloCommand { get; }

    // Moves the layer on this header's row to newLayerNum, together with directElements, the clips on the row that
    // can move. The service moves the clips and the TimelineLayer models in one history entry. The headers stay on
    // their rows and follow the models, so neither the move nor its undo or redo has to change them.
    public LayerMovePlan MoveTo(int newLayerNum, IReadOnlyList<Element> directElements)
    {
        PosY.Value = 0;
        return Timeline.EditorContext.GetRequiredService<ILayerMoveService>()
            .ApplyMove(Timeline.Scene, Number, newLayerNum, directElements);
    }

    public void ElementAdded() => ItemsCount.Value++;

    public void ElementRemoved() => ItemsCount.Value--;

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

    public void SetColor(Color color)
    {
        Timeline.EditorContext.GetRequiredService<ILayerAttributeService>()
            .SetColor(Timeline.Scene, Number, color.ToBtlColor(), Name.Value);

        // Re-sync the local TimelineLayer from the scene so Name/Color bindings track
        // the model the service just created or updated.
        ResyncModel();
    }

    private void ToggleLayerFlag(ReactiveProperty<bool> flag, Func<ILayerAttributeService, Scene, int, bool, bool> apply)
    {
        bool target = !flag.Value;
        ILayerAttributeService service = Timeline.EditorContext.GetRequiredService<ILayerAttributeService>();
        apply(service, Timeline.Scene, Number, target);

        // The service may have materialized or pruned the TimelineLayer; re-sync so
        // the flag's source observable tracks the current model.
        ResyncModel();
    }

    private void ResyncModel()
    {
        _model.Value = Timeline.Scene.Layers.FirstOrDefault(l => l.ZIndex == Number);
    }

    // The timeline passes the model on this header's row whenever the models change rows.
    internal void SetModel(TimelineLayer? model)
    {
        _model.Value = model;
    }
}
