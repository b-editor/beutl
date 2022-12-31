using System.Text.Json.Nodes;

using Avalonia;
using Avalonia.Input;
using Avalonia.Input.Platform;

using Beutl.Models;
using Beutl.ProjectSystem;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels;

public sealed class TimelineLayerViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();

    public TimelineLayerViewModel(Layer sceneLayer, TimelineViewModel timeline)
    {
        Model = sceneLayer;
        Timeline = timeline;

        IObservable<int> zIndexSubject = sceneLayer.GetObservable(Layer.ZIndexProperty);
        Margin = Timeline.GetTrackedLayerTopObservable(zIndexSubject)
            .Select(item => new Thickness(0, item, 0, 0))
            .ToReactiveProperty()
            .AddTo(_disposables);

        BorderMargin = sceneLayer.GetObservable(Layer.StartProperty)
            .CombineLatest(timeline.EditorContext.Scale)
            .Select(item => new Thickness(item.First.ToPixel(item.Second), 0, 0, 0))
            .ToReactiveProperty()
            .AddTo(_disposables);

        Width = sceneLayer.GetObservable(Layer.LengthProperty)
            .CombineLatest(timeline.EditorContext.Scale)
            .Select(item => item.First.ToPixel(item.Second))
            .ToReactiveProperty()
            .AddTo(_disposables);

        Color = sceneLayer.GetObservable(Layer.AccentColorProperty)
            .Select(c => c.ToAvalonia())
            .ToReactiveProperty()
            .AddTo(_disposables);

        Split.Where(func => func != null).Subscribe(func =>
        {
            int rate = Scene.Parent is Project proj ? proj.GetFrameRate() : 30;
            TimeSpan absTime = func!().RoundToRate(rate);
            TimeSpan forwardLength = absTime - Model.Start;
            TimeSpan backwardLength = Model.Length - forwardLength;

            JsonNode jsonNode = new JsonObject();
            Model.WriteToJson(ref jsonNode);
            string json = jsonNode.ToJsonString(JsonHelper.SerializerOptions);
            var backwardLayer = new Layer();
            backwardLayer.ReadFromJson(JsonNode.Parse(json)!);

            Scene.MoveChild(Model.ZIndex, Model.Start, forwardLength, Model).DoAndRecord(CommandRecorder.Default);
            backwardLayer.Start = absTime;
            backwardLayer.Length = backwardLength;

            backwardLayer.Save(Helper.RandomLayerFileName(Path.GetDirectoryName(Scene.FileName)!, Constants.LayerFileExtension));
            Scene.AddChild(backwardLayer).DoAndRecord(CommandRecorder.Default);
        });

        Cut.Subscribe(async () =>
        {
            if (await SetClipboard())
            {
                Exclude.Execute();
            }
        });

        Copy.Subscribe(async () => await SetClipboard());

        Exclude.Subscribe(() => Scene.RemoveChild(Model).DoAndRecord(CommandRecorder.Default));

        Delete.Subscribe(() =>
        {
            Scene.RemoveChild(Model).Do();
            if (File.Exists(Model.FileName))
            {
                File.Delete(Model.FileName);
            }
        });

        Color.Subscribe(c => Model.AccentColor = Media.Color.FromArgb(c.A, c.R, c.G, c.B))
            .AddTo(_disposables);

        FinishEditingAnimation.Subscribe(() =>
        {
            foreach (InlineAnimationLayerViewModel item in Timeline.Inlines.Where(x => x.Layer == this).ToArray())
            {
                Timeline.DetachInline(item);
            }
        });

        BringAnimationToTop.Subscribe(() =>
        {
            if (LayerHeader.Value is { } layerHeader)
            {
                var inlines = Timeline.Inlines.Where(x => x.Layer == this).ToArray();
                Array.Sort(inlines, (x, y) => x.Index.Value - y.Index.Value);

                for (int i = 0; i < inlines.Length; i++)
                {
                    InlineAnimationLayerViewModel? item = inlines[i];
                    int oldIndex = layerHeader.Inlines.IndexOf(item);
                    if (oldIndex >= 0)
                    {
                        layerHeader.Inlines.Move(oldIndex, i);
                    }
                }
            }
        });

        zIndexSubject.Subscribe(number =>
        {
            LayerHeaderViewModel? newLH = Timeline.LayerHeaders.FirstOrDefault(i => i.Number.Value == number);

            if (LayerHeader.Value != null)
                LayerHeader.Value.ItemsCount.Value--;

            if (newLH != null)
                newLH.ItemsCount.Value++;
            LayerHeader.Value = newLH;
        }).AddTo(_disposables);
    }

    ~TimelineLayerViewModel()
    {
        _disposables.Dispose();
    }

    public Func<(Thickness Margin, Thickness BorderMargin, double Width), CancellationToken, Task> AnimationRequested { get; set; } = (_, _) => Task.CompletedTask;

    public TimelineViewModel Timeline { get; }

    public Layer Model { get; }

    public Scene Scene => (Scene)Model.Parent!;

    public ReactiveProperty<Thickness> Margin { get; }

    public ReactiveProperty<Thickness> BorderMargin { get; }

    public ReactiveProperty<double> Width { get; }

    public ReactivePropertySlim<LayerHeaderViewModel?> LayerHeader { get; set; } = new();

    public ReactiveProperty<Avalonia.Media.Color> Color { get; }

    public ReactiveCommand<Func<TimeSpan>?> Split { get; } = new();

    public ReactiveCommand Cut { get; } = new();

    public ReactiveCommand Copy { get; } = new();

    public ReactiveCommand Exclude { get; } = new();

    public ReactiveCommand Delete { get; } = new();

    public ReactiveCommand FinishEditingAnimation { get; } = new();

    public ReactiveCommand BringAnimationToTop { get; } = new();

    public void Dispose()
    {
        _disposables.Dispose();
        GC.SuppressFinalize(this);
    }

    public async void AnimationRequest(int layerNum, bool affectModel = true, CancellationToken cancellationToken = default)
    {
        var inlines = Timeline.Inlines
            .Where(x => x.Layer == this)
            .Select(x => (ViewModel: x, Context: x.PrepareAnimation()))
            .ToArray();

        var newMargin = new Thickness(0, Timeline.CalculateLayerTop(layerNum), 0, 0);
        Thickness oldMargin = Margin.Value;
        if (affectModel)
            Model.ZIndex = layerNum;

        Margin.Value = oldMargin;

        foreach (var (item, context) in inlines)
            item.AnimationRequest(context, newMargin, cancellationToken);

        await AnimationRequested((newMargin, BorderMargin.Value, Width.Value), cancellationToken);
        Margin.Value = newMargin;
    }

    public async ValueTask SyncModelToViewModel()
    {
        float scale = Timeline.Options.Value.Scale;
        int rate = Scene.Parent is Project proj ? proj.GetFrameRate() : 30;
        Thickness oldMargin = Margin.Value;
        Thickness oldBorderMargin = BorderMargin.Value;
        double oldWidth = Width.Value;
        var inlines = Timeline.Inlines
            .Where(x => x.Layer == this)
            .Select(x => (ViewModel: x, Context: x.PrepareAnimation()))
            .ToArray();

        int layerNum = Timeline.ToLayerNumber(Margin.Value);
        Scene.MoveChild(
            layerNum,
            BorderMargin.Value.Left.ToTimeSpan(scale).RoundToRate(rate),
            Width.Value.ToTimeSpan(scale).RoundToRate(rate),
            Model).DoAndRecord(CommandRecorder.Default);

        var margin = new Thickness(0, Timeline.CalculateLayerTop(Model.ZIndex), 0, 0);
        var borderMargin = new Thickness(Model.Start.ToPixel(Timeline.Options.Value.Scale), 0, 0, 0);
        double width = Model.Length.ToPixel(Timeline.Options.Value.Scale);

        BorderMargin.Value = oldBorderMargin;
        Margin.Value = oldMargin;
        Width.Value = oldWidth;

        foreach (var (item, context) in inlines)
            item.AnimationRequest(context, margin);

        await AnimationRequested((margin, borderMargin, width), default);
        BorderMargin.Value = borderMargin;
        Margin.Value = margin;
        Width.Value = width;
    }

    private async ValueTask<bool> SetClipboard()
    {
        IClipboard? clipboard = Application.Current?.Clipboard;
        if (clipboard != null)
        {
            JsonNode jsonNode = new JsonObject();
            Model.WriteToJson(ref jsonNode);
            string json = jsonNode.ToJsonString(JsonHelper.SerializerOptions);
            var data = new DataObject();
            data.Set(DataFormats.Text, json);
            data.Set(Constants.Layer, json);

            await clipboard.SetDataObjectAsync(data);
            return true;
        }
        else
        {
            return false;
        }
    }
}
