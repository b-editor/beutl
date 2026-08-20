using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Editor.Services;
using Beutl.Graphics;
using Beutl.Language;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Services.AI;
using Beutl.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.ViewModels.Dialogs;

public sealed class AiVideoGenerationDialogViewModel : IDisposable, IAsyncDisposable
{
    private readonly CompositeDisposable _disposables = [];
    private readonly AsyncOperationLifetime _operations = new();
    private readonly object _disposeGate = new();
    private readonly ILogger _logger = Log.CreateLogger<AiVideoGenerationDialogViewModel>();
    private readonly IAiEntitlementService _entitlements;
    private readonly IAiOperationAvailabilityService _availability;
    private readonly IAiModelCatalogService _modelCatalog;
    private readonly IAiPlanCoordinator _aiPlanCoordinator;
    private readonly IAiVideoService _videos;
    private readonly IAuthenticatedContentService _content;
    private readonly IAiJobKindRegistry _jobKinds;
    private readonly IAiJobMonitor _jobMonitor;
    private readonly AiOperationAvailabilityTracker _availabilityTracker;
    private readonly AiRequestKey _requestKey = new();
    private readonly CancellationTokenSource _availabilityLifetimeCts = new();
    private readonly EditViewModel? _editViewModel;
    private readonly HashSet<string> _temporaryFiles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _temporaryFileLeases = new(StringComparer.Ordinal);
    private readonly HashSet<string> _temporaryFilesPendingDeletion = new(StringComparer.Ordinal);
    private readonly object _lifetimeGate = new();
    private CancellationTokenSource? _pollingCts;
    private AsyncOperationLifetime.Operation? _runningRequest;
    private string? _firstFrameElementId;
    private string? _lastFrameElementId;
    private AiVideoResultSnapshot? _resultSnapshot;
    private Task? _disposeTask;

