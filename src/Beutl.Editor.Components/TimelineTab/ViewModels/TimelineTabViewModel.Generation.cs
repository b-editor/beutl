using System.Collections.Specialized;
using Beutl.Collections;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.Generative;
using Beutl.Editor.Services.AI;
using Beutl.Logging;
using Beutl.NodeGraph.Generative;
using Beutl.ProjectSystem;
using Beutl.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.TimelineTab.ViewModels;

public sealed partial class TimelineTabViewModel
{
    private TimelineGenerationService? _generation;

    /// <summary>Generations started from this timeline, shown where their results will go.</summary>
    public CoreList<GenerationPlaceholderViewModel> Placeholders { get; } = [];

    /// <summary>Raised to show a generation's popup; the view anchors it to the job's placeholder.</summary>
    public event EventHandler<TimelineGenerationJob>? GenerationPopupRequested;

    /// <summary>Generations run through the editor; null where AI generation is not offered.</summary>
    public TimelineGenerationService? GenerationService => _generation;

    /// <summary>Whether the timeline offers AI generation, which needs the application's services.</summary>
    public bool IsGenerationAvailable => _generation is not null;

    private void InitializeGeneration()
    {
        _generation = EditorContext.GetService<TimelineGenerationService>();
        if (_generation is null)
            return;

        foreach (TimelineGenerationJob job in _generation.Jobs)
            Placeholders.Add(new GenerationPlaceholderViewModel(job, this));
        _generation.Jobs.CollectionChanged += OnGenerationJobsChanged;
    }

    private void DisposeGeneration()
    {
        if (_generation is not null)
            _generation.Jobs.CollectionChanged -= OnGenerationJobsChanged;
        foreach (GenerationPlaceholderViewModel placeholder in Placeholders)
            placeholder.Dispose();
        Placeholders.Clear();
    }

