using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.Json.Nodes;

using Avalonia;
using Avalonia.Input;
using Avalonia.Input.Platform;

using BeUtl.Models;
using BeUtl.ProjectSystem;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels;

public sealed class TimelineLayerViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();
    private LayerHeaderViewModel? _layerHeader;

    public TimelineLayerViewModel(Layer sceneLayer, TimelineViewModel timeline)
    {
        Model = sceneLayer;
        Timeline = timeline;

        IObservable<int> zIndexSubject = sceneLayer.GetObservable(Layer.ZIndexProperty);
        Margin = zIndexSubject
            .Select(item => new Thickness(0, item.ToLayerPixel(), 0, 0))
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
            // Todo: レイヤー内複数オブジェクトに対応する
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
            backwardLayer.ZIndex++;

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

        zIndexSubject.Subscribe(number =>
        {
            LayerHeaderViewModel? newLH = Timeline.LayerHeaders.FirstOrDefault(i => i.Number.Value == number);

            if (_layerHeader != null)
                _layerHeader.ItemsCount.Value--;

            if (newLH != null)
                newLH.ItemsCount.Value++;
            _layerHeader = newLH;
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

    public ReactiveProperty<Avalonia.Media.Color> Color { get; }

    public ReactiveCommand<Func<TimeSpan>?> Split { get; } = new();

    public ReactiveCommand Cut { get; } = new();

    public ReactiveCommand Copy { get; } = new();

    public ReactiveCommand Exclude { get; } = new();

    public ReactiveCommand Delete { get; } = new();

    public void Dispose()
    {
        _disposables.Dispose();
        GC.SuppressFinalize(this);
    }

    public async void AnimationRequest(int layerNum, bool affectModel = true, CancellationToken cancellationToken = default)
    {
        var newMargin = new Thickness(0, layerNum.ToLayerPixel(), 0, 0);
        Thickness oldMargin = Margin.Value;
        if (affectModel)
            Model.ZIndex = layerNum;

        Margin.Value = oldMargin;
        await AnimationRequested((newMargin, BorderMargin.Value, Width.Value), cancellationToken);
        Margin.Value = newMargin;
    }

    public async void SyncModelToViewModel()
    {
        float scale = Timeline.Options.Value.Scale;
        int rate = Scene.Parent is Project proj ? proj.GetFrameRate() : 30;
        Thickness oldMargin = Margin.Value;
        Thickness oldBorderMargin = BorderMargin.Value;
        double oldWidth = Width.Value;

        int layerNum = Margin.Value.ToLayerNumber();
        Scene.MoveChild(
            layerNum,
            BorderMargin.Value.Left.ToTimeSpan(scale).RoundToRate(rate),
            Width.Value.ToTimeSpan(scale).RoundToRate(rate),
            Model).DoAndRecord(CommandRecorder.Default);

        var margin = new Thickness(0, Model.ZIndex.ToLayerPixel(), 0, 0);
        var borderMargin = new Thickness(Model.Start.ToPixel(Timeline.Options.Value.Scale), 0, 0, 0);
        double width = Model.Length.ToPixel(Timeline.Options.Value.Scale);

        BorderMargin.Value = oldBorderMargin;
        Margin.Value = oldMargin;
        Width.Value = oldWidth;
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
