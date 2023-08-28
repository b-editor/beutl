using System.Text.Json.Nodes;

using Avalonia;
using Avalonia.Input;
using Avalonia.Input.Platform;

using Beutl.Commands;
using Beutl.Models;
using Beutl.ProjectSystem;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels;

public sealed class ElementViewModel : IDisposable
{
    private static readonly Graphics.ColorMatrix s_grayscale = new(
        0.21f, 0.72f, 0.07f, 0, 0,
        0.21f, 0.72f, 0.07f, 0, 0,
        0.21f, 0.72f, 0.07f, 0, 0,
        0.000f, 0.000f, 0.000f, 1, 0);
    private static readonly Graphics.ColorMatrix s_contrast100 = Graphics.ColorMatrix.CreateContrast(100);
    private readonly CompositeDisposable _disposables = new();
    private IClipboard? _clipboard;

    public ElementViewModel(Element element, TimelineViewModel timeline)
    {
        Model = element;
        Timeline = timeline;

        // プロパティを構成
        IsEnabled = element.GetObservable(Element.IsEnabledProperty)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        UseNode = element.GetObservable(Element.UseNodeProperty)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        Name = element.GetObservable(CoreObject.NameProperty)
            .ToReactiveProperty()
            .AddTo(_disposables)!;
        Name.Subscribe(v => Model.Name = v)
            .AddTo(_disposables);

        IObservable<int> zIndexSubject = element.GetObservable(Element.ZIndexProperty);
        Margin = Timeline.GetTrackedLayerTopObservable(zIndexSubject)
            .Select(item => new Thickness(0, item, 0, 0))
            .ToReactiveProperty()
            .AddTo(_disposables);

        BorderMargin = element.GetObservable(Element.StartProperty)
            .CombineLatest(timeline.EditorContext.Scale)
            .Select(item => new Thickness(item.First.ToPixel(item.Second), 0, 0, 0))
            .ToReactiveProperty()
            .AddTo(_disposables);

        Width = element.GetObservable(Element.LengthProperty)
            .CombineLatest(timeline.EditorContext.Scale)
            .Select(item => item.First.ToPixel(item.Second))
            .ToReactiveProperty()
            .AddTo(_disposables);

        Color = element.GetObservable(Element.AccentColorProperty)
            .Select(c => c.ToAvalonia())
            .ToReactiveProperty()
            .AddTo(_disposables);

        TextColor = Color.Select(c =>
            {
                //filter: invert(100 %) grayscale(100 %) contrast(100);
                var cc = new Media.Color(c.A, (byte)(255 - c.R), (byte)(255 - c.G), (byte)(255 - c.B));
                cc = s_grayscale * cc;
                cc = s_contrast100 * cc;
                return cc.ToAvalonia();
            })
            .ToReadOnlyReactivePropertySlim()
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

        Color.Skip(1)
            .Subscribe(c => new ChangePropertyCommand<Media.Color>(Model, Element.AccentColorProperty, c.ToMedia(), Model.AccentColor)
                .DoAndRecord(CommandRecorder.Default))
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

        KeyBindings = CreateKeyBinding();
    }

    ~ElementViewModel()
    {
        _disposables.Dispose();
    }

    public Func<(Thickness Margin, Thickness BorderMargin, double Width), CancellationToken, Task> AnimationRequested { get; set; } = (_, _) => Task.CompletedTask;

    public TimelineViewModel Timeline { get; private set; }

    public Element Model { get; private set; }

    public Scene Scene => (Scene)Model.HierarchicalParent!;

    public ReadOnlyReactivePropertySlim<bool> IsEnabled { get; }

    public ReadOnlyReactivePropertySlim<bool> UseNode { get; }

    public ReactiveProperty<string> Name { get; }

    public ReactiveProperty<Thickness> Margin { get; }

    public ReactiveProperty<Thickness> BorderMargin { get; }

    public ReactiveProperty<double> Width { get; }

    public ReactiveProperty<bool> IsSelected { get; } = new(false);

    public ReactivePropertySlim<LayerHeaderViewModel?> LayerHeader { get; set; } = new();

    public ReactiveProperty<Avalonia.Media.Color> Color { get; }

    public ReadOnlyReactivePropertySlim<Avalonia.Media.Color> TextColor { get; }

    public ReactiveCommand<Func<TimeSpan>?> Split { get; } = new();

    public AsyncReactiveCommand Cut { get; } = new();

    public ReactiveCommand Copy { get; } = new();

    public ReactiveCommand Exclude { get; } = new();

    public ReactiveCommand Delete { get; } = new();

    public ReactiveCommand FinishEditingAnimation { get; } = new();

    public ReactiveCommand BringAnimationToTop { get; } = new();

    public List<KeyBinding> KeyBindings { get; }

    public void SetClipboard(IClipboard? clipboard)
    {
        _clipboard = clipboard;
    }

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
        TimeSpan start = BorderMargin.Value.Left.ToTimeSpan(scale).RoundToRate(rate);
        TimeSpan length = Width.Value.ToTimeSpan(scale).RoundToRate(rate);
        int zindex = Timeline.ToLayerNumber(Margin.Value);
        Scene.MoveChild(zindex, start, length, Model)
            .DoAndRecord(CommandRecorder.Default);

        await AnimationRequest(context);
    }

    private async ValueTask<bool> SetClipboard()
    {
        IClipboard? clipboard = _clipboard;
        if (clipboard != null)
        {
            var jsonNode = new JsonObject();
            Model.WriteToJson(jsonNode);
            string json = jsonNode.ToJsonString(JsonHelper.SerializerOptions);
            var data = new DataObject();
            data.Set(DataFormats.Text, json);
            data.Set(Constants.Element, json);

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
        if (File.Exists(fileName))
        {
            File.Delete(fileName);
        }

        Scene.RemoveChild(Model).Do();
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
        var backwardLayer = new Element();
        backwardLayer.ReadFromJson(JsonNode.Parse(json)!.AsObject());

        Scene.MoveChild(Model.ZIndex, Model.Start, forwardLength, Model).DoAndRecord(CommandRecorder.Default);
        backwardLayer.Start = absTime;
        backwardLayer.Length = backwardLength;

        backwardLayer.Save(RandomFileNameGenerator.Generate(Path.GetDirectoryName(Scene.FileName)!, Constants.ElementFileExtension));
        Scene.AddChild(backwardLayer).DoAndRecord(CommandRecorder.Default);
    }

    private List<KeyBinding> CreateKeyBinding()
    {
        PlatformHotkeyConfiguration? config = Application.Current?.PlatformSettings?.HotkeyConfiguration;
        KeyModifiers modifier = config?.CommandModifiers ?? KeyModifiers.Control;
        var list = new List<KeyBinding>
        {
            new KeyBinding { Gesture = new(Key.Delete), Command = Exclude },
            new KeyBinding { Gesture = new(Key.Delete, modifier), Command = Delete }
        };

        if (config != null)
        {
            list.AddRange(config.Cut.Select(x => new KeyBinding { Gesture = x, Command = Cut }));
            list.AddRange(config.Copy.Select(x => new KeyBinding { Gesture = x, Command = Copy }));
        }
        else
        {
            list.Add(new KeyBinding { Gesture = new(Key.X, modifier), Command = Cut });
            list.Add(new KeyBinding { Gesture = new(Key.C, modifier), Command = Copy });
        }

        return list;
    }

    public record struct PrepareAnimationContext(
        Thickness Margin,
        Thickness BorderMargin,
        double Width,
        (InlineAnimationLayerViewModel ViewModel, InlineAnimationLayerViewModel.PrepareAnimationContext Context)[] Inlines);
}
