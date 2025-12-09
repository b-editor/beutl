using System.Text.Json.Nodes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Editor;
using Beutl.Helpers;
using Beutl.Logging;
using Beutl.Models;
using Beutl.Serialization;
using Beutl.Services;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using Splat.ModeDetection;

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
            .Select(item => item.First.ToPixel(item.Second.Scale))
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
        IClipboard? clipboard = App.GetClipboard();
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
            NotificationService.ShowError("Copy", "Failed to copy keyframe");
        }
    }

    private async Task PasteAsync()
    {
        IClipboard? clipboard = App.GetClipboard();
        if (clipboard == null) return;

        try
        {
            if (await clipboard.TryGetValueAsync(BeutlDataFormats.KeyFrame) is { } json
                && JsonNode.Parse(json) is JsonObject jsonObj)
            {
                if (!jsonObj.TryGetDiscriminator(out Type? type))
                {
                    NotificationService.ShowWarning("", "Invalid keyframe data format. missing $type.");
                    return;
                }

                if (!type.IsAssignableTo(typeof(KeyFrame)))
                {
                    NotificationService.ShowWarning("", "Invalid keyframe data format. $type is not KeyFrame.");
                    return;
                }

                KeyFrame newKeyFrame = (KeyFrame)Activator.CreateInstance(type)!;
                CoreSerializer.PopulateFromJsonObject(newKeyFrame, jsonObj);
                HistoryManager history = Timeline.EditorContext.HistoryManager;

                if (type.GenericTypeArguments[0] != Parent.Property.PropertyType)
                {
                    // イージングのみ変更
                    Model.Easing = newKeyFrame.Easing;
                    history.Commit();
                    NotificationService.ShowWarning(Strings.GraphEditor,
                        "The property type of the pasted keyframe does not match. Only the easing is applied.");
                }
                else
                {
                    newKeyFrame.KeyTime = Model.KeyTime;
                    int index = Animation.KeyFrames.IndexOf(Model);
                    Animation.KeyFrames.Remove(Model);
                    Animation.KeyFrames.Insert(index, (IKeyFrame)newKeyFrame);
                    history.Commit();
                }

                return;
            }

            NotificationService.ShowWarning("", "Invalid keyframe data format.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to paste keyframe");
            NotificationService.ShowError("Paste", "Failed to paste keyframe");
        }
    }

    private void Remove()
    {
        HistoryManager history = Timeline.EditorContext.HistoryManager;
        AnimationOperations.RemoveKeyFrame(
            animation: Animation,
            keyframe: Model,
            logger: _logger);
        history.Commit();
    }

    public void UpdateKeyTime()
    {
        float scale = Timeline.Options.Value.Scale;
        Project? proj = Timeline.Scene.FindHierarchicalParent<Project>();
        int rate = proj?.GetFrameRate() ?? 30;
        HistoryManager history = Timeline.EditorContext.HistoryManager;

        TimeSpan time = Left.Value.ToTimeSpan(scale).RoundToRate(rate);
        SplineEasingHelper.Move(Animation, Model, time);
        history.Commit();

        Left.Value = time.ToPixel(scale);
    }

    public void ReflectModelKeyTime()
    {
        float scale = Timeline.Options.Value.Scale;
        Project? proj = Timeline.Scene.FindHierarchicalParent<Project>();
        int rate = proj?.GetFrameRate() ?? 30;

        TimeSpan time = Left.Value.ToTimeSpan(scale).RoundToRate(rate);

        Left.Value = time.ToPixel(scale);

        Model.KeyTime = time;
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}
