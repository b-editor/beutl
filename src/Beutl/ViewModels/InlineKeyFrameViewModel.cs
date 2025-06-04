using System.Text.Json.Nodes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Beutl.Animation;
using Beutl.Helpers;
using Beutl.Logging;
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
            var dataObject = new DataObject();
            ObjectRegenerator.Regenerate(Model, out string json);
            dataObject.Set(DataFormats.Text, json);
            dataObject.Set(nameof(IKeyFrame), json);

            await clipboard.SetDataObjectAsync(dataObject);
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
            string[] formats = await clipboard.GetFormatsAsync();

            if (formats.Contains(nameof(IKeyFrame)))
            {
                byte[]? json = await clipboard.GetDataAsync(nameof(IKeyFrame)) as byte[];
                JsonNode? jsonNode = JsonNode.Parse(json!);
                if (jsonNode is not JsonObject jsonObj)
                {
                    NotificationService.ShowWarning("", "Invalid keyframe data format.");
                    return;
                }

                if (jsonObj.TryGetDiscriminator(out Type? type)
                    && Activator.CreateInstance(type) is IKeyFrame keyframe)
                {
                    CoreSerializerHelper.PopulateFromJsonObject(keyframe, type, jsonObj);
                    keyframe.KeyTime = Model.KeyTime;
                    CommandRecorder recorder = Timeline.EditorContext.CommandRecorder;
                    int index = Animation.KeyFrames.IndexOf(Model);
                    Animation.KeyFrames.BeginRecord<IKeyFrame>()
                        .Remove(Model)
                        .Insert(index, keyframe)
                        .ToCommand([Parent.Element.Model])
                        .DoAndRecord(recorder);
                    return;
                }
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
        CommandRecorder recorder = Timeline.EditorContext.CommandRecorder;
        Animation.KeyFrames.BeginRecord<IKeyFrame>()
            .Remove(Model)
            .ToCommand([Parent.Element.Model])
            .DoAndRecord(recorder);
    }

    public void UpdateKeyTime()
    {
        float scale = Timeline.Options.Value.Scale;
        Project? proj = Timeline.Scene.FindHierarchicalParent<Project>();
        int rate = proj?.GetFrameRate() ?? 30;
        CommandRecorder recorder = Timeline.EditorContext.CommandRecorder;

        TimeSpan time = Left.Value.ToTimeSpan(scale).RoundToRate(rate);
        RecordableCommands.Edit(Model, KeyFrame.KeyTimeProperty, time)
            .WithStoables([Parent.Element.Model])
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
