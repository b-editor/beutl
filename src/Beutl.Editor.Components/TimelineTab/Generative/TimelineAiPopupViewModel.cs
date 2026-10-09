using System.Reactive.Disposables;
using System.Reactive.Linq;
using Beutl.Editor.Services.AI;
using Beutl.Language;
using Beutl.Logging;
using Beutl.Media;
using Beutl.NodeGraph.Generative;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;

namespace Beutl.Editor.Components.TimelineTab.Generative;

/// <summary>One choice in the popup, labelled for the person.</summary>
public sealed record TimelineAiChoice<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// The compact task bar for one timeline generation: what to ask for, on which model and in
/// what shape, and what it starts from. Every change is written to the job, so closing the
/// popup and opening it again from the placeholder finds it as it was left.
/// </summary>
public sealed class TimelineAiPopupViewModel : IDisposable
{
    private static readonly ILogger s_logger = Log.CreateLogger<TimelineAiPopupViewModel>();
    private readonly CompositeDisposable _disposables = [];
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TimelineGenerationService _service;
    private readonly IGenerativeModelCatalog? _catalog;
    private readonly ITimelineAiHost? _host;
    private readonly ITimelineAiUsageEstimate? _estimate;
    private readonly PixelSize? _frameSize;
    // The popup is made on the UI thread; values from elsewhere come back to it.
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private bool _syncing;
    private bool _durationSuggested;
    private int _modelLoad;

