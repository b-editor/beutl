using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Helpers;
using Beutl.Logging;
using Beutl.Models;
using Beutl.Reactive;
using Beutl.Services;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.ViewModels;

public sealed class InlineAnimationLayerViewModel<T>(
    IAnimatablePropertyAdapter<T> property,
    TimelineViewModel timeline,
    ElementViewModel element)
    : InlineAnimationLayerViewModel(property, timeline, element)
{
    public override void DropEasing(Easing easing, TimeSpan keyTime)
    {
        if (Property.Animation is KeyFrameAnimation<T> kfAnimation)
        {
            TimeSpan originalKeyTime = keyTime;
            keyTime = ConvertKeyTime(originalKeyTime, kfAnimation);
            Project? proj = Timeline.Scene.FindHierarchicalParent<Project>();
            int rate = proj?.GetFrameRate() ?? 30;

            TimeSpan threshold = TimeSpan.FromSeconds(1d / rate) * 3;

            IKeyFrame? keyFrame =
                kfAnimation.KeyFrames.FirstOrDefault(v => Math.Abs(v.KeyTime.Ticks - keyTime.Ticks) <= threshold.Ticks);
            if (keyFrame != null)
            {
                CommandRecorder recorder = Timeline.EditorContext.CommandRecorder;
                RecordableCommands.Edit(keyFrame, KeyFrame.EasingProperty, easing)
                    .WithStoables([Element.Model])
                    .DoAndRecord(recorder);
            }
            else
            {
                InsertKeyFrame(easing, originalKeyTime);
            }
        }
    }

    public override void InsertKeyFrame(Easing easing, TimeSpan keyTime)
    {
        if (Property.Animation is not KeyFrameAnimation<T> animation) return;

        AnimationOperations.InsertKeyFrame(
            animation: animation,
            scene: Timeline.Scene,
            element: Element.Model,
            easing: easing,
            keyTime: keyTime,
            logger: _logger,
            cr: Timeline.EditorContext.CommandRecorder,
            storables: [Element.Model]);
    }
}

public abstract class InlineAnimationLayerViewModel : IDisposable
{
    protected readonly ILogger _logger = Log.CreateLogger<InlineAnimationLayerViewModel>();
    private readonly CompositeDisposable _disposables = [];
    private readonly CompositeDisposable _innerDisposables = [];
    private readonly ReactivePropertySlim<bool> _useGlobalClock = new(true);
    private LayerHeaderViewModel? _lastLayerHeader;
    private TimeSpan _pointerPosition;

