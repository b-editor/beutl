using System.Text.Json.Nodes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Editor;
using Beutl.Editor.Components.Helpers;
using Beutl.Logging;
using Beutl.Serialization;
using Beutl.Services;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.ViewModels;

public sealed class InlineKeyFrameViewModel : IDisposable
{
    private readonly ILogger _logger = Log.CreateLogger<InlineKeyFrameViewModel>();
    private readonly CompositeDisposable _disposables = [];

    public InlineKeyFrameViewModel(IKeyFrame keyframe, IKeyFrameAnimation animation,
        InlineAnimationLayerViewModel parent)
    {
        Model = keyframe;
        Animation = animation;
        Timeline = parent.Timeline;
        Parent = parent;

        Left = keyframe.GetObservable(KeyFrame.KeyTimeProperty)
            .CombineLatest(Timeline.Options)
            .Select(item => item.First.TimeToPixel(item.Second.Scale))
            .ToReactiveProperty()
            .DisposeWith(_disposables);

        CopyCommand = new AsyncReactiveCommand()
            .WithSubscribe(CopyAsync)
            .DisposeWith(_disposables);

        PasteCommand = new AsyncReactiveCommand()
            .WithSubscribe(PasteAsync)
            .DisposeWith(_disposables);

        RemoveCommand = new ReactiveCommand()
            .WithSubscribe(Remove)
            .DisposeWith(_disposables);
    }

    public InlineAnimationLayerViewModel Parent { get; }

    public IKeyFrame Model { get; }

    public IKeyFrameAnimation Animation { get; }

    public TimelineViewModel Timeline { get; }

    public ReactiveProperty<double> Left { get; }

    public AsyncReactiveCommand CopyCommand { get; set; }

    public AsyncReactiveCommand PasteCommand { get; set; }

    public ReactiveCommand RemoveCommand { get; }

    private async Task CopyAsync()
    {
        IClipboard? clipboard = ClipboardHelper.GetClipboard();
        if (clipboard == null) return;

        try
        {
            var data = new DataTransfer();
            ObjectRegenerator.Regenerate(Model, out string json);
            data.Add(DataTransferItem.CreateText(json));
            data.Add(DataTransferItem.Create(BeutlDataFormats.KeyFrame, json));

            await clipboard.SetDataAsync(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy keyframe");
            NotificationService.ShowError(Strings.Copy, Strings.FailedToCopyKeyframe);
        }
    }

    private async Task PasteAsync()
    {
        IClipboard? clipboard = ClipboardHelper.GetClipboard();
        if (clipboard == null) return;

        try
        {
            if (await clipboard.TryGetValueAsync(BeutlDataFormats.KeyFrame) is { } json
                && JsonNode.Parse(json) is JsonObject jsonObj)
            {
                if (!jsonObj.TryGetDiscriminator(out Type? type))
                {
                    NotificationService.ShowWarning(Strings.Paste, Strings.InvalidKeyframeDataFormat_MissingType);
                    return;
                }

                if (!type.IsAssignableTo(typeof(KeyFrame)))
                {
                    NotificationService.ShowWarning(Strings.Paste, Strings.InvalidKeyframeDataFormat_TypeIsNotKeyFrame);
                    return;
                }

                KeyFrame newKeyFrame = (KeyFrame)Activator.CreateInstance(type)!;
                CoreSerializer.PopulateFromJsonObject(newKeyFrame, jsonObj);
                HistoryManager history = Timeline.EditorContext.HistoryManager;

                if (type.GenericTypeArguments[0] != Parent.Property.PropertyType)
                {
                    // イージングのみ変更
                    Model.Easing = newKeyFrame.Easing;
                    history.Commit(CommandNames.ChangeEasing);
                    NotificationService.ShowWarning(Strings.GraphEditor,
                        Strings.KeyframePropertyTypeMismatch_EasingApplied);
                }
                else
                {
                    newKeyFrame.KeyTime = Model.KeyTime;
                    int index = Animation.KeyFrames.IndexOf(Model);
                    Animation.KeyFrames.Remove(Model);
                    Animation.KeyFrames.Insert(index, (IKeyFrame)newKeyFrame);
                    history.Commit(CommandNames.PasteKeyFrame);
                }

                return;
            }

            NotificationService.ShowWarning(Strings.Paste, Strings.InvalidKeyframeDataFormat);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to paste keyframe");
            NotificationService.ShowError(Strings.Paste, Strings.FailedToPasteKeyframe);
        }
    }

    private void Remove()
    {
        HistoryManager history = Timeline.EditorContext.HistoryManager;
        AnimationOperations.RemoveKeyFrame(
            animation: Animation,
            keyframe: Model,
            logger: _logger);
        history.Commit(CommandNames.RemoveKeyFrame);
    }

    public void UpdateKeyTime()
    {
        float scale = Timeline.Options.Value.Scale;
        Project? proj = Timeline.Scene.FindHierarchicalParent<Project>();
        int rate = proj?.GetFrameRate() ?? 30;
        HistoryManager history = Timeline.EditorContext.HistoryManager;

        TimeSpan time = Left.Value.PixelToTimeSpan(scale).RoundToRate(rate);
        SplineEasingHelper.Move(Animation, Model, time);
        history.Commit(CommandNames.MoveKeyFrame);

        Left.Value = time.TimeToPixel(scale);
    }

    public void ReflectModelKeyTime()
    {
        float scale = Timeline.Options.Value.Scale;
        Project? proj = Timeline.Scene.FindHierarchicalParent<Project>();
        int rate = proj?.GetFrameRate() ?? 30;

        TimeSpan time = Left.Value.PixelToTimeSpan(scale).RoundToRate(rate);

        Left.Value = time.TimeToPixel(scale);

        Model.KeyTime = time;
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}
