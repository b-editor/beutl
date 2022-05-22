using System.Reactive.Disposables;
using System.Reactive.Linq;

using Avalonia;

using BeUtl.Commands;
using BeUtl.ProjectSystem;

using FluentAvalonia.UI.Media;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels;

public sealed class LayerHeaderViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();

    public LayerHeaderViewModel(int num, TimelineViewModel timeline)
    {
        Number = new(num);
        Timeline = timeline;
        Margin = Number
            .Select(item => new Thickness(0, item.ToLayerPixel(), 0, 0))
            .ToReactiveProperty()
            .AddTo(_disposables);

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
    }

    public ReactiveProperty<int> Number { get; }

    public TimelineViewModel Timeline { get; }

    public ReactiveProperty<Thickness> Margin { get; }

    public ReactiveProperty<Color2> Color { get; } = new();

    public ReactiveProperty<string> Name { get; } = new();

    public ReactiveProperty<bool> IsEnabled { get; } = new(true);

    public ReactiveProperty<int> ItemsCount { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> HasItems { get; }

    public Func<Thickness, CancellationToken, Task> AnimationRequested { get; set; } = (_, _) => Task.CompletedTask;

    public async void AnimationRequest(int layerNum, bool affectModel = true, CancellationToken cancellationToken = default)
    {
        var newMargin = new Thickness(0, layerNum.ToLayerPixel(), 0, 0);
        Thickness oldMargin = Margin.Value;

        if (affectModel)
            Number.Value = layerNum;

        Margin.Value = oldMargin;
        await AnimationRequested(newMargin, cancellationToken);
        Margin.Value = newMargin;
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}