    protected InlineAnimationLayerViewModel(
        IAnimatablePropertyAdapter property,
        TimelineViewModel timeline,
        ElementViewModel element)
    {
        Property = property;
        Timeline = timeline;
        Element = element;

        Element.LayerHeader.Subscribe(OnLayerHeaderChanged).DisposeWith(_disposables);

        LeftMargin = _useGlobalClock.Select(v => !v ? element.BorderMargin : Observable.Return<Thickness>(default))
            .Switch()
            .ToReactiveProperty()
            .DisposeWith(_disposables);

        Margin = new TrackedInlineLayerTopObservable(this)
            .Select(x => new Thickness(0, x, 0, 0))
            .CombineLatest(element.Margin)
            .Select(x => x.First + x.Second)
            .ToReactiveProperty()
            .DisposeWith(_disposables);

        // Widthプロパティを構成
        Timeline.Options.Subscribe(_ => UpdateWidth()).DisposeWith(_disposables);

        Header = property.DisplayName;

        Close = new ReactiveCommand()
            .WithSubscribe(() => Timeline.DetachInline(this))
            .DisposeWith(_disposables);

        CopyAllKeyFramesCommand = new ReactiveCommand()
            .WithSubscribe(async () => await CopyAllKeyFramesAsync())
            .DisposeWith(_disposables);

        PasteKeyFrameAtCurrentPositionCommand = new ReactiveCommand()
            .WithSubscribe(async () => await PasteKeyFrameAtPositionAsync(_pointerPosition))
            .DisposeWith(_disposables);

        DeleteCurrentAnimationCommand = new ReactiveCommand()
            .WithSubscribe(() =>
            {
                if (Property.Animation is { } animation)
                {
                    (Timeline.EditorContext as ISupportCloseAnimation)?.Close(animation);
                    DeleteAnimation();
                }
            })
            .DisposeWith(_disposables);

        Property.ObserveAnimation.CombineWithPrevious()
            .Subscribe(t =>
            {
                if (t.OldValue != null)
                {
                    t.OldValue.Invalidated -= OnAnimationInvalidated;
                    _innerDisposables.Clear();
                    ClearItems();
                }

                if (t.NewValue != null)
                {
                    t.NewValue.Invalidated += OnAnimationInvalidated;
                    if (t.NewValue is IKeyFrameAnimation kfAnimation)
                    {
                        kfAnimation.KeyFrames.ForEachItem(
                                (idx, item) => Items.Insert(idx, new InlineKeyFrameViewModel(item, kfAnimation, this)),
                                (idx, _) =>
                                {
                                    InlineKeyFrameViewModel item = Items[idx];
                                    item.Dispose();
                                    Items.RemoveAt(idx);
                                },
                                ClearItems)
                            .DisposeWith(_innerDisposables);
                    }

                    ((CoreObject)t.NewValue).GetObservable(KeyFrameAnimation.UseGlobalClockProperty)
                        .Subscribe(v => _useGlobalClock.Value = v)
                        .DisposeWith(_innerDisposables);
                }
            })
            .DisposeWith(_disposables);
    }

    public Func<Thickness, Thickness, CancellationToken, Task>? AnimationRequested { get; set; }

    public IAnimatablePropertyAdapter Property { get; }

    public TimelineViewModel Timeline { get; }

    public ElementViewModel Element { get; }

    [Obsolete("Use Element property instead.")]
    public ElementViewModel Layer => Element;

    public CoreList<InlineKeyFrameViewModel> Items { get; } = [];

    public ReactiveProperty<Thickness> Margin { get; }

    public ReactiveProperty<Thickness> LeftMargin { get; }

    public ReactivePropertySlim<double> Width { get; } = new();

    public ReactivePropertySlim<int> Index { get; } = new();

    public string Header { get; }

    public ReactiveCommand Close { get; }

    public ReactiveCommand CopyAllKeyFramesCommand { get; }

    public ReactiveCommand PasteKeyFrameAtCurrentPositionCommand { get; }

    public ReactiveCommand DeleteCurrentAnimationCommand { get; }

    public ReactivePropertySlim<LayerHeaderViewModel?> LayerHeader => Element.LayerHeader;

    public abstract void DropEasing(Easing easing, TimeSpan keyTime);

    public abstract void InsertKeyFrame(Easing easing, TimeSpan keyTime);

    public TimeSpan ConvertKeyTime(TimeSpan globalkeyTime, IAnimation animation)
    {
        TimeSpan localKeyTime = globalkeyTime - Element.Model.Start;
        TimeSpan keyTime = animation.UseGlobalClock ? globalkeyTime : localKeyTime;

        int rate = Timeline.Scene?.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30;

        return keyTime.RoundToRate(rate);
    }

