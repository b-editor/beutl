using System.Numerics;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Beutl.Animation;
using Beutl.Commands;
using Beutl.Helpers;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;
using Beutl.Utilities;
using FluentAvalonia.UI.Media;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using HslColor = Avalonia.Media.HslColor;

namespace Beutl.ViewModels;

public sealed class ElementViewModel : IDisposable, IContextCommandHandler
{
    private readonly CompositeDisposable _disposables = [];

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

        RestBorderColor = Color.Select(v => (Avalonia.Media.Color)((Color2)v).LightenPercent(-0.15f))
            .ToReadOnlyReactivePropertySlim()!;

        TextColor = Color.Select(ColorGenerator.GetTextColor)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        // コマンドを構成
        Split.Where(_ => GetClickedTime != null)
            .Subscribe(_ => OnSplit(GetClickedTime!()))
            .AddTo(_disposables);

        SplitByCurrentFrame
            .Subscribe(_ => OnSplit(timeline.EditorContext.CurrentTime.Value))
            .AddTo(_disposables);

        Cut.Subscribe(OnCut)
            .AddTo(_disposables);

        Copy.Subscribe(async () => await SetClipboard())
            .AddTo(_disposables);

        Exclude.Subscribe(() =>
            {
                CommandRecorder recorder = Timeline.EditorContext.CommandRecorder;
                Scene.RemoveChild(Model).DoAndRecord(recorder);
            })
            .AddTo(_disposables);

        Delete.Subscribe(OnDelete)
            .AddTo(_disposables);

        Color.Skip(1)
            .Subscribe(c =>
            {
                CommandRecorder recorder = Timeline.EditorContext.CommandRecorder;
                RecordableCommands.Edit(Model, Element.AccentColorProperty, c.ToMedia(), Model.AccentColor)
                    .WithStoables([Model])
                    .DoAndRecord(recorder);
            })
            .AddTo(_disposables);

        FinishEditingAnimation.Subscribe(OnFinishEditingAnimation)
            .AddTo(_disposables);

        BringAnimationToTop.Subscribe(OnBringAnimationToTop)
            .AddTo(_disposables);

        ChangeToOriginalLength.Subscribe(OnChangeToOriginalLength)
            .AddTo(_disposables);

        // ZIndexが変更されたら、LayerHeaderのカウントを増減して、新しいLayerHeaderを設定する。
        zIndexSubject.Subscribe(number =>
            {
                LayerHeaderViewModel? newLH = Timeline.LayerHeaders.FirstOrDefault(i => i.Number.Value == number);

                LayerHeader.Value?.ElementRemoved(this);

                newLH?.ElementAdded(this);
                LayerHeader.Value = newLH;
            })
            .AddTo(_disposables);