    public TimelineAiPopupViewModel(
        TimelineGenerationJob job,
        TimelineGenerationService service,
        IGenerativeModelCatalog? catalog,
        ITimelineAiHost? host,
        PixelSize? frameSize,
        int frameRate)
    {
        Job = job ?? throw new ArgumentNullException(nameof(job));
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _catalog = catalog;
        _host = host;
        _frameSize = frameSize;
        TimelineGenerationSpec spec = job.Spec.Value;
        Kind = spec.Kind;
        Title = TitleFor(spec);
        FrameRateText = $"{frameRate} fps";

        ImageTasks = job.Target.Placement != TimelineGenerationPlacement.NewTake
                     && spec.Kind == TimelineGenerationKind.ImageEdit
            ? Enum.GetValues<AiImageEditTask>()
                .Select(task => new TimelineAiChoice<AiImageEditTask>(task, LabelOf(task)))
                .ToArray()
            : [];
        SelectedImageTask = new ReactivePropertySlim<TimelineAiChoice<AiImageEditTask>?>(
            ImageTasks.FirstOrDefault(choice => choice.Value == spec.ImageTask)).DisposeWith(_disposables);
        OutpaintExpansions = [10, 25, 50];
        SelectedOutpaintExpansion = new ReactivePropertySlim<int>(spec.OutpaintExpansionPercent).DisposeWith(_disposables);
        Prompt = new ReactivePropertySlim<string>(spec.Prompt).DisposeWith(_disposables);
        Models = new ReactivePropertySlim<IReadOnlyList<GenerativeModelInfo>>([]).DisposeWith(_disposables);
        SelectedModel = new ReactivePropertySlim<GenerativeModelInfo?>().DisposeWith(_disposables);
        IsLoadingModels = new ReactivePropertySlim<bool>(true).DisposeWith(_disposables);
        Resolutions = new ReactivePropertySlim<IReadOnlyList<string>>([]).DisposeWith(_disposables);
        SelectedResolution = new ReactivePropertySlim<string?>(spec.Resolution).DisposeWith(_disposables);
        AspectRatios = new ReactivePropertySlim<IReadOnlyList<string>>([]).DisposeWith(_disposables);
        SelectedAspectRatio = new ReactivePropertySlim<string?>(spec.AspectRatio).DisposeWith(_disposables);
        Durations = new ReactivePropertySlim<IReadOnlyList<int>>([]).DisposeWith(_disposables);
        SelectedDuration = new ReactivePropertySlim<int>(spec.DurationSeconds).DisposeWith(_disposables);
        GenerateAudio = new ReactivePropertySlim<bool>(spec.GenerateAudio).DisposeWith(_disposables);
        SupportsAudio = new ReactivePropertySlim<bool>(false).DisposeWith(_disposables);
        SupportsLastFrame = new ReactivePropertySlim<bool>(false).DisposeWith(_disposables);
        FirstFrame = new ReactivePropertySlim<AvaloniaBitmap?>().DisposeWith(_disposables);
        LastFrame = new ReactivePropertySlim<AvaloniaBitmap?>().DisposeWith(_disposables);

        RequiresPrompt = SelectedImageTask
            .Select(_ => BuildSpec().RequiresPrompt)
            .ToReadOnlyReactivePropertySlim(spec.RequiresPrompt)
            .DisposeWith(_disposables);
        ShowOutpaintExpansion = SelectedImageTask
            .Select(choice => choice?.Value == AiImageEditTask.Outpaint)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        State = job.State.ToReadOnlyReactivePropertySlim().DisposeWith(_disposables);
        IsRunning = job.State
            .Select(state => state == TimelineGenerationState.Running)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
        Error = job.Error.ToReadOnlyReactivePropertySlim().DisposeWith(_disposables);
        Status = job.Status
            .CombineLatest(
                IsRunning,
                Observable.Interval(TimeSpan.FromSeconds(1)).Select(_ => 0L).StartWith(0L),
                (status, running, _) => running ? DescribeRunning(status) : null)
            .ObserveOnContext(_context)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        IObservable<TimelineAiAccess> access = host?.Access ?? Observable.Return(TimelineAiAccess.Ready);
        Access = access.ObserveOnContext(_context).ToReadOnlyReactivePropertySlim(TimelineAiAccess.Loading).DisposeWith(_disposables);
        IsGated = Access
            .Select(value => value is TimelineAiAccess.SignInRequired or TimelineAiAccess.PlanRequired)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
        GateMessage = Access
            .Select(value => value switch
            {
                TimelineAiAccess.SignInRequired => Strings.AiTimelineSignInRequired,
                TimelineAiAccess.PlanRequired => Strings.AiTimelinePlanRequired,
                _ => null,
            })
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        _estimate = host?.CreateUsageEstimate();
        _estimate?.DisposeWith(_disposables);
        CanAfford = (_estimate?.CanAfford ?? Observable.Return(true))
            .ObserveOnContext(_context)
            .ToReadOnlyReactivePropertySlim(true)
            .DisposeWith(_disposables);
        Explanation = (_estimate?.Explanation ?? Observable.Return(string.Empty))
            .ObserveOnContext(_context)
            .ToReadOnlyReactivePropertySlim(string.Empty)
            .DisposeWith(_disposables);

        IObservable<bool> canGenerate = new[]
            {
                IsRunning.Select(running => !running),
                job.IsPreparing.Select(preparing => !preparing),
                IsGated.Select(gated => !gated),
                IsLoadingModels.Select(loading => !loading),
                SelectedModel.Select(model => model is null || model.IsAvailable),
                CanAfford.AsObservable(),
                Prompt.CombineLatest(RequiresPrompt, (prompt, required) => !required || !string.IsNullOrWhiteSpace(prompt)),
            }
            .CombineLatestValuesAreAllTrue();
        Generate = new AsyncReactiveCommand(canGenerate).WithSubscribe(GenerateCore).DisposeWith(_disposables);
        Cancel = new ReactiveCommand(IsRunning).WithSubscribe(() => _service.Cancel(Job)).DisposeWith(_disposables);
        Discard = new ReactiveCommand(IsRunning.Select(running => !running))
            .WithSubscribe(() =>
            {
                CloseRequested?.Invoke(this, EventArgs.Empty);
                _service.Remove(Job);
            })
            .DisposeWith(_disposables);
        OpenAiWorkspace = new ReactiveCommand().WithSubscribe(() => _host?.OpenAiWorkspace()).DisposeWith(_disposables);

        // Every edit is the job's, so the placeholder and a later popup see it.
        Observable.Merge(
                SelectedImageTask.Select(_ => System.Reactive.Unit.Default),
                SelectedOutpaintExpansion.Select(_ => System.Reactive.Unit.Default),
                Prompt.Throttle(TimeSpan.FromMilliseconds(150)).Select(_ => System.Reactive.Unit.Default),
                SelectedModel.Select(_ => System.Reactive.Unit.Default),
                SelectedResolution.Select(_ => System.Reactive.Unit.Default),
                SelectedAspectRatio.Select(_ => System.Reactive.Unit.Default),
                SelectedDuration.Select(_ => System.Reactive.Unit.Default),
                GenerateAudio.Select(_ => System.Reactive.Unit.Default))
            .ObserveOnContext(_context)
            .Subscribe(_ => WriteSpec())
            .DisposeWith(_disposables);
        SelectedImageTask.Skip(1).Subscribe(choice => _ = LoadModelsAsync()).DisposeWith(_disposables);
        SelectedModel.Subscribe(ApplyCapabilities).DisposeWith(_disposables);

        // A captured frame can arrive after the popup opens.
        job.Inputs
            .ObserveOnContext(_context)
            .Subscribe(inputs =>
            {
                LoadThumbnails(inputs);
                ApplyCapabilities(SelectedModel.Value);
            })
            .DisposeWith(_disposables);
        _ = LoadModelsAsync();
    }