    private void PasteAnimation(string json)
    {
        _logger.LogInformation("Pasting JSON");
        if (JsonNode.Parse(json) is not JsonObject newJson)
        {
            _logger.LogError("Invalid JSON");
            NotificationService.ShowError(Strings.GraphEditor, "Invalid JSON");
            return;
        }

        if (!newJson.TryGetDiscriminator(out Type? discriminator))
        {
            _logger.LogError("Invalid JSON: missing $type");
            NotificationService.ShowError(Strings.GraphEditor, "Invalid JSON: missing $type");
            return;
        }

        if (!discriminator.IsAssignableTo(typeof(IKeyFrameAnimation)))
        {
            _logger.LogError("Invalid JSON: $type is not a KeyFrameAnimation");
            NotificationService.ShowError(Strings.GraphEditor, "Invalid JSON: $type is not a KeyFrameAnimation");
            return;
        }

        try
        {
            CommandRecorder recorder = Timeline.EditorContext.CommandRecorder;
            KeyFrameAnimation animation = (KeyFrameAnimation)Property.Animation!;

            if (discriminator.GenericTypeArguments[0] != animation.Property.PropertyType)
            {
                _logger.LogError("The property type of the pasted animation does not match.");
                NotificationService.ShowError(Strings.GraphEditor, $"The property type of the pasted animation does not match. (Expected: {animation.Property.PropertyType.Name}, Actual: {discriminator.GenericTypeArguments[0].Name})");
                return;
            }

            JsonObject oldJson = CoreSerializerHelper.SerializeToJsonObject(animation);
            Guid id = animation.Id;
            CoreProperty property = animation.Property;

            RecordableCommands.Create(
                    () =>
                    {
                        CoreSerializerHelper.PopulateFromJsonObject(animation, newJson);
                        animation.Property = property;
                        animation.Id = id;
                        foreach (IKeyFrame item in animation.KeyFrames)
                        {
                            item.Id = Guid.NewGuid();
                        }
                    },
                    () =>
                    {
                        CoreSerializerHelper.PopulateFromJsonObject(animation, oldJson);
                        animation.Property = property;
                        animation.Id = id;
                    },
                    [Element.Model])
                .DoAndRecord(recorder);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception occurred while pasting JSON.");
            NotificationService.ShowError(Strings.GraphEditor, ex.Message);
        }
    }

