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

        // プロパティを構成
        IsEnabled = sceneLayer.GetObservable(Layer.IsEnabledProperty)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        AllowOutflow = sceneLayer.GetObservable(Layer.AllowOutflowProperty)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        UseNode = sceneLayer.GetObservable(Layer.UseNodeProperty)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        Name = sceneLayer.GetObservable(CoreObject.NameProperty)
            .ToReactiveProperty()
            .AddTo(_disposables)!;
        Name.Subscribe(v => Model.Name = v)
            .AddTo(_disposables);

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

        // コマンドを構成
        Split.Where(func => func != null)
            .Subscribe(func => OnSplit(func!()))
            .AddTo(_disposables);

        Cut.Subscribe(OnCut)
            .AddTo(_disposables);

        Copy.Subscribe(async () => await SetClipboard())
            .AddTo(_disposables);

        Exclude.Subscribe(() => Scene.RemoveChild(Model).DoAndRecord(CommandRecorder.Default))
            .AddTo(_disposables);

        Delete.Subscribe(OnDelete)
            .AddTo(_disposables);

        Color.Subscribe(c => Model.AccentColor = Media.Color.FromArgb(c.A, c.R, c.G, c.B))
            .AddTo(_disposables);

        FinishEditingAnimation.Subscribe(OnFinishEditingAnimation)
            .AddTo(_disposables);

        BringAnimationToTop.Subscribe(OnBringAnimationToTop)
            .AddTo(_disposables);

        // ZIndexが変更されたら、LayerHeaderのカウントを増減して、新しいLayerHeaderを設定する。
        zIndexSubject.Subscribe(number =>
            {
                LayerHeaderViewModel? newLH = Timeline.LayerHeaders.FirstOrDefault(i => i.Number.Value == number);

                if (LayerHeader.Value != null)
                    LayerHeader.Value.ItemsCount.Value--;

                if (newLH != null)
                    newLH.ItemsCount.Value++;
                LayerHeader.Value = newLH;
            })
            .AddTo(_disposables);
    }

    ~TimelineLayerViewModel()
    {
        _disposables.Dispose();
    }

    public Func<(Thickness Margin, Thickness BorderMargin, double Width), CancellationToken, Task> AnimationRequested { get; set; } = (_, _) => Task.CompletedTask;

    public TimelineViewModel Timeline { get; private set; }

    public Layer Model { get; private set; }

    public Scene Scene => (Scene)Model.HierarchicalParent!;

    public ReadOnlyReactivePropertySlim<bool> IsEnabled { get; }

    public ReadOnlyReactivePropertySlim<bool> AllowOutflow { get; }

    public ReadOnlyReactivePropertySlim<bool> UseNode { get; }

    public ReactiveProperty<string> Name { get; }

    public ReactiveProperty<Thickness> Margin { get; }

    public ReactiveProperty<Thickness> BorderMargin { get; }

    public ReactiveProperty<double> Width { get; }

    public ReactiveProperty<bool> IsSelected { get; } = new(false);

    public ReactivePropertySlim<LayerHeaderViewModel?> LayerHeader { get; set; } = new();

    public ReactiveProperty<Avalonia.Media.Color> Color { get; }

    public ReactiveCommand<Func<TimeSpan>?> Split { get; } = new();

    public AsyncReactiveCommand Cut { get; } = new();

    public ReactiveCommand Copy { get; } = new();

    public ReactiveCommand Exclude { get; } = new();

    public ReactiveCommand Delete { get; } = new();

    public ReactiveCommand FinishEditingAnimation { get; } = new();

    public ReactiveCommand BringAnimationToTop { get; } = new();

    public void Dispose()
    {
        _disposables.Dispose();
        LayerHeader.Dispose();

        LayerHeader.Value = null!;
        Timeline = null!;
        Model = null!;
        AnimationRequested = (_, _) => Task.CompletedTask;
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
            item.AnimationRequest(context, newMargin, BorderMargin.Value, cancellationToken);

        await AnimationRequested((newMargin, BorderMargin.Value, Width.Value), cancellationToken);
        Margin.Value = newMargin;
    }

    public async Task AnimationRequest(PrepareAnimationContext context, CancellationToken cancellationToken = default)
    {
        var margin = new Thickness(0, Timeline.CalculateLayerTop(Model.ZIndex), 0, 0);
        var borderMargin = new Thickness(Model.Start.ToPixel(Timeline.Options.Value.Scale), 0, 0, 0);
        double width = Model.Length.ToPixel(Timeline.Options.Value.Scale);

        BorderMargin.Value = context.BorderMargin;
        Margin.Value = context.Margin;
        Width.Value = context.Width;

        foreach (var (item, inlineContext) in context.Inlines)
        {
            item.AnimationRequest(inlineContext, margin, borderMargin, cancellationToken);
        }

        await AnimationRequested((margin, borderMargin, width), cancellationToken);
        BorderMargin.Value = borderMargin;
        Margin.Value = margin;
        Width.Value = width;
    }

    public async Task SubmitViewModelChanges()
    {
        PrepareAnimationContext context = PrepareAnimation();

        float scale = Timeline.Options.Value.Scale;
        int rate = Scene.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30;
        int zindex = Timeline.ToLayerNumber(Margin.Value);
        TimeSpan start = BorderMargin.Value.Left.ToTimeSpan(scale).RoundToRate(rate);
        TimeSpan length = Width.Value.ToTimeSpan(scale).RoundToRate(rate);
        Scene.MoveChild(zindex, start, length, Model)
            .DoAndRecord(CommandRecorder.Default);

        await AnimationRequest(context);
    }

    private async ValueTask<bool> SetClipboard()
    {
        IClipboard? clipboard = Application.Current?.Clipboard;
        if (clipboard != null)
        {
            var jsonNode = new JsonObject();
            Model.WriteToJson(jsonNode);
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

    public PrepareAnimationContext PrepareAnimation()
    {
        return new PrepareAnimationContext(
            Margin: Margin.Value,
            BorderMargin: BorderMargin.Value,
            Width: Width.Value,
            Inlines: Timeline.Inlines
                .Where(x => x.Layer == this)
                .Select(x => (ViewModel: x, Context: x.PrepareAnimation()))
                .ToArray());
    }

    private void OnDelete()
    {
        string fileName = Model.FileName;
        Scene.RemoveChild(Model).Do();
        if (File.Exists(fileName))
        {
            File.Delete(fileName);
        }
    }

    private void OnBringAnimationToTop()
    {
        if (LayerHeader.Value is { } layerHeader)
        {
            InlineAnimationLayerViewModel[] inlines = Timeline.Inlines.Where(x => x.Layer == this).ToArray();
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
    }

    private void OnFinishEditingAnimation()
    {
        foreach (InlineAnimationLayerViewModel item in Timeline.Inlines.Where(x => x.Layer == this).ToArray())
        {
            Timeline.DetachInline(item);
        }
    }

    private async Task OnCut()
    {
        if (await SetClipboard())
        {
            Exclude.Execute();
        }
    }

    private void OnSplit(TimeSpan timeSpan)
    {
        int rate = Scene.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30;
        TimeSpan absTime = timeSpan.RoundToRate(rate);
        TimeSpan forwardLength = absTime - Model.Start;
        TimeSpan backwardLength = Model.Length - forwardLength;

        var jsonNode = new JsonObject();
        Model.WriteToJson(jsonNode);
        string json = jsonNode.ToJsonString(JsonHelper.SerializerOptions);
        var backwardLayer = new Layer();
        backwardLayer.ReadFromJson(JsonNode.Parse(json)!.AsObject());

        Scene.MoveChild(Model.ZIndex, Model.Start, forwardLength, Model).DoAndRecord(CommandRecorder.Default);
        backwardLayer.Start = absTime;
        backwardLayer.Length = backwardLength;

        backwardLayer.Save(Helper.RandomLayerFileName(Path.GetDirectoryName(Scene.FileName)!, Constants.LayerFileExtension));
        Scene.AddChild(backwardLayer).DoAndRecord(CommandRecorder.Default);
    }

    public record struct PrepareAnimationContext(
        Thickness Margin,
        Thickness BorderMargin,
        double Width,
        (InlineAnimationLayerViewModel ViewModel, InlineAnimationLayerViewModel.PrepareAnimationContext Context)[] Inlines);
}