    public TimelineGenerationJob Job { get; }

    public TimelineGenerationKind Kind { get; }

    public string Title { get; }

    public bool IsVideo => Kind is TimelineGenerationKind.Video;

    public bool HasDuration => Kind is TimelineGenerationKind.Video or TimelineGenerationKind.VideoExtend;

    public bool IsImageEdit => Kind == TimelineGenerationKind.ImageEdit;

    public bool HasFrames => Kind == TimelineGenerationKind.Video;

    public string FrameRateText { get; }

    public IReadOnlyList<TimelineAiChoice<AiImageEditTask>> ImageTasks { get; }

    public bool CanChooseImageTask => ImageTasks.Count > 1;

    public ReactivePropertySlim<TimelineAiChoice<AiImageEditTask>?> SelectedImageTask { get; }

    public IReadOnlyList<int> OutpaintExpansions { get; }

    public ReactivePropertySlim<int> SelectedOutpaintExpansion { get; }

    public ReadOnlyReactivePropertySlim<bool> ShowOutpaintExpansion { get; }

    public ReactivePropertySlim<string> Prompt { get; }

    public ReadOnlyReactivePropertySlim<bool> RequiresPrompt { get; }

    public ReactivePropertySlim<IReadOnlyList<GenerativeModelInfo>> Models { get; }

    public ReactivePropertySlim<GenerativeModelInfo?> SelectedModel { get; }

    public ReactivePropertySlim<bool> IsLoadingModels { get; }

    public ReactivePropertySlim<IReadOnlyList<string>> Resolutions { get; }

    public ReactivePropertySlim<string?> SelectedResolution { get; }

    public ReactivePropertySlim<IReadOnlyList<string>> AspectRatios { get; }

    public ReactivePropertySlim<string?> SelectedAspectRatio { get; }

    public ReactivePropertySlim<IReadOnlyList<int>> Durations { get; }

    public ReactivePropertySlim<int> SelectedDuration { get; }

    public ReactivePropertySlim<bool> GenerateAudio { get; }

    public ReactivePropertySlim<bool> SupportsAudio { get; }

    public ReactivePropertySlim<bool> SupportsLastFrame { get; }

    public ReactivePropertySlim<AvaloniaBitmap?> FirstFrame { get; }

    public ReactivePropertySlim<AvaloniaBitmap?> LastFrame { get; }

    public ReadOnlyReactivePropertySlim<TimelineGenerationState> State { get; }