    private void PasteKeyFrame(string json, TimeSpan pointerPosition)
    {
        _logger.LogInformation("Pasting JSON");
        if (JsonNode.Parse(json) is not JsonObject newJson)
        {
            _logger.LogError("Invalid JSON");
            NotificationService.ShowError(Strings.GraphEditor, "Invalid JSON");
            return;
        }

        if (!newJson.TryGetDiscriminator(out Type? discriminator))
        {
            _logger.LogError("Invalid JSON: missing $type");
            NotificationService.ShowError(Strings.GraphEditor, "Invalid JSON: missing $type");
            return;
        }

        if (!discriminator.IsAssignableTo(typeof(KeyFrame)))
        {
            _logger.LogError("Invalid JSON: $type is not a KeyFrame");
            NotificationService.ShowError(Strings.GraphEditor, "Invalid JSON: $type is not a KeyFrame");
            return;
        }

        try
        {
            CommandRecorder recorder = Timeline.EditorContext.CommandRecorder;
            KeyFrameAnimation animation = (KeyFrameAnimation)Property.Animation!;

            KeyFrame newKeyFrame = (KeyFrame)Activator.CreateInstance(discriminator)!;
            CoreSerializerHelper.PopulateFromJsonObject(newKeyFrame, newJson);

            if (discriminator.GenericTypeArguments[0] != animation.Property.PropertyType)
            {
                InsertKeyFrame(newKeyFrame.Easing, pointerPosition);
                NotificationService.ShowWarning(Strings.GraphEditor,
                    "The property type of the pasted keyframe does not match. Only the easing is applied.");
                return;
            }

            var keyTime = ConvertKeyTime(pointerPosition, animation);
            if (animation.KeyFrames.FirstOrDefault(k => k.KeyTime == keyTime) is { } existingKeyFrame)
            {
                // イージングと値を変更
                object? oldValue = existingKeyFrame.Value;
                var command1 = RecordableCommands.Edit(existingKeyFrame, KeyFrame.EasingProperty, newKeyFrame.Easing);
                var command2 = RecordableCommands.Create(
                    () => existingKeyFrame.Value = ((IKeyFrame)newKeyFrame).Value,
                    () => existingKeyFrame.Value = oldValue, []);
                command1.Append(command2)
                    .WithStoables([Element.Model])
                    .DoAndRecord(recorder);
                NotificationService.ShowWarning(Strings.GraphEditor,
                    "A keyframe already exists at the paste position. The easing and value have been updated.");
            }
            else
            {
                newKeyFrame.KeyTime = keyTime;
                RecordableCommands.Create(
                        () => animation.KeyFrames.Add((IKeyFrame)newKeyFrame, out _),
                        () => animation.KeyFrames.Remove((IKeyFrame)newKeyFrame),
                        [Element.Model])
                    .DoAndRecord(recorder);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception occurred while pasting JSON.");
            NotificationService.ShowError(Strings.GraphEditor, ex.Message);
        }
    }

    public void DeleteAnimation()
    {
        CommandRecorder recorder = Timeline.EditorContext.CommandRecorder;
        IAnimation? oldAnimation = Property.Animation;

        RecordableCommands.Create([Element.Model])
            .OnDo(() => Property.Animation = null)
            .OnUndo(() => Property.Animation = oldAnimation)
            .ToCommand()
            .DoAndRecord(recorder);
    }

    private async Task CopyAllKeyFramesAsync()
    {
        IClipboard? clipboard = App.GetClipboard();
        if (clipboard == null) return;

        try
        {
            ObjectRegenerator.Regenerate(Property.Animation!, out string json);

            var data = new DataTransfer();
            data.Add(DataTransferItem.CreateText(json));
            data.Add(DataTransferItem.Create(BeutlDataFormats.KeyFrameAnimation, json));

            await clipboard.SetDataAsync(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy all keyframes");
            NotificationService.ShowError("Copy", "Failed to copy animation");
        }
    }

    private async Task PasteKeyFrameAtPositionAsync(TimeSpan pointerPosition)
    {
        IClipboard? clipboard = App.GetClipboard();
        if (clipboard == null) return;
        if (Property.Animation is not IKeyFrameAnimation) return;

        try
        {
            if (await clipboard.TryGetValueAsync(BeutlDataFormats.KeyFrame) is { } keyFrameJson)
            {
                PasteKeyFrame(keyFrameJson, pointerPosition);
                return;
            }
            else if (await clipboard.TryGetValueAsync(BeutlDataFormats.KeyFrameAnimation) is { } keyFrameAnimationJson)
            {
                PasteAnimation(keyFrameAnimationJson);
                return;
            }

            NotificationService.ShowWarning("", "Invalid keyframe data format.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to paste keyframe at position");
            NotificationService.ShowError("Paste", "Failed to paste keyframe");
        }
    }

    public bool HandleDragOver(DragEventArgs e)
    {
        if (!e.DataTransfer.Contains(BeutlDataFormats.Easing)) return false;

        e.DragEffects = DragDropEffects.Copy;
        return true;
    }

    public bool HandleDrop(DragEventArgs e, double positionX)
    {
        if (e.DataTransfer.TryGetValue(BeutlDataFormats.Easing) is not { } typeName) return false;
        if (TypeFormat.ToType(typeName) is not { } type) return false;
        if (Activator.CreateInstance(type) is not Easing easing) return false;

        float scale = Timeline.Options.Value.Scale;
        TimeSpan time = positionX.ToTimeSpan(scale);
        DropEasing(easing, time);
        return true;
    }

    public void UpdatePointerPosition(double positionX)
    {
        float scale = Timeline.Options.Value.Scale;
        _pointerPosition = positionX.ToTimeSpan(scale);
    }

    private void OnLayerHeaderChanged(LayerHeaderViewModel? obj)
    {
        _lastLayerHeader?.Inlines.Remove(this);
        _lastLayerHeader = obj;
        _lastLayerHeader?.Inlines.Add(this);
    }

    private void UpdateWidth()
    {
        TimeSpan duration = Property.Animation?.Duration ?? TimeSpan.Zero;
        Width.Value = duration.ToPixel(Timeline.Options.Value.Scale);
    }

    private void OnAnimationInvalidated(object? sender, EventArgs e)
    {
        UpdateWidth();
    }

    public PrepareAnimationContext PrepareAnimation()
    {
        return new PrepareAnimationContext(Margin.Value, LeftMargin.Value);
    }

    public async void AnimationRequest(PrepareAnimationContext context, Thickness layerMargin, Thickness leftMargin,
        CancellationToken cancellationToken = default)
    {
        if (LayerHeader.Value is { } layerHeader)
        {
            Index.Value = layerHeader.Inlines.IndexOf(this);
            double top = layerHeader.CalculateInlineTop(Index.Value) + FrameNumberHelper.LayerHeight;
            Thickness newMargin = new Thickness(0, top, 0, 0) + layerMargin;
            Thickness newLeftMargin = Property.Animation?.UseGlobalClock == true ? default : leftMargin;

            Margin.Value = context.Margin;
            LeftMargin.Value = context.LeftMargin;
            if (AnimationRequested != null)
            {
                await AnimationRequested(newMargin, newLeftMargin, cancellationToken);
            }

            Margin.Value = newMargin;
            LeftMargin.Value = newLeftMargin;
        }
    }

    public void Dispose()
    {
        _innerDisposables?.Dispose();
        _disposables.Dispose();
        if (Property.Animation != null)
            Property.Animation.Invalidated -= OnAnimationInvalidated;

        ClearItems();

        GC.SuppressFinalize(this);
    }

    private void ClearItems()
    {
        foreach (InlineKeyFrameViewModel item in Items.GetMarshal().Value)
        {
            item.Dispose();
        }

        Items.Clear();
    }

    public record struct PrepareAnimationContext(Thickness Margin, Thickness LeftMargin);

    private sealed class TrackedInlineLayerTopObservable(InlineAnimationLayerViewModel inline)
        : LightweightObservableBase<double>
    {
        private IDisposable? _disposable1;
        private IDisposable? _disposable2;
        private IDisposable? _disposable3;
        private int _prevIndex = -1;
        private LayerHeaderViewModel? _prevLayerHeader;

        protected override void Deinitialize()
        {
            _disposable1?.Dispose();
            _disposable2?.Dispose();
            _disposable3?.Dispose();
            _disposable1 = null;
            _disposable2 = null;
            _disposable3 = null;
        }

        protected override void Initialize()
        {
            _disposable1 = inline.LayerHeader
                .Subscribe(OnLayerHeaderChanged);

            _disposable2 = inline.Index.Subscribe(OnIndexChanged);
        }

        private void OnLayerHeaderChanged(LayerHeaderViewModel? obj)
        {
            if (_prevLayerHeader != null)
            {
                _disposable3?.Dispose();
                _disposable3 = null;
            }

            _prevLayerHeader = obj;

            if (obj != null)
            {
                _disposable3 = obj.Height.Subscribe(OnLayerHeaderHeightChanged);
            }
        }

        private void OnLayerHeaderHeightChanged(double obj)
        {
            PublishValue();
        }

        private void OnIndexChanged(int obj)
        {
            if (_prevIndex != obj)
            {
                _prevIndex = obj;
                PublishValue();
            }
        }

        private void PublishValue(IObserver<double>? observer = null)
        {
            if (_prevLayerHeader != null)
            {
                _prevIndex = _prevLayerHeader.Inlines.IndexOf(inline);

                double value = _prevLayerHeader.CalculateInlineTop(_prevIndex) + FrameNumberHelper.LayerHeight;
                if (observer == null)
                {
                    PublishNext(value);
                }
                else
                {
                    observer.OnNext(value);
                }
            }
        }

        protected override void Subscribed(IObserver<double> observer, bool first)
        {
            PublishValue(observer);
        }
    }
}