    public AiVideoGenerationDialogViewModel(
        IAiEntitlementService entitlements,
        IAiOperationAvailabilityService availability,
        IAiModelCatalogService modelCatalog,
        IAiPlanCoordinator aiPlanCoordinator,
        IAiVideoService videos,
        IAuthenticatedContentService content,
        IAiJobKindRegistry jobKinds,
        IAiJobMonitor jobMonitor,
        EditViewModel? editViewModel = null)
    {
        _entitlements = entitlements ?? throw new ArgumentNullException(nameof(entitlements));
        _availability = availability ?? throw new ArgumentNullException(nameof(availability));
        _modelCatalog = modelCatalog ?? throw new ArgumentNullException(nameof(modelCatalog));
        _aiPlanCoordinator = aiPlanCoordinator
            ?? throw new ArgumentNullException(nameof(aiPlanCoordinator));
        _videos = videos ?? throw new ArgumentNullException(nameof(videos));
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _jobKinds = jobKinds ?? throw new ArgumentNullException(nameof(jobKinds));
        _jobMonitor = jobMonitor ?? throw new ArgumentNullException(nameof(jobMonitor));
        _editViewModel = editViewModel;
        Usage = new AiUsageViewModel(_entitlements.Entitlements).DisposeWith(_disposables);
        ModelPicker = new AiModelPickerViewModel(_modelCatalog, _entitlements)
            .DisposeWith(_disposables);
        PromptLibrary = new AiPromptLibraryViewModel(
                PromptTaskKind.Video,
                ComposePrompt,
                prompt => Prompt.Value = prompt)
            .DisposeWith(_disposables);

        Replace(
            DurationOptions,
            DefaultDurations.Select(seconds => new AiVideoDurationOption(seconds)));
        SelectedDuration = new ReactivePropertySlim<AiVideoDurationOption>(DurationOptions[1])
            .DisposeWith(_disposables);
        // The lengths a model takes are a short, unevenly spaced list, so the
        // slider walks that list by index instead of pretending every second in
        // between can be asked for.
        DurationIndex = new ReactivePropertySlim<int>(DurationOptions.IndexOf(SelectedDuration.Value))
            .DisposeWith(_disposables);
        MaxDurationIndex = new ReactivePropertySlim<int>(DurationOptions.Count - 1)
            .DisposeWith(_disposables);
        SelectedDuration.Subscribe(option => DurationIndex.Value = IndexOfDuration(option))
            .DisposeWith(_disposables);
        DurationIndex.Subscribe(index =>
        {
            if (index >= 0 && index < DurationOptions.Count)
                SelectedDuration.Value = DurationOptions[index];
        }).DisposeWith(_disposables);
        _availabilityTracker = new AiOperationAvailabilityTracker(
            _availability,
            _availabilityLifetimeCts.Token);
        SelectedDuration.Subscribe(option =>
                _availabilityTracker.Check(new AiOperationAvailabilityRequest.Video(
                    option.Seconds,
                    ModelPicker.SelectedModel)))
            .DisposeWith(_disposables);
        // A dearer model can put the same clip out of reach, so the estimate
        // has to be re-asked when the choice changes.
        ModelPicker.Selected.Subscribe(_ =>
                _availabilityTracker.Check(new AiOperationAvailabilityRequest.Video(
                    SelectedDuration.Value.Seconds,
                    ModelPicker.SelectedModel)))
            .DisposeWith(_disposables);
        EstimatedUsage = new AiUsageEstimateViewModel(
                Usage,
                _availabilityTracker.State)
            .DisposeWith(_disposables);

        Replace(
            ResolutionOptions,
            DefaultResolutions.Select(value => new AiVideoResolutionOption(value)));
        SelectedResolution = new ReactivePropertySlim<AiVideoResolutionOption>(ResolutionOptions[0])
            .DisposeWith(_disposables);

        // Resolution says how many pixels; this says what shape they are in.
        // Without it a vertical clip could not be asked for at all.
        Replace(
            AspectRatioOptions,
            DefaultAspectRatios.Select(value => new AiVideoAspectRatioOption(value)));
        SelectedAspectRatio = new ReactivePropertySlim<AiVideoAspectRatioOption>(
                GetSuggestedAspectRatio(AspectRatioOptions, editViewModel?.Scene.FrameSize))
            .DisposeWith(_disposables);
        // On by default: the model produces sound and the plan is priced for it.
        GenerateAudio = new ReactivePropertySlim<bool>(true)
            .DisposeWith(_disposables);
        SupportsAudio = new ReactivePropertySlim<bool>(true)
            .DisposeWith(_disposables);
        SupportsSeed = new ReactivePropertySlim<bool>(true)
            .DisposeWith(_disposables);
        Seed = new ReactivePropertySlim<int?>()
            .DisposeWith(_disposables);
        // A model that cannot be given a shape must not be offered one, so the
        // lists are rebuilt from whichever model is chosen.
        ModelPicker.Filter = model =>
            model.Video is not { } video || video.CanServeAnything();
        ModelPicker.Selected.Subscribe(option => ApplyModelCapabilities(option?.Model))
            .DisposeWith(_disposables);

        IsGenerating = new ReactivePropertySlim<bool>(false)
            .DisposeWith(_disposables);
        IsWaitingForJob = new ReactivePropertySlim<bool>(false)
            .DisposeWith(_disposables);

        PromptValidationError = Prompt
            .CombineLatest(
                Style,
                Composition,
                Motion,
                Exclusions,
                (prompt, style, composition, motion, exclusions) =>
                    AiPromptComposer.GetValidationError(new AiPromptParts(
                        prompt,
                        style,
                        composition,
                        motion,
                        exclusions)))
            .ToReadOnlyReactivePropertySlim(Strings.AiPromptRequired)
            .DisposeWith(_disposables);

        VisiblePromptValidationError = AiPromptValidation
            .WhileTyping(PromptValidationError, Prompt, Style, Composition, Motion, Exclusions)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        SelectFirstFrame = new AsyncReactiveCommand()
            .WithSubscribe(() => SelectFrameAsync(isFirstFrame: true));
        SelectLastFrame = new AsyncReactiveCommand()
            .WithSubscribe(() => SelectFrameAsync(isFirstFrame: false));
        CaptureCurrentFrame = new AsyncReactiveCommand()
            .WithSubscribe(CaptureCurrentFrameAsync);
        ClearFirstFrame = new ReactiveCommand();
        ClearFirstFrame.Subscribe(() => SetFrame(isFirstFrame: true, null)).DisposeWith(_disposables);
        ClearLastFrame = new ReactiveCommand();
        ClearLastFrame.Subscribe(() => SetFrame(isFirstFrame: false, null)).DisposeWith(_disposables);

        CanGenerate = PromptValidationError
            .CombineLatest(IsGenerating, (error, generating) => error is null && !generating)
            .CombineLatest(
                FirstFramePath,
                LastFramePath,
                (canGenerate, firstFrame, lastFrame) =>
                    canGenerate && (string.IsNullOrEmpty(lastFrame) || !string.IsNullOrEmpty(firstFrame)))
            .CombineLatest(EstimatedUsage.CanAfford, (canGenerate, canAfford) => canGenerate && canAfford)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        Generate = new AsyncReactiveCommand(CanGenerate)
            .WithSubscribe(GenerateCore);
        StopGenerating = new ReactiveCommand(IsGenerating);
        StopGenerating.Subscribe(StopGeneratingCore).DisposeWith(_disposables);

        CanAddToScene = ResultVideoPath
            .Select(x => x != null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        AddToScene = new AsyncReactiveCommand(CanAddToScene)
            .WithSubscribe(AddToSceneCore);

        SaveToFile = new AsyncReactiveCommand(CanAddToScene)
            .WithSubscribe(SaveToFileCore);

        OpenResult = new ReactiveCommand(CanAddToScene);
        OpenResult.Subscribe(OpenResultCore).DisposeWith(_disposables);

        OpenAiPlan = new ReactiveCommand();
        OpenAiPlan.Subscribe(aiPlanCoordinator.OpenAiPlan).DisposeWith(_disposables);

        ShowJoinPro = Usage.HasSnapshot
            .CombineLatest(Usage.CanUseAi, (hasSnapshot, canUseAi) => hasSnapshot && !canUseAi)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        CoreObject? selectedObject = editViewModel?.GetService<IEditorSelection>()?.SelectedObject.Value;
        SetFrame(
            isFirstFrame: true,
            AiImageEditDialogViewModel.GetSelectedImageSourcePath(selectedObject),
            selectedObject is Element selectedElement ? selectedElement.Id.ToString("N") : null);

        _ = LoadEntitlementsAsync();
    }

    /// <summary>
    /// The lengths on offer, which follow the chosen model: Veo 3.1 takes 4, 6
    /// or 8 seconds and nothing between, MiniMax H3 nothing under five. A model
    /// that publishes none leaves the list this client has always offered.
    /// </summary>
    public ObservableCollection<AiVideoDurationOption> DurationOptions { get; } = [];

    public ReactivePropertySlim<AiVideoDurationOption> SelectedDuration { get; }

    /// <summary>Where the duration slider sits in <see cref="DurationOptions"/>.</summary>
    public ReactivePropertySlim<int> DurationIndex { get; }

    /// <summary>The last index <see cref="DurationOptions"/> currently holds.</summary>
    public ReactivePropertySlim<int> MaxDurationIndex { get; }

    public ObservableCollection<AiVideoResolutionOption> ResolutionOptions { get; } = [];

    public ReactivePropertySlim<AiVideoResolutionOption> SelectedResolution { get; }

    public ObservableCollection<AiVideoAspectRatioOption> AspectRatioOptions { get; } = [];

    public ReactivePropertySlim<AiVideoAspectRatioOption> SelectedAspectRatio { get; }

    public ReactivePropertySlim<bool> GenerateAudio { get; }

    /// <summary>
    /// False for a model that produces no sound, or takes no seed. The controls
    /// are hidden rather than left to fail: a request carrying either would be
    /// refused, and a switch that does nothing is worse than none.
    /// </summary>
    public ReactivePropertySlim<bool> SupportsAudio { get; }

    public ReactivePropertySlim<bool> SupportsSeed { get; }

    /// <summary>
    /// Repeating a seed with the same prompt reproduces the same clip. Null
    /// leaves the choice to the server, which is a different clip every run.
    /// </summary>
    public ReactivePropertySlim<int?> Seed { get; }

    public decimal SeedMinimum => AiRequestLimits.MinSeed;

    public decimal SeedMaximum => AiRequestLimits.MaxSeed;

    /// <summary>
    /// What this client asks for when the server says nothing about a model —
    /// the lists it offered before models could publish their own. The server
    /// accepts more than these; a shape it would take but this dialog has no
    /// control for is simply not offered.
    /// </summary>
    private static readonly int[] DefaultDurations = [4, 6, 8];

    private static readonly string[] DefaultResolutions = ["720p", "1080p"];

    private static readonly string[] DefaultAspectRatios = ["16:9", "9:16"];

    /// <summary>
    /// Rebuilds the lists around the chosen model, keeping each selection where
    /// the model still takes it. A length is snapped to the nearest on offer
    /// rather than reset: a model that takes 6 but not 5 should land on 6, and
    /// leaving 5 in place would be charged for and then refused.
    /// </summary>
    private void ApplyModelCapabilities(AiModelOption? model)
    {
        AiVideoModelCapabilities video = model?.Video ?? AiVideoModelCapabilities.Unrestricted;

        // The model's own lists, already narrowed to what the server accepts.
        // The client's own are a fallback for a server that publishes none.
        IEnumerable<int> durations = video.DurationsSeconds.IsDefaultOrEmpty
            ? DefaultDurations
            : video.DurationsSeconds;
        Replace(DurationOptions, durations.Select(seconds => new AiVideoDurationOption(seconds)));
        SelectedDuration.Value = NearestDuration(SelectedDuration.Value, DurationOptions);
        MaxDurationIndex.Value = DurationOptions.Count - 1;
        DurationIndex.Value = IndexOfDuration(SelectedDuration.Value);

        IEnumerable<string> resolutions = video.Resolutions.IsDefaultOrEmpty
            ? DefaultResolutions
            : video.Resolutions;
        Replace(ResolutionOptions, resolutions.Select(value => new AiVideoResolutionOption(value)));
        SelectedResolution.Value =
            ResolutionOptions.FirstOrDefault(option => option == SelectedResolution.Value)
            ?? ResolutionOptions[0];

        IEnumerable<string> aspectRatios = video.AspectRatios.IsDefaultOrEmpty
            ? DefaultAspectRatios
            : video.AspectRatios;
        Replace(
            AspectRatioOptions,
            aspectRatios.Select(value => new AiVideoAspectRatioOption(value)));
        SelectedAspectRatio.Value =
            AspectRatioOptions.FirstOrDefault(option => option == SelectedAspectRatio.Value)
            // The shape it would have started on, which is the one nearest the
            // scene rather than whichever the model happens to list first.
            ?? GetSuggestedAspectRatio(AspectRatioOptions, _editViewModel?.Scene.FrameSize);

        SupportsAudio.Value = video.SupportsAudio;
        if (!video.SupportsAudio)
            GenerateAudio.Value = false;
        SupportsSeed.Value = video.SupportsSeed;
        if (!video.SupportsSeed)
            Seed.Value = null;
        // A model conditions on the frames it publishes, and one of the two is
        // not the other. A picker left up for a frame the model does not take
        // only produces a request refused after the shape has been checked.
        //
        // A last frame is only ever sent alongside a first one — the endpoint
        // takes no request without one — so a model that publishes a last frame
        // and no first frame can be given neither.
        SupportsFirstFrame.Value = video.SupportsFirstFrame;
        SupportsLastFrame.Value = video.SupportsFirstFrame && video.SupportsLastFrame;
        SupportsFrameGuidance.Value = SupportsFirstFrame.Value;
        // Cleared the way the button clears it, so the preview goes with the
        // path and the temporary file it was captured into is released.
        if (!SupportsFirstFrame.Value)
            SetFrameCore(isFirstFrame: true, null, null);
        if (!SupportsLastFrame.Value)
            SetFrameCore(isFirstFrame: false, null, null);
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (T value in values)
            target.Add(value);
    }

    private int IndexOfDuration(AiVideoDurationOption option)
        => Math.Max(0, DurationOptions.IndexOf(option));

    private static AiVideoDurationOption NearestDuration(
        AiVideoDurationOption current,
        IReadOnlyList<AiVideoDurationOption> options)
    {
        AiVideoDurationOption nearest = options[0];
        foreach (AiVideoDurationOption option in options)
        {
            if (Math.Abs(option.Seconds - current.Seconds)
                < Math.Abs(nearest.Seconds - current.Seconds))
            {
                nearest = option;
            }
        }

        return nearest;
    }

    internal static AiVideoAspectRatioOption GetSuggestedAspectRatio(
        IReadOnlyList<AiVideoAspectRatioOption> options,
        Beutl.Media.PixelSize? frameSize)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Count == 0)
        {
            throw new ArgumentException(
                "At least one aspect ratio option is required.",
                nameof(options));
        }