    public ReadOnlyReactivePropertySlim<bool> IsRunning { get; }

    public ReadOnlyReactivePropertySlim<string?> Error { get; }

    public ReadOnlyReactivePropertySlim<string?> Status { get; }

    public ReadOnlyReactivePropertySlim<TimelineAiAccess> Access { get; }

    public ReadOnlyReactivePropertySlim<bool> IsGated { get; }

    public ReadOnlyReactivePropertySlim<string?> GateMessage { get; }

    public ReadOnlyReactivePropertySlim<bool> CanAfford { get; }

    public ReadOnlyReactivePropertySlim<string> Explanation { get; }

    public AsyncReactiveCommand Generate { get; }

    public ReactiveCommand Cancel { get; }

    public ReactiveCommand Discard { get; }

    public ReactiveCommand OpenAiWorkspace { get; }

    /// <summary>Asks for the frame the clip ends on; set by the view.</summary>
    public Func<CancellationToken, Task<string?>>? PickImageFile { get; set; }

    /// <summary>Raised when the popup should close, such as once a generation has started.</summary>
    public event EventHandler? CloseRequested;

    public async Task ChooseLastFrameAsync()
    {
        if (PickImageFile is not { } pick || IsRunning.Value)
            return;
        string? path = await pick(_lifetime.Token);
        if (path is null)
            return;
        Job.Inputs.Value = Job.Inputs.Value with { LastFramePath = path };
    }

    public void ClearLastFrame()
    {
        if (IsRunning.Value)
            return;
        Job.Inputs.Value = Job.Inputs.Value with { LastFramePath = null };
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        _disposables.Dispose();
        FirstFrame.Value?.Dispose();
        LastFrame.Value?.Dispose();
    }

    private async Task GenerateCore()
    {
        WriteSpec();
        Task run = _service.RunAsync(Job);
        // The placeholder carries the progress; the popup is out of the way of the edit.
        CloseRequested?.Invoke(this, EventArgs.Empty);
        await run;
    }

    private TimelineGenerationSpec BuildSpec()
    {
        TimelineGenerationSpec current = Job.Spec.Value;
        return current with
        {
            Prompt = Prompt?.Value ?? current.Prompt,
            ImageTask = SelectedImageTask?.Value?.Value ?? current.ImageTask,
            OutpaintExpansionPercent = SelectedOutpaintExpansion?.Value ?? current.OutpaintExpansionPercent,
            ModelId = SelectedModel?.Value is { IsDefault: false } model ? model.Id : null,
            Resolution = SelectedResolution?.Value ?? current.Resolution,
            AspectRatio = SelectedAspectRatio?.Value ?? current.AspectRatio,
            DurationSeconds = SelectedDuration?.Value ?? current.DurationSeconds,
            GenerateAudio = (GenerateAudio?.Value ?? current.GenerateAudio) && (SupportsAudio?.Value ?? true),
        };
    }

    private void WriteSpec()
    {
        if (_syncing || IsRunning.Value)
            return;
        TimelineGenerationSpec spec = BuildSpec();
        Job.Spec.Value = spec;
        _estimate?.Check(spec.OperationId, spec.ModelId, HasDuration ? spec.DurationSeconds : null);
    }

