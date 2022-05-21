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

public class TimelineLayerViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();

    public TimelineLayerViewModel(Layer sceneLayer, ITimelineOptionsProvider optionsProvider)
    {
        Model = sceneLayer;
        OptionsProvider = optionsProvider;

        Margin = sceneLayer.GetSubject(Layer.ZIndexProperty)
            .Select(item => new Thickness(0, item.ToLayerPixel(), 0, 0))
            .ToReactiveProperty()
            .AddTo(_disposables);

        BorderMargin = sceneLayer.GetSubject(Layer.StartProperty)
            .CombineLatest(optionsProvider.Scale)
            .Select(item => new Thickness(item.First.ToPixel(item.Second), 0, 0, 0))
            .ToReactiveProperty()
            .AddTo(_disposables);

        Width = sceneLayer.GetSubject(Layer.LengthProperty)
            .CombineLatest(optionsProvider.Scale)
            .Select(item => item.First.ToPixel(item.Second))
            .ToReactiveProperty()
            .AddTo(_disposables);

        Color = sceneLayer.GetSubject(Layer.AccentColorProperty)
            .Select(c => c.ToAvalonia())
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        ColorSetter = Color.Select(i => (FluentAvalonia.UI.Media.Color2)i)
            .ToReactiveProperty()
            .AddTo(_disposables);

        Split.Where(func => func != null).Subscribe(func =>
        {
            int rate = Scene.Parent is Project proj ? proj.GetFrameRate() : 30;
            TimeSpan absTime = func!().RoundToRate(rate);
            TimeSpan forwardLength = absTime - Model.Start;
            TimeSpan backwardLength = Model.Length - forwardLength;

            string json = Model.ToJson().ToJsonString(JsonHelper.SerializerOptions);
            var backwardLayer = new Layer();
            backwardLayer.FromJson(JsonNode.Parse(json)!);

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

        ColorSetter.Subscribe(c => Model.AccentColor = Media.Color.FromArgb(c.A, c.R, c.G, c.B))
            .AddTo(_disposables);
    }

    ~TimelineLayerViewModel()
    {
        _disposables.Dispose();
    }

    public Func<(Thickness Margin, Thickness BorderMargin, double Width), Task> AnimationRequested1 { get; set; } = _ => Task.CompletedTask;

    public Func<Thickness, Task> AnimationRequested2 { get; set; } = _ => Task.CompletedTask;

    public ITimelineOptionsProvider OptionsProvider { get; }

    public Layer Model { get; }

    public Scene Scene => (Scene)Model.Parent!;

    public ReactiveProperty<Thickness> Margin { get; }

    public ReactiveProperty<Thickness> BorderMargin { get; }

    public ReactiveProperty<double> Width { get; }

    public ReadOnlyReactivePropertySlim<Avalonia.Media.Color> Color { get; }

    public ReactiveProperty<FluentAvalonia.UI.Media.Color2> ColorSetter { get; }

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

    public async void SyncModelToViewModel()
    {
        float scale = OptionsProvider.Options.Value.Scale;
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
        var borderMargin = new Thickness(Model.Start.ToPixel(OptionsProvider.Options.Value.Scale), 0, 0, 0);
        double width = Model.Length.ToPixel(OptionsProvider.Options.Value.Scale);

        BorderMargin.Value = oldBorderMargin;
        Margin.Value = oldMargin;
        Width.Value = oldWidth;
        Task task1 = AnimationRequested1((margin, borderMargin, width));
        Task task2 = AnimationRequested2(margin);
        await Task.WhenAll(task1, task2);
        BorderMargin.Value = borderMargin;
        Margin.Value = margin;
        Width.Value = width;
    }

    private async ValueTask<bool> SetClipboard()
    {
        IClipboard? clipboard = Application.Current?.Clipboard;
        if (clipboard != null)
        {
            string json = Model.ToJson().ToJsonString(JsonHelper.SerializerOptions);
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
