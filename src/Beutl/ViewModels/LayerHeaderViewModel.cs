using System.Collections.Specialized;

using Avalonia.Media;

using Beutl.Commands;
using Beutl.ProjectSystem;
using Beutl.Reactive;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels;

public sealed class LayerHeaderViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();

    public LayerHeaderViewModel(int num, TimelineViewModel timeline)
    {
        Number = new(num);
        Timeline = timeline;

        HasItems = ItemsCount.Select(i => i > 0)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        IsEnabled.Subscribe(b =>
        {
            IRecordableCommand? command = null;
            foreach (Element? item in Timeline.Scene.Children.Where(i => i.ZIndex == Number.Value))
            {
                if (item.IsEnabled != b)
                {
                    var command2 = new ChangePropertyCommand<bool>(item, Element.IsEnabledProperty, b, item.IsEnabled);
                    if (command == null)
                    {
                        command = command2;
                    }
                    else
                    {
                        command = command.Append(command2);
                    }
                }
            }

            command?.DoAndRecord(CommandRecorder.Default);
        }).DisposeWith(_disposables);

        Height.Subscribe(_ => Timeline.RaiseLayerHeightChanged(this)).DisposeWith(_disposables);

#if DEBUG
        Name = Number.Select(x => x.ToString()).ToReactiveProperty()!;
#endif
        Inlines.ForEachItem(
            (idx, x) =>
            {
                Height.Value += Helper.LayerHeight;
                x.Index.Value = idx;
            },
            (_, x) =>
            {
                Height.Value -= Helper.LayerHeight;
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

    public ReactiveProperty<double> Height { get; } = new(Helper.LayerHeight);

    public CoreList<InlineAnimationLayerViewModel> Inlines { get; } = new() { ResetBehavior = ResetBehavior.Remove };

    public void AnimationRequest(int layerNum, bool affectModel = true)
    {
        if (affectModel)
            Number.Value = layerNum;

        //await AnimationRequested(0, cancellationToken);
        PosY.Value = 0;
    }

    public void Dispose()
    {
        _disposables.Dispose();
        Inlines.Clear();
        Timeline = null!;
    }

    public double CalculateInlineTop(int index)
    {
        return Helper.LayerHeight * index;
    }
}
