using System.Text.Json.Nodes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Helpers;
using Beutl.Logging;
using Beutl.Models;
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
                CoreSerializerHelper.PopulateFromJsonObject(newKeyFrame, jsonObj);
                CommandRecorder recorder = Timeline.EditorContext.CommandRecorder;

                if (type.GenericTypeArguments[0] != Parent.Property.PropertyType)
                {
                    // イージングのみ変更
                    RecordableCommands.Edit(Model, KeyFrame.EasingProperty, newKeyFrame.Easing, Model.Easing)
                        .WithStoables([Parent.Element.Model])
                        .DoAndRecord(recorder);
                    NotificationService.ShowWarning(Strings.GraphEditor,
                        "The property type of the pasted keyframe does not match. Only the easing is applied.");
                }
                else
                {
                    newKeyFrame.KeyTime = Model.KeyTime;
                    int index = Animation.KeyFrames.IndexOf(Model);
                    Animation.KeyFrames.BeginRecord<IKeyFrame>()
                        .Remove(Model)
                        .Insert(index, (IKeyFrame)newKeyFrame)
                        .ToCommand([Parent.Element.Model])
                        .DoAndRecord(recorder);
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
        AnimationOperations.RemoveKeyFrame(
            animation: Animation,
            scene: Timeline.Scene,
            element: Parent.Element.Model,
            keyframe: Model,
            logger: _logger,
            cr: Timeline.EditorContext.CommandRecorder,
            storables: [Parent.Element.Model]);
    }

    public void UpdateKeyTime()
    {
        float scale = Timeline.Options.Value.Scale;
        Project? proj = Timeline.Scene.FindHierarchicalParent<Project>();
        int rate = proj?.GetFrameRate() ?? 30;
        CommandRecorder recorder = Timeline.EditorContext.CommandRecorder;

        TimeSpan time = Left.Value.ToTimeSpan(scale).RoundToRate(rate);
        SplineEasingHelper.Move(Animation, Model, time)
            ?.WithStoables([Parent.Element.Model])
            .DoAndRecord(recorder);

        Left.Value = time.ToPixel(scale);
    }

    public IRecordableCommand CreateUpdateCommand()
    {
        float scale = Timeline.Options.Value.Scale;
        Project? proj = Timeline.Scene.FindHierarchicalParent<Project>();
        int rate = proj?.GetFrameRate() ?? 30;

        TimeSpan time = Left.Value.ToTimeSpan(scale).RoundToRate(rate);

        Left.Value = time.ToPixel(scale);

        return RecordableCommands.Edit(Model, KeyFrame.KeyTimeProperty, time);
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}