        string ratio = AiAspectRatioSuggestion.Nearest(
            options.Select(option => option.Value).ToArray(),
            frameSize,
            "16:9");
        return options.FirstOrDefault(option => option.Value == ratio) ?? options[0];
    }

    public ReactivePropertySlim<string> Prompt { get; } = new();

    public ReactivePropertySlim<string> Style { get; } = new();

    public ReactivePropertySlim<string> Composition { get; } = new();

    public ReactivePropertySlim<string> Motion { get; } = new();

    public ReactivePropertySlim<string> Exclusions { get; } = new();

    /// <summary>Whether the chosen model conditions on a starting frame.</summary>
    public ReactivePropertySlim<bool> SupportsFirstFrame { get; } = new(true);

    /// <summary>Whether it conditions on an ending one, which is not the same.</summary>
    public ReactivePropertySlim<bool> SupportsLastFrame { get; } = new(true);

    /// <summary>Whether it takes either, and the section is worth showing.</summary>
    public ReactivePropertySlim<bool> SupportsFrameGuidance { get; } = new(true);

    public ReactivePropertySlim<string?> FirstFramePath { get; } = new();

    public ReactivePropertySlim<string?> LastFramePath { get; } = new();

    public ReactivePropertySlim<Ref<Bitmap>?> FirstFramePreview { get; } = new();

    public ReactivePropertySlim<Ref<Bitmap>?> LastFramePreview { get; } = new();

    public AsyncReactiveCommand SelectFirstFrame { get; }

    public AsyncReactiveCommand SelectLastFrame { get; }

    public AsyncReactiveCommand CaptureCurrentFrame { get; }

    public ReactiveCommand ClearFirstFrame { get; }

    public ReactiveCommand ClearLastFrame { get; }

    public ReactivePropertySlim<bool> IsGenerating { get; }

    public ReactivePropertySlim<bool> IsWaitingForJob { get; }

    public ReadOnlyReactivePropertySlim<string?> PromptValidationError { get; }

    /// <summary>
    /// The same message, held back until the person has typed something.
    /// </summary>
    public ReadOnlyReactivePropertySlim<string?> VisiblePromptValidationError { get; }

    public ReadOnlyReactivePropertySlim<bool> CanGenerate { get; }

    public AsyncReactiveCommand Generate { get; }

    /// <summary>
    /// Abandons the run in flight, from the moment it is submitted until the
    /// result lands, so a wrong prompt does not mean closing the tab.
    /// </summary>
    public ReactiveCommand StopGenerating { get; }

    public ReadOnlyReactivePropertySlim<bool> CanAddToScene { get; }

    public AsyncReactiveCommand AddToScene { get; }

    public AsyncReactiveCommand SaveToFile { get; }

    public ReactiveCommand OpenResult { get; }

    public ReactiveCommand OpenAiPlan { get; }

    internal IAiPlanCoordinator AiPlanCoordinator => _aiPlanCoordinator;

    internal void RefreshAvailability()
        => _availabilityTracker.Refresh(new AiOperationAvailabilityRequest.Video(
            SelectedDuration.Value.Seconds,
            ModelPicker.SelectedModel));

    public ReactivePropertySlim<string?> ResultVideoPath { get; } = new();

    public ReactivePropertySlim<string> StatusText { get; } = new(Strings.AiVideoIdle);

    internal AiUsageViewModel Usage { get; }

    internal AiModelPickerViewModel ModelPicker { get; }

    internal AiUsageEstimateViewModel EstimatedUsage { get; }

    internal AiPromptLibraryViewModel PromptLibrary { get; }

    internal Func<CancellationToken, Task<Bitmap>>? CurrentFrameRenderer { get; set; }

    internal Func<TimeSpan, CancellationToken, Task> PollDelayAsync { get; set; } = Task.Delay;

    internal TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    internal TimeSpan MaximumTransientPollDelay { get; set; } = TimeSpan.FromSeconds(30);

    public ReadOnlyReactivePropertySlim<bool> ShowJoinPro { get; }

    public ReactivePropertySlim<string?> Error { get; } = new();

    public void Dispose() => _ = BeginDisposeAsync();

    public ValueTask DisposeAsync() => new(BeginDisposeAsync());

    private Task BeginDisposeAsync()
    {
        lock (_disposeGate)
        {
            return _disposeTask ??= DisposeCoreAsync();
        }
    }

    private async Task DisposeCoreAsync()
    {
        _availabilityLifetimeCts.Cancel();
        await _operations.DisposeAsync();

        string[] temporaryFiles;
        CancellationTokenSource? pollingCts;
        lock (_lifetimeGate)
        {
            temporaryFiles = _temporaryFiles.ToArray();
            _temporaryFiles.Clear();
            _temporaryFileLeases.Clear();
            _temporaryFilesPendingDeletion.Clear();
            pollingCts = _pollingCts;
            _pollingCts = null;
        }

        pollingCts?.Dispose();
        Prompt.Dispose();
        Style.Dispose();
        Composition.Dispose();
        Motion.Dispose();
        Exclusions.Dispose();
        FirstFramePreview.Value?.Dispose();
        FirstFramePreview.Value = null;
        LastFramePreview.Value?.Dispose();
        LastFramePreview.Value = null;
        FirstFramePath.Value = null;
        LastFramePath.Value = null;
        FirstFramePreview.Dispose();
        LastFramePreview.Dispose();
        SupportsFirstFrame.Dispose();
        SupportsLastFrame.Dispose();
        SupportsFrameGuidance.Dispose();
        FirstFramePath.Dispose();
        LastFramePath.Dispose();
        Error.Dispose();
        _disposables.Dispose();
        _availabilityTracker.Dispose();
        _availabilityLifetimeCts.Dispose();
        foreach (string path in temporaryFiles)
        {
            DeleteTemporaryFile(path);
        }
    }

    /// <summary>
    /// Re-reads the model list. The catalog is cached with a freshness window,
    /// so this costs nothing while it is fresh and picks up a model an operator
    /// added, removed or reordered once it is not — which a workspace tab left
    /// open would otherwise never see.
    /// </summary>
    internal void RefreshModels() => _ = RefreshModelsAsync();

    private async Task RefreshModelsAsync()
    {
        try
        {
            await ModelPicker.LoadAsync(AiOperations.VideoGeneration, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to reload the AI models for video generation.");
        }
    }

    private async Task LoadEntitlementsAsync()
    {
        using AsyncOperationLifetime.Operation? operation = _operations.TryEnter();
        if (operation is null)
            return;
        try
        {
            await _entitlements.RefreshAsync(operation.CancellationToken);
            await ModelPicker.LoadAsync(
                AiOperations.VideoGeneration,
                operation.CancellationToken);
        }
        catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load AI entitlements.");
        }
    }

    private async Task SelectFrameAsync(bool isFirstFrame)
    {
        using AsyncOperationLifetime.Operation? operation = _operations.TryEnter();
        if (operation is null)
            return;
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime
            { MainWindow: { } window }
            || TopLevel.GetTopLevel(window)?.StorageProvider is not { } storage)
        {
            return;
        }

        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(
            SharedFilePickerOptions.OpenAiVideoFrame());
        if (files.Count > 0)
        {
            operation.TryPublish(() =>
                SetFrameCore(isFirstFrame, files[0].Path.LocalPath, sourceElementId: null));
        }
    }

    private async Task CaptureCurrentFrameAsync()
    {
        using AsyncOperationLifetime.Operation? operation = _operations.TryEnter();
        if (operation is null)
            return;
        if (_editViewModel is null)
            return;

        CancellationToken lifetimeToken = operation.CancellationToken;
        if (!operation.TryPublish(() => Error.Value = null))
            return;

        string? unpublishedPath = null;
        try
        {
            using Bitmap bitmap = CurrentFrameRenderer is { } renderer
                ? await renderer(lifetimeToken)
                : await _editViewModel.Player.DrawFrameAtFullScale();
            lifetimeToken.ThrowIfCancellationRequested();

            lifetimeToken.ThrowIfCancellationRequested();
            (unpublishedPath, FileStream stream) = AiTemporaryFileStore.Create(
                "inputs",
                "frame",
                ".png");
            using (stream)
            {
                bitmap.Save(stream, EncodedImageFormat.Png);
            }
            if (new FileInfo(unpublishedPath).Length > AiRequestLimits.MaxFrameUploadBytes)
                throw new AiFileTooLargeException();
            lifetimeToken.ThrowIfCancellationRequested();

            bool published = operation.TryPublish(() =>
            {
                lock (_lifetimeGate)
                {
                    _temporaryFiles.Add(unpublishedPath);
                    try
                    {
                        SetFrameCore(isFirstFrame: true, unpublishedPath, sourceElementId: null);
                    }
                    catch
                    {
                        _temporaryFiles.Remove(unpublishedPath);
                        throw;
                    }
                }
            });
            if (!published)
                return;

            unpublishedPath = null;
        }
        catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested)
        {
        }
        catch (AiFileTooLargeException)
        {
            operation.TryPublish(() => Error.Value = Strings.AiFileTooLarge);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to capture the current frame for AI video generation.");
            operation.TryPublish(() => Error.Value = Strings.AiVideoFrameCaptureFailed);
        }
        finally
        {
            if (unpublishedPath is not null)
            {
                DeleteTemporaryFile(unpublishedPath);
            }
        }
    }

    private void SetFrame(bool isFirstFrame, string? path, string? sourceElementId = null)
    {
        using AsyncOperationLifetime.Operation? operation = _operations.TryEnter();
        operation?.TryPublish(() => SetFrameCore(isFirstFrame, path, sourceElementId));
    }

    private void SetFrameCore(bool isFirstFrame, string? path, string? sourceElementId)
    {
        if (!string.IsNullOrEmpty(path)
            && File.Exists(path)
            && new FileInfo(path).Length > AiRequestLimits.MaxFrameUploadBytes)
        {
            Error.Value = Strings.AiFileTooLarge;
            return;
        }

        ReactivePropertySlim<string?> pathProperty = isFirstFrame ? FirstFramePath : LastFramePath;
        ReactivePropertySlim<Ref<Bitmap>?> previewProperty = isFirstFrame ? FirstFramePreview : LastFramePreview;
        string? previousPath = pathProperty.Value;
        previewProperty.Value?.Dispose();
        previewProperty.Value = null;
        pathProperty.Value = path;
        if (isFirstFrame)
        {
            _firstFrameElementId = sourceElementId;
        }
        else
        {
            _lastFrameElementId = sourceElementId;
        }

        if (!string.Equals(previousPath, path, StringComparison.Ordinal))
        {
            RequestTemporaryFileDeletion(previousPath);
        }

        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return;

        try
        {
            previewProperty.Value = Ref<Bitmap>.Create(Bitmap.FromFile(path));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load an AI video frame preview from {Path}", path);
            Error.Value = Strings.AiEditSourcePreviewFailed;
        }
    }

    private void OpenResultCore()
    {
        using AsyncOperationLifetime.Operation? operation = _operations.TryEnter();
        if (operation is null)
            return;
        if (ResultVideoPath.Value is not { } path || !File.Exists(path))
            return;

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "open" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open the generated AI video preview.");
            operation.TryPublish(() => Error.Value = Strings.AiVideoPreviewOpenFailed);
        }
    }

    private async Task GenerateCore()
    {
        using AsyncOperationLifetime.Operation? operation = _operations.TryEnter();
        if (operation is null)
            return;
        if (!operation.TryPublish(() =>
            {
                Error.Value = null;
                IsGenerating.Value = true;
                StatusText.Value = Strings.AiVideoSubmitting;
            }))
        {
            return;
        }

        _runningRequest = operation;
        bool persistedServerJob = false;
        try
        {
            string prompt = ComposePrompt();
            int durationSeconds = SelectedDuration.Value.Seconds;
            string resolution = SelectedResolution.Value.Value;
            string aspectRatio = SelectedAspectRatio.Value.Value;
            bool generateAudio = GenerateAudio.Value;
            string? firstFramePath = FirstFramePath.Value;
            string? lastFramePath = LastFramePath.Value;
            string? firstFrameElementId = _firstFrameElementId;
            string? lastFrameElementId = _lastFrameElementId;
            using IDisposable firstFrameLease = AcquireTemporaryFileLease(firstFramePath);
            using IDisposable lastFrameLease = AcquireTemporaryFileLease(lastFramePath);
            AiModelId? model = ModelPicker.SelectedModel;
            AiRequestName name = _requestKey.NameFor(
                prompt,
                durationSeconds.ToString(CultureInfo.InvariantCulture),
                resolution,
                aspectRatio,
                generateAudio ? "audio" : "silent",
                Seed.Value?.ToString(CultureInfo.InvariantCulture),
                model?.Value,
                AiRequestKey.FileStamp(firstFramePath),
                AiRequestKey.FileStamp(lastFramePath));
            // Not for a repeat: the server looks up the job this name already
            // made before it looks at the balance, so refusing here would refuse
            // to collect something already paid for.
            if (!name.IsRepeat
                && !await _availabilityTracker.CheckNowAsync(
                    new AiOperationAvailabilityRequest.Video(durationSeconds, model),
                    operation.CancellationToken))
            {
                throw new AiUsageLimitExceededException();
            }
            AiVideoGenerationResult response = await _videos.CreateAsync(
                new AiVideoGenerationRequest(
                    prompt,
                    durationSeconds,
                    new AiVideoResolutionId(resolution),
                    new AiVideoAspectRatioId(aspectRatio),
                    generateAudio,
                    seed: Seed.Value,
                    firstFrame: firstFramePath is null
                        ? null
                        : AiUploadSource.FromFile(firstFramePath),
                    lastFrame: lastFramePath is null
                        ? null
                        : AiUploadSource.FromFile(lastFramePath),
                    model: model,
                    idempotencyKey: name.Key),
                operation.CancellationToken);
            persistedServerJob = true;

            var pendingSnapshot = new AiVideoResultSnapshot(durationSeconds);
            if (!operation.TryPublish(() =>
                {
                    PromptLibrary.Record(prompt);
                }))
            {
                return;
            }

            // Retired only once the server has settled the job. A clip whose
            // result never reached the client is still recoverable under the key
            // that created it, and asking again under a new one would pay twice.
            if (await PollJobAsync(response.JobId, operation, pendingSnapshot))
            {
                _requestKey.Retire();
            }
        }
        catch (AuthenticationRequiredException)
        {
            operation.TryPublish(() => Error.Value = Strings.AiAuthenticationRequired);
        }
        catch (AiPlanRequiredException)
        {
            operation.TryPublish(() => Error.Value = Strings.AiProRequired);
        }
        catch (AiUsageLimitExceededException)
        {
            operation.TryPublish(() => Error.Value = Strings.AiUsageLimitExceeded);
        }
        // Charged for and still the server's; asking again under the same name
        // is what recovers it, so the key stays.
        catch (AiResultUnavailableException)
        {
            operation.TryPublish(() => Error.Value = Strings.AiResultUnavailable);
        }
        catch (AiModelUnavailableException)
        {
            _requestKey.Retire();
            operation.TryPublish(() => Error.Value = Strings.AiModelUnavailable);
        }
        // Refused before the operation was reserved, so nothing was charged;
        // what has to change is the model or the shape of the request.
        catch (AiModelDoesNotSupportRequestException)
        {
            operation.TryPublish(() => Error.Value = Strings.AiModelDoesNotSupportRequest);
        }
        catch (AiProviderErrorException)
        {
            _requestKey.Retire();
            operation.TryPublish(() => Error.Value = Strings.AiProviderError);
        }
        // Reachable because a request keeps its name across attempts: asking
        // again for one the server is still working on is how its result is
        // recovered rather than bought twice, and until it finishes the answer
        // is this. The key stays — it is still the way back to that job.
        catch (AiRequestInProgressException)
        {
            operation.TryPublish(() => Error.Value = Strings.AiRequestInProgress);
        }
        // The job that key created is gone, so the key can only ever answer
        // with that. The next attempt has to be a new request.
        catch (AiRequestWasDeletedException)
        {
            _requestKey.Retire();
            operation.TryPublish(() => Error.Value = Strings.AiRequestWasDeleted);
        }
        catch (AiJobLimitReachedException)
        {
            operation.TryPublish(() => Error.Value = Strings.AiVideoJobLimitReached);
        }
        catch (AiFileTooLargeException)
        {
            operation.TryPublish(() => Error.Value = Strings.AiFileTooLarge);
        }
        catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested)
        {
            if (persistedServerJob)
                await RefreshJobHistoryAfterLocalStopAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate AI video.");
            operation.TryPublish(() => Error.Value = Strings.AiUnexpectedError);
        }
        finally
        {
            _runningRequest = null;
            operation.TryPublish(() => IsGenerating.Value = false);
        }
    }

    /// <summary>Waits for the job to finish, showing what it is doing.</summary>
    /// <returns>
    /// True once the server has settled the job — the clip is in hand, or the
    /// job failed and was refunded. False while its outcome is still unknown to
    /// this client, which is what makes the request worth repeating as itself.
    /// </returns>
    private async Task<bool> PollJobAsync(
        AiJobId jobId,
        AsyncOperationLifetime.Operation operation,
        AiVideoResultSnapshot pendingSnapshot)
    {
        CancellationTokenSource pollingCts;
        lock (_lifetimeGate)
        {
            _pollingCts?.Cancel();
            _pollingCts?.Dispose();
            pollingCts = CancellationTokenSource.CreateLinkedTokenSource(operation.CancellationToken);
            _pollingCts = pollingCts;
        }
        CancellationToken token = pollingCts.Token;
        operation.TryPublish(() => IsWaitingForJob.Value = true);

        int transientFailures = 0;
        try
        {
            while (!token.IsCancellationRequested)
            {
                AiVideoJob job;
                try
                {
                    job = await _videos.GetAsync(jobId, token);
                    transientFailures = 0;
                }
                catch (Exception ex) when (IsTransientPollingFailure(ex))
                {
                    transientFailures++;
                    operation.TryPublish(() => StatusText.Value = Strings.AiVideoProcessing);
                    await PollDelayAsync(GetTransientPollDelay(transientFailures), token);
                    continue;
                }
                AiJobStatusSemantics status = _jobKinds.GetStatus(AiJobKinds.Video, job.Status);
                if (status.Outcome == AiJobOutcomes.Succeeded)
                {
                    if (job.ContentUri is not { } contentUri)
                    {
                        throw new InvalidOperationException("A successful video job did not provide content.");
                    }

                    string? localPath = await DownloadVideoAsync(
                        contentUri,
                        job.ContentMetadata,
                        operation);
                    if (localPath is null)
                        return false;

                    operation.TryPublish(() =>
                    {
                        string? previousPath = ResultVideoPath.Value;
                        StatusText.Value = Strings.AiVideoCompleted;
                        ResultVideoPath.Value = localPath;
                        _resultSnapshot = pendingSnapshot;
                        if (!string.Equals(previousPath, localPath, StringComparison.Ordinal))
                        {
                            RequestTemporaryFileDeletion(previousPath);
                        }
                    });
                    return true;
                }
                if (status.IsTerminal || !status.ShouldPoll)
                {
                    operation.TryPublish(() =>
                    {
                        StatusText.Value = Strings.AiVideoFailed;
                        Error.Value = job.Error ?? Strings.AiProviderError;
                    });
                    return true;
                }

                operation.TryPublish(() => StatusText.Value = Strings.AiVideoProcessing);
                await PollDelayAsync(PollInterval, token);
            }
        }
        catch (OperationCanceledException) when (
            pollingCts.IsCancellationRequested
            && !operation.CancellationToken.IsCancellationRequested)
        {
            await RefreshJobHistoryAfterLocalStopAsync();
            operation.TryPublish(() => StatusText.Value = Strings.AiVideoWaitStopped);
        }
        finally
        {
            operation.TryPublish(() => IsWaitingForJob.Value = false);
            lock (_lifetimeGate)
            {
                if (ReferenceEquals(_pollingCts, pollingCts))
                    _pollingCts = null;
            }

            pollingCts.Dispose();
        }

        // Stopped waiting rather than heard an answer: the job is still the
        // server's, and the key that created it is still the way back to it.
        return false;
    }

    private async Task<string?> DownloadVideoAsync(
        Uri contentUri,
        AiContentMetadata? declaredMetadata,
        AsyncOperationLifetime.Operation operation)
    {
        string? filePath = null;
        try
        {
            (string stagingPath, FileStream destination) = AiTemporaryFileStore.Create(
                "results",
                "ai-video",
                ".download");
            filePath = stagingPath;
            AiContentDownload download;
            await using (destination)
            {
                download = await _content.CopyToAsync(
                    contentUri,
                    destination,
                    operation.CancellationToken);
            }
            AiContentMetadata? metadata = AiContentMetadata.Combine(
                declaredMetadata,
                download.Metadata);
            string extension = metadata?.GetFileExtension(".mp4", "video") ?? ".mp4";
            string completedPath = Path.ChangeExtension(stagingPath, extension);
            File.Move(stagingPath, completedPath);
            AiTemporaryFileStore.EnsurePrivateFile(completedPath);
            filePath = completedPath;

            if (!operation.TryPublish(() =>
                {
                    lock (_lifetimeGate)
                    {
                        _temporaryFiles.Add(filePath);
                    }
                }))
            {
                DeleteTemporaryFile(filePath);
                return null;
            }

            return filePath;
        }
        catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested)
        {
            if (filePath is not null)
            {
                DeleteTemporaryFile(filePath);
            }

            throw;
        }
        // The clip was rendered and charged for; only fetching it failed, and it
        // is still waiting in the job history.
        catch (AiContentUnavailableException ex)
        {
            if (filePath is not null)
            {
                DeleteTemporaryFile(filePath);
            }
            _logger.LogError(ex, "Failed to download the AI video.");
            operation.TryPublish(() => Error.Value = Strings.AiResultDownloadFailed);
            return null;
        }
        catch (Exception ex)
        {
            if (filePath is not null)
            {
                DeleteTemporaryFile(filePath);
            }
            _logger.LogError(ex, "Failed to download the AI video.");
            operation.TryPublish(() => Error.Value = Strings.AiUnexpectedError);
            return null;
        }
    }

    // A job the server already accepted keeps running and lands in the job centre,
    // so stopping means stopping the wait. Before that there is nothing to keep and
    // the request itself goes.
    private void StopGeneratingCore()
    {
        CancellationTokenSource? polling;
        lock (_lifetimeGate)
        {
            polling = _pollingCts;
        }

        if (polling is { IsCancellationRequested: false })
        {
            polling.Cancel();
            return;
        }

        _runningRequest?.Cancel();
    }

    private async Task RefreshJobHistoryAfterLocalStopAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _jobMonitor.RefreshAsync(timeout.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Failed to refresh AI job history after stopping a local wait.");
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static bool IsTransientPollingFailure(Exception exception)
        => exception is HttpRequestException
            || exception is AiException { IsTransient: true };

    private TimeSpan GetTransientPollDelay(int failureCount)
    {
        double multiplier = Math.Pow(2, Math.Min(failureCount - 1, 10));
        double milliseconds = Math.Min(
            PollInterval.TotalMilliseconds * multiplier,
            MaximumTransientPollDelay.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
    }

    private IDisposable AcquireTemporaryFileLease(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return Disposable.Empty;

        lock (_lifetimeGate)
        {
            if (!_temporaryFiles.Contains(path))
                return Disposable.Empty;

            _temporaryFileLeases[path] = _temporaryFileLeases.GetValueOrDefault(path) + 1;
        }

        return Disposable.Create(() => ReleaseTemporaryFileLease(path));
    }

    private void ReleaseTemporaryFileLease(string path)
    {
        lock (_lifetimeGate)
        {
            if (!_temporaryFileLeases.TryGetValue(path, out int count))
                return;

            if (count > 1)
            {
                _temporaryFileLeases[path] = count - 1;
                return;
            }

            _temporaryFileLeases.Remove(path);
            if (_temporaryFilesPendingDeletion.Contains(path))
            {
                DeleteTrackedTemporaryFile(path);
            }
        }
    }

    private void RequestTemporaryFileDeletion(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        lock (_lifetimeGate)
        {
            if (!_temporaryFiles.Contains(path))
                return;

            _temporaryFilesPendingDeletion.Add(path);
            if (!_temporaryFileLeases.ContainsKey(path))
            {
                DeleteTrackedTemporaryFile(path);
            }
        }
    }

    // Called with _lifetimeGate held so a new lease cannot race with deletion.
    private void DeleteTrackedTemporaryFile(string path)
    {
        if (DeleteTemporaryFile(path))
        {
            _temporaryFiles.Remove(path);
            _temporaryFilesPendingDeletion.Remove(path);
        }
    }

    private bool DeleteTemporaryFile(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to remove temporary AI file {Path}", path);
            return false;
        }
    }

    private async Task AddToSceneCore()
    {
        using AsyncOperationLifetime.Operation? operation = _operations.TryEnter();
        if (operation is null)
            return;
        if (_editViewModel == null || ResultVideoPath.Value is not { } filePath)
            return;
        using IDisposable fileLease = AcquireTemporaryFileLease(filePath);

        try
        {
            TimeSpan start = _editViewModel.Player.CurrentFrame.Value;
            int layer = _editViewModel.Scene.Children
                .Where(item => item.Start <= start && start < item.Range.End)
                .Select(item => item.ZIndex)
                .DefaultIfEmpty(-1)
                .Max() + 1;
            int durationSeconds = _resultSnapshot?.DurationSeconds ?? SelectedDuration.Value.Seconds;
            var importer = new AiResultImporter(
                _editViewModel.Scene,
                _editViewModel.GetRequiredService<IElementAdder>());
            ElementAddResult result = await importer.ImportVideoAsync(
                filePath,
                new AiResultImportOptions(
                    start,
                    TimeSpan.FromSeconds(durationSeconds),
                    layer,
                    Strings.AiVideoGeneration),
                operation.CancellationToken);

            if (result.Failure is LockedElementLayerFailure)
            {
                operation.TryPublish(() =>
                    NotificationService.ShowWarning(Strings.Lock, Strings.LayerIsLocked));
                return;
            }
            EnsureImportSucceeded(result);
            if (result.IsSuccess)
            {
                operation.TryPublish(() =>
                    NotificationService.ShowSuccess(Strings.AiVideoGeneration, Strings.AiVideoAddedToScene));
            }
        }
        catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add the AI video to the scene.");
            operation.TryPublish(() => Error.Value = Strings.AiUnexpectedError);
        }

    }

    private static void EnsureImportSucceeded(ElementAddResult result)
    {
        if (result.IsSuccess)
            return;
        throw new InvalidOperationException(
            $"Failed to add the generated video: {result.Failure?.Id}.",
            result.Failure?.Exception);
    }

    private async Task SaveToFileCore()
    {
        using AsyncOperationLifetime.Operation? operation = _operations.TryEnter();
        if (operation is null)
            return;
        if (ResultVideoPath.Value is not { } filePath)
            return;
        using IDisposable fileLease = AcquireTemporaryFileLease(filePath);

        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime
            { MainWindow: { } window })
            return;

        if (TopLevel.GetTopLevel(window)?.StorageProvider is not { } storage)
            return;

        FilePickerSaveOptions options = SharedFilePickerOptions.SaveVideo();
        options.SuggestedFileName = $"AI Video {DateTime.Now:yyyy-MM-dd HHmmss}";
        options.SuggestedStartLocation = await storage.TryGetWellKnownFolderAsync(WellKnownFolder.Videos);
        options.DefaultExtension = Path.GetExtension(filePath).TrimStart('.');

        IStorageFile? file = await storage.SaveFilePickerAsync(options);
        if (file == null)
            return;

        try
        {
            await using Stream source = File.OpenRead(filePath);
            await using Stream destination = await file.OpenWriteAsync();
            destination.SetLength(0);
            await source.CopyToAsync(destination, operation.CancellationToken);
            operation.TryPublish(() =>
                NotificationService.ShowSuccess(Strings.AiVideoGeneration, Strings.AiVideoSaved));
        }
        catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save the AI video.");
            operation.TryPublish(() => Error.Value = Strings.AiUnexpectedError);
        }
    }

    private string ComposePrompt() => AiPromptComposer.Compose(new AiPromptParts(
        Prompt.Value,
        Style.Value,
        Composition.Value,
        Motion.Value,
        Exclusions.Value));

    private sealed record AiVideoResultSnapshot(int DurationSeconds);

}

public sealed record AiVideoDurationOption(int Seconds)
{
    public override string ToString() => $"{Seconds} {Strings.AiVideoSeconds}";
}

public sealed record AiVideoAspectRatioOption(string Value)
{
    public override string ToString() => Value;
}

public sealed record AiVideoResolutionOption(string Value)
{
    public override string ToString() => Value;
}
