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

    public LayerHeaderViewModel(int num, TimelineViewModel timeline)
    {
        Number = new(num);
        Timeline = timeline;
        Name.Value = num.ToString();

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
        new SetColorCommand(this, color)
            .DoAndRecord(CommandRecorder.Default);
    }

    private sealed class SetColorCommand(LayerHeaderViewModel viewModel, Color color) : IRecordableCommand
    {
        public void Do()
        {
            (color, viewModel.Color.Value) = (viewModel.Color.Value, color);
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            Do();
        }
    }
}
