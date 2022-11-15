using System.Collections.Specialized;

using Avalonia.Media;

using Beutl.Commands;
using Beutl.ProjectSystem;

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
            .AddTo(_disposables);

        IsEnabled.Subscribe(b =>
        {
            IRecordableCommand? command = null;
            foreach (Layer? item in Timeline.Scene.Children.Where(i => i.ZIndex == Number.Value))
            {
                if (item.IsEnabled != b)
                {
                    var command2 = new ChangePropertyCommand<bool>(item, Layer.IsEnabledProperty, b, item.IsEnabled);
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
        }).AddTo(_disposables);

        Height.Subscribe(_ => Timeline.RaiseLayerHeightChanged(this)).AddTo(_disposables);

#if DEBUG
        Name = Number.Select(x => x.ToString()).ToReactiveProperty()!;
#endif
        Inlines.ForEachItem(
            x =>
            {
                x.HeightChanged += OnInlineItemHeightChanged;
                Height.Value += x.Height;
            },
            x =>
            {
                x.HeightChanged -= OnInlineItemHeightChanged;
                Height.Value -= x.Height;
            },
            () => { }).AddTo(_disposables);
    }

    private void OnInlineItemHeightChanged(object? sender, (double OldHeight, double NewHeight) e)
    {
        Height.Value += e.NewHeight - e.OldHeight;
    }

    public ReactiveProperty<int> Number { get; }

    public TimelineViewModel Timeline { get; }

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
    }
}