    private void OnGenerationJobsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is { } removed)
        {
            foreach (TimelineGenerationJob job in removed)
            {
                GenerationPlaceholderViewModel? placeholder = Placeholders.FirstOrDefault(item => ReferenceEquals(item.Job, job));
                if (placeholder is not null)
                {
                    Placeholders.Remove(placeholder);
                    placeholder.Dispose();
                }
            }
        }

        if (e.NewItems is { } added)
        {
            foreach (TimelineGenerationJob job in added)
                Placeholders.Add(new GenerationPlaceholderViewModel(job, this));
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (GenerationPlaceholderViewModel placeholder in Placeholders)
                placeholder.Dispose();
            Placeholders.Clear();
        }
    }

    /// <summary>Opens the popup for a job that is already on the timeline.</summary>
    public void ShowGeneration(TimelineGenerationJob job) => GenerationPopupRequested?.Invoke(this, job);

    /// <summary>Starts an AI action on an element: a popup to set it up, or the AI tab for subtitles.</summary>
    public async Task StartAiActionAsync(Element element, TimelineAiAction action)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (action == TimelineAiAction.Subtitles)
        {
            if (TimelineAiActions.FindSound(element) is { } sound)
                EditorContext.GetService<ITimelineAiHost>()?.OpenSubtitles(sound);
            return;
        }

        if (_generation is null)
            return;

        // Reading the clip's length is too slow for every menu, so it is checked here.
        if (action == TimelineAiAction.ExtendVideo && !TimelineAiActions.CanExtend(element))
        {
            NotificationService.ShowWarning(Strings.AiTimelineExtendVideo, Strings.AiTimelineExtendUnavailable);
            return;
        }

        TimelineGenerationJob? job = action switch
        {
            TimelineAiAction.VideoFromImage => _generation.Create(
                TimelineGenerationTarget.ForElement(TimelineGenerationPlacement.After, element),
                new TimelineGenerationSpec { Kind = TimelineGenerationKind.Video },
                new TimelineGenerationInputs { ImagePath = TimelineFrameCapture.GetImagePath(element) }),
            TimelineAiAction.ContinueVideo => _generation.Create(
                TimelineGenerationTarget.ForElement(TimelineGenerationPlacement.After, element),
                new TimelineGenerationSpec { Kind = TimelineGenerationKind.Video },
                new TimelineGenerationInputs()),
            TimelineAiAction.ExtendVideo => _generation.Create(
                TimelineGenerationTarget.ForElement(TimelineGenerationPlacement.After, element),
                new TimelineGenerationSpec { Kind = TimelineGenerationKind.VideoExtend },
                new TimelineGenerationInputs { VideoPath = TimelineFrameCapture.GetVideoPath(element) }),
            TimelineAiAction.EditVideo => _generation.Create(
                TimelineGenerationTarget.ForElement(TimelineGenerationPlacement.Above, element),
                new TimelineGenerationSpec { Kind = TimelineGenerationKind.VideoEdit },
                new TimelineGenerationInputs { VideoPath = TimelineFrameCapture.GetVideoPath(element) }),
            _ when TimelineAiActions.ImageTaskOf(action) is { } task => _generation.Create(
                TimelineGenerationTarget.ForElement(TimelineGenerationPlacement.ReplaceImage, element),
                new TimelineGenerationSpec { Kind = TimelineGenerationKind.ImageEdit, ImageTask = task },
                new TimelineGenerationInputs { ImagePath = TimelineFrameCapture.GetImagePath(element) }),
            _ => null,
        };
        if (job is null)
            return;

        ShowGeneration(job);
        if (action == TimelineAiAction.ContinueVideo)
            await CaptureFirstFrameAsync(job, element);
    }

    /// <summary>The empty stretch of the layer at the clicked point, if there is one to fill.</summary>
    public TimelineGap? FindGenerationGapAtPointer()
    {
        if (_generation is null)
            return null;
        // The raw pointer time, as for closing a gap: rounding can land on the next clip's start.
        TimeSpan time = ClickedPosition.X.PixelToTimeSpan(Options.Value.Scale);
        if (time < Scene.Start || time >= Scene.Start + Scene.Duration)
            return null;
        return TimelineGenerationSlots.FindGap(
            Scene,
            CalculateClickedLayer(),
            time,
            _generation.Jobs.Select(job => job.Slot.Value).OfType<TimelineGenerationSlot>());
    }

    /// <summary>Starts a clip that fills the gap, beginning on the last frame before it.</summary>
    public async Task StartGapGenerationAsync(TimelineGap gap)
    {
        ArgumentNullException.ThrowIfNull(gap);
        if (_generation is null)
            return;

        string? image = gap.Previous is { } previous ? TimelineFrameCapture.GetImagePath(previous) : null;
        TimelineGenerationJob job = _generation.Create(
            TimelineGenerationTarget.ForGap(gap),
            new TimelineGenerationSpec { Kind = TimelineGenerationKind.Video },
            new TimelineGenerationInputs { ImagePath = image });
        ShowGeneration(job);
        if (image is null && gap.Previous is { } clip && TimelineFrameCapture.GetVideoPath(clip) is not null)
            await CaptureFirstFrameAsync(job, clip);
    }

    /// <summary>Generates a generated element again under the conditions it was made with.</summary>
    public void Regenerate(Element element, bool edit)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (_generation is null
            || element.Generation is not { } generation
            || TimelineGenerationSpec.FromJson(generation.Parameters) is not { } spec)
        {
            return;
        }

        TimelineGenerationJob job = _generation.Create(
            TimelineGenerationTarget.ForElement(TimelineGenerationPlacement.NewTake, element),
            spec,
            new TimelineGenerationInputs
            {
                ImagePath = generation.InputImage is { HasUri: true } image ? image.Uri.LocalPath : null,
                LastFramePath = generation.InputLastFrame is { HasUri: true } last ? last.Uri.LocalPath : null,
                VideoPath = generation.InputVideo is { HasUri: true } video ? video.Uri.LocalPath : null,
            });
        if (edit)
            ShowGeneration(job);
        else
            _ = _generation.RunAsync(job);
    }

    private async Task CaptureFirstFrameAsync(TimelineGenerationJob job, Element clip)
    {
        job.IsPreparing.Value = true;
        string directory = EditorContext.GetService<ITimelineAiHost>()?.GetResourceDirectory(Scene)
                           ?? Path.Combine(Path.GetTempPath(), "beutl-ai-frames");
        try
        {
            string? frame = await TimelineFrameCapture.CaptureLastFrameAsync(clip, directory, CancellationToken.None);
            if (frame is null)
            {
                job.Error.Value = Strings.AiTimelineNoFrame;
                return;
            }

            if (job.State.Value == TimelineGenerationState.Draft)
                job.Inputs.Value = job.Inputs.Value with { ImagePath = frame };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to capture the last frame of a clip for a generation.");
            job.Error.Value = Strings.AiTimelineNoFrame;
        }
        finally
        {
            if (_generation?.Jobs.Contains(job) == true)
                job.IsPreparing.Value = false;
        }
    }
}