        Scope = new ElementScopeViewModel(Model, this);
    }

    ~ElementViewModel()
    {
        _disposables.Dispose();
    }

    public Func<(Thickness Margin, Thickness BorderMargin, double Width), CancellationToken, Task> AnimationRequested
    {
        get;
        set;
    } = (_, _) => Task.CompletedTask;

    public Action RenameRequested { get; set; } = () => { };

    public Func<TimeSpan>? GetClickedTime { get; set; }

    public TimelineViewModel Timeline { get; private set; }

    public Element Model { get; private set; }

    public ElementScopeViewModel Scope { get; private set; }

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

    public ReadOnlyReactivePropertySlim<Avalonia.Media.Color> RestBorderColor { get; }

    public ReadOnlyReactivePropertySlim<Avalonia.Media.Color> TextColor { get; }

    public ReactiveCommand Split { get; } = new();

    public ReactiveCommand SplitByCurrentFrame { get; } = new();

    public AsyncReactiveCommand Cut { get; } = new();

    public ReactiveCommand Copy { get; } = new();

    public ReactiveCommand Exclude { get; } = new();

    public ReactiveCommand Delete { get; } = new();

    public ReactiveCommand FinishEditingAnimation { get; } = new();

    public ReactiveCommand BringAnimationToTop { get; } = new();

    public ReactiveCommand ChangeToOriginalLength { get; } = new();

    public void Dispose()
    {
        _disposables.Dispose();
        LayerHeader.Dispose();
        Scope.Dispose();

        LayerHeader.Value = null!;
        Timeline = null!;
        Model = null!;
        Scope = null!;
        AnimationRequested = (_, _) => Task.CompletedTask;
        GetClickedTime = null;
        GC.SuppressFinalize(this);
    }

    public async void AnimationRequest(int layerNum, bool affectModel = true,
        CancellationToken cancellationToken = default)
    {
        var inlines = Timeline.Inlines
            .Where(x => x.Element == this)
            .Select(x => (ViewModel: x, Context: x.PrepareAnimation()))
            .ToArray();
        var scope = Scope.PrepareAnimation();

        Thickness newMargin = new(0, Timeline.CalculateLayerTop(layerNum), 0, 0);
        Thickness oldMargin = Margin.Value;
        if (affectModel)
            Model.ZIndex = layerNum;

        Margin.Value = oldMargin;

        foreach (var (item, context) in inlines)
            item.AnimationRequest(context, newMargin, BorderMargin.Value, cancellationToken);

        Task task1 = Scope.AnimationRequest(scope, cancellationToken);
        Task task2 = AnimationRequested((newMargin, BorderMargin.Value, Width.Value), cancellationToken);

        await Task.WhenAll(task1, task2);
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

        Task task1 = Scope.AnimationRequest(context.Scope, cancellationToken);
        Task task2 = AnimationRequested((margin, borderMargin, width), cancellationToken);

        await Task.WhenAll(task1, task2);
        BorderMargin.Value = borderMargin;
        Margin.Value = margin;
        Width.Value = width;
    }

    public async Task SubmitViewModelChanges()
    {
        PrepareAnimationContext context = PrepareAnimation();
        CommandRecorder recorder = Timeline.EditorContext.CommandRecorder;

        float scale = Timeline.Options.Value.Scale;
        int rate = Scene.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30;
        TimeSpan start = BorderMargin.Value.Left.ToTimeSpan(scale).RoundToRate(rate);
        TimeSpan length = Width.Value.ToTimeSpan(scale).RoundToRate(rate);
        int zindex = Timeline.ToLayerNumber(Margin.Value);

        Scene.MoveChild(zindex, start, length, Model)
            .DoAndRecord(recorder);

        await AnimationRequest(context);
    }

    private async ValueTask<bool> SetClipboard()
    {
        IClipboard? clipboard = App.GetClipboard();
        if (clipboard == null) return false;

        string json = CoreSerializerHelper.SerializeToJsonString(Model);
        var data = new DataObject();
        data.Set(DataFormats.Text, json);
        data.Set(Constants.Element, json);

        await clipboard.SetDataObjectAsync(data);
        return true;
    }

    public PrepareAnimationContext PrepareAnimation()
    {
        return new PrepareAnimationContext(
            Margin: Margin.Value,
            BorderMargin: BorderMargin.Value,
            Width: Width.Value,
            Inlines: Timeline.Inlines
                .Where(x => x.Element == this)
                .Select(x => (ViewModel: x, Context: x.PrepareAnimation()))
                .ToArray(),
            Scope: Scope.PrepareAnimation());
    }

    private void OnDelete()
    {
        CommandRecorder recorder = Timeline.EditorContext.CommandRecorder;
        Scene.DeleteChild(Model).DoAndRecord(recorder);
    }

    private void OnBringAnimationToTop()
    {
        if (LayerHeader.Value is { } layerHeader)
        {
            InlineAnimationLayerViewModel[] inlines = Timeline.Inlines.Where(x => x.Element == this).ToArray();
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
        foreach (InlineAnimationLayerViewModel item in Timeline.Inlines.Where(x => x.Element == this).ToArray())
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
        CommandRecorder recorder = Timeline.EditorContext.CommandRecorder;
        int rate = Scene.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30;
        TimeSpan minLength = TimeSpan.FromSeconds(1d / rate);
        TimeSpan absTime = timeSpan.RoundToRate(rate);
        TimeSpan forwardLength = absTime - Model.Start;
        TimeSpan backwardLength = Model.Length - forwardLength;

        if (forwardLength < minLength || backwardLength < minLength)
            return;

        ObjectRegenerator.Regenerate(Model, out Element backward);

        IRecordableCommand command1 = Scene.MoveChild(Model.ZIndex, Model.Start, forwardLength, Model);
        backward.Start = absTime;
        backward.Length = backwardLength;
        foreach (KeyFrameAnimation item in new ObjectSearcher(backward,
                         o => o is KeyFrameAnimation { UseGlobalClock: false })
                     .SearchAll()
                     .OfType<KeyFrameAnimation>())
        {
            foreach (IKeyFrame keyframe in item.KeyFrames)
            {
                keyframe.KeyTime -= forwardLength;
            }
        }

        backward.Save(RandomFileNameGenerator.Generate(Path.GetDirectoryName(Scene.FileName)!,
            Constants.ElementFileExtension));
        IRecordableCommand command2 = Scene.AddChild(backward);
        IRecordableCommand command3 = backward.Operation.OnSplit(true, forwardLength, -forwardLength);
        IRecordableCommand command4 = Model.Operation.OnSplit(false, TimeSpan.Zero, -backwardLength);

        IRecordableCommand[] commands = [command1, command2, command3, command4];
        commands.ToCommand()
            .DoAndRecord(recorder);
    }

    private async void OnChangeToOriginalLength()
    {
        if (!Model.UseNode
            && Model.Operation.Children.FirstOrDefault(v => v.HasOriginalLength()) is { } op
            && op.TryGetOriginalLength(out TimeSpan timeSpan))
        {
            PrepareAnimationContext context = PrepareAnimation();
            CommandRecorder recorder = Timeline.EditorContext.CommandRecorder;

            int rate = Scene.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30;
            TimeSpan length = timeSpan.FloorToRate(rate);

            Element? after = Model.GetAfter(Model.ZIndex, Model.Range.End);
            if (after != null)
            {
                TimeSpan delta = after.Start - Model.Start;
                if (delta < length)
                {
                    length = delta;
                }
            }

            Scene.MoveChild(Model.ZIndex, Model.Start, length, Model)
                .DoAndRecord(recorder);

            await AnimationRequest(context);
        }
    }

    public bool HasOriginalLength()
    {
        return !Model.UseNode && Model.Operation.Children.Any(v => v.HasOriginalLength());
    }

    public void Execute(ContextCommandExecution execution)
    {
        if (execution.KeyEventArgs != null)
            execution.KeyEventArgs.Handled = true;
        switch (execution.CommandName)
        {
            case "Rename":
                RenameRequested();
                break;
            case "Copy":
                Copy.Execute();
                break;
            case "Cut":
                Cut.Execute();
                break;
            case "Delete":
                Delete.Execute();
                break;
            case "Exclude":
                Exclude.Execute();
                break;
            case "Split":
                SplitByCurrentFrame.Execute();
                break;
            default:
                if (execution.KeyEventArgs != null)
                    execution.KeyEventArgs.Handled = false;
                break;
        }
    }

    public record struct PrepareAnimationContext(
        Thickness Margin,
        Thickness BorderMargin,
        double Width,
        (InlineAnimationLayerViewModel ViewModel, InlineAnimationLayerViewModel.PrepareAnimationContext Context)[]
            Inlines,
        ElementScopeViewModel.PrepareAnimationContext Scope);
}