    private async Task LoadModelsAsync()
    {
        int load = ++_modelLoad;
        IsLoadingModels.Value = true;
        IReadOnlyList<GenerativeModelInfo> models = [];
        try
        {
            if (_catalog is not null)
                models = await _catalog.GetModelsAsync(BuildSpec().OperationId, _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            s_logger.LogWarning(ex, "Failed to load the models for a timeline generation.");
        }

        if (load != _modelLoad || _lifetime.IsCancellationRequested)
            return;

        string? chosen = Job.Spec.Value.ModelId;
        Models.Value = models;
        SelectedModel.Value = models.FirstOrDefault(model => model.Id == chosen)
            ?? models.FirstOrDefault(model => model.IsDefault)
            ?? models.FirstOrDefault();
        IsLoadingModels.Value = false;
        WriteSpec();
    }

    // Keeps every choice within what the model offers, starting from the scene's own shape.
    private void ApplyCapabilities(GenerativeModelInfo? model)
    {
        GenerativeVideoCapabilities video = model?.Video ?? GenerativeVideoCapabilities.Unrestricted;
        _syncing = true;
        try
        {
            if (HasDuration)
            {
                IReadOnlyList<int> durations = video.DurationChoices;
                Durations.Value = durations;
                TimeSpan? span = Job.Target.Gap?.MaxLength;
                // A gap's length picks the first duration offered; after that the choice is the person's.
                bool fitGap = span is not null && !_durationSuggested && model is not null;
                if (model is not null)
                    _durationSuggested = true;
                if (!durations.Contains(SelectedDuration.Value) || fitGap)
                {
                    SelectedDuration.Value = GenerativeShapeSuggestion.SuggestDuration(
                        durations,
                        span,
                        GenerativeVideoCapabilities.DefaultDuration);
                }
            }

            if (IsVideo)
            {
                IReadOnlyList<string> resolutions = video.ResolutionChoices;
                Resolutions.Value = resolutions;
                if (SelectedResolution.Value is not { } resolution || !resolutions.Contains(resolution))
                    SelectedResolution.Value = GenerativeShapeSuggestion.SuggestResolution(resolutions, _frameSize);

                IReadOnlyList<string> ratios = video.AspectRatioChoices;
                AspectRatios.Value = ratios;
                if (SelectedAspectRatio.Value is not { } ratio || !ratios.Contains(ratio))
                    SelectedAspectRatio.Value = GenerativeShapeSuggestion.NearestAspectRatio(ratios, _frameSize, "16:9");

                SupportsAudio.Value = video.SupportsAudio;
                SupportsLastFrame.Value = video.SupportsLastFrame && Job.Inputs.Value.ImagePath is not null;
            }
        }
        finally
        {
            _syncing = false;
        }

        WriteSpec();
    }

    private void LoadThumbnails(TimelineGenerationInputs inputs)
    {
        Replace(FirstFrame, Kind == TimelineGenerationKind.Video ? inputs.ImagePath : null);
        Replace(LastFrame, Kind == TimelineGenerationKind.Video ? inputs.LastFramePath : null);
    }

    private static void Replace(ReactivePropertySlim<AvaloniaBitmap?> property, string? path)
    {
        AvaloniaBitmap? previous = property.Value;
        property.Value = path is null ? null : TryLoadThumbnail(path);
        previous?.Dispose();
    }

    private static AvaloniaBitmap? TryLoadThumbnail(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            return AvaloniaBitmap.DecodeToHeight(stream, 64);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private string? DescribeRunning(string? status)
    {
        TimeSpan elapsed = Job.StartedAt is { } started ? DateTimeOffset.Now - started : TimeSpan.Zero;
        string time = elapsed.ToString(elapsed.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss");
        return string.IsNullOrEmpty(status) ? time : $"{status} · {time}";
    }

    internal static string LabelOf(AiImageEditTask task)
        => Beutl.TypeDisplayHelpers.GetLocalizedName(typeof(AiImageEditTask).GetField(task.ToString())!);

    internal static string TitleFor(TimelineGenerationSpec spec) => spec.Kind switch
    {
        TimelineGenerationKind.Video => Strings.AiTimelineGenerateVideo,
        TimelineGenerationKind.VideoExtend => Strings.AiVideoExtend,
        TimelineGenerationKind.VideoEdit => Strings.AiVideoEditing,
        _ => Strings.AiImageEdit,
    };
}

internal static class TimelineAiObservables
{
    public static IObservable<T> ObserveOnContext<T>(this IObservable<T> source, SynchronizationContext? context)
        => context is null ? source : source.ObserveOn(context);
}
