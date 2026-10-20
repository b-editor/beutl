using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Reactive.Disposables;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Editor.Services;
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

internal sealed partial class AiVideoGenerationDialogViewModel : IDisposable, IAsyncDisposable, IAiModelListConsumer,
    IAiRecoverableForm, IAiPlanOffer, IAiResultActions
{
    private readonly CompositeDisposable _disposables = [];
    private readonly AsyncOperationLifetime _operations = new();
    private readonly IdentityOperationLifetime _identityOperations = new();
    private readonly OnceAsyncDisposal _disposal = new();
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
    private readonly AiRequestKey _requestKey;
    private readonly AiRequestRecoveryContext _requestRecoveryContext;
    private readonly CancellationTokenSource _availabilityLifetimeCts = new();
    private readonly EditViewModel? _editViewModel;
    // Preserve the user's choices separately from the displayed values, which are narrowed when
    // the model changes. Restoring the old model must also restore these values or the same intent
    // becomes a different request and no longer reaches the result named by the outstanding key.
    private AiVideoDurationOption? _chosenDuration;
    private AiVideoResolutionOption? _chosenResolution;
    private AiVideoAspectRatioOption? _chosenAspectRatio;
    private bool _chosenAudio = true;
    private int? _chosenSeed;
    private (string? Path, string? ElementId) _chosenFirstFrame;
    private (string? Path, string? ElementId) _chosenLastFrame;
    // True while model constraints update the UI. Those changes are not user choices and must
    // not replace the preserved values.
    private bool _applyingCapabilities;
    private readonly object _lifetimeGate = new();
    private CancellationTokenSource? _pollingCts;
    private IdentityOperationLifetime.Operation? _runningRequest;
    private string? _firstFrameElementId;
    private string? _lastFrameElementId;
    private double _resultDurationSeconds;
    private readonly AiRecoveryState _recovery;

    internal AiVideoGenerationDialogViewModel(
        IAiEntitlementService entitlements,
        IAiOperationAvailabilityService availability,
        IAiModelCatalogService modelCatalog,
        IAiPlanCoordinator aiPlanCoordinator,
        IAiVideoService videos,
        IAuthenticatedContentService content,
        IAiJobKindRegistry jobKinds,
        IAiJobMonitor jobMonitor,
        EditViewModel? editViewModel,
        AiRequestRecoveryContext requestRecoveryContext,
        AiSourceVideoMode? sourceMode = null)
    {
        SourceMode = sourceMode;
        StatusText.Value = InitialStatusText;
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
        _requestRecoveryContext = requestRecoveryContext;
        _requestKey = new(
            recoveryContext: requestRecoveryContext,
            operation: Operation.Value);
        Usage = new AiUsageViewModel(_entitlements.Entitlements).DisposeWith(_disposables);
        ModelPicker = new AiModelPickerViewModel(_modelCatalog, _entitlements)
            .DisposeWith(_disposables);
        _recovery = new AiRecoveryState(
            _requestKey,
            ModelPicker,
            Operation,
            Error,
            new AiRecoveryLogs(
                ex => _logger.LogError(ex, "Failed to read video-generation recovery attempts."),
                ex => _logger.LogError(ex, "Failed to clear video-generation state after an account change."),
                ex => _logger.LogWarning(ex, "Failed to abandon video-generation recovery attempt.")),
            TryRecoverPendingAttempt,
            () => ApplyModelCapabilities(ModelPicker.Selected.Value?.Model));
        PromptLibrary = new AiPromptLibraryViewModel(
                PromptTaskKind.Video,
                ComposePrompt,
                prompt => Prompt.Value = prompt,
                recoveryContext: requestRecoveryContext)
            .DisposeWith(_disposables);

        DurationOptions.Replace(
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
        SelectedDuration.Subscribe(option => _availabilityTracker.Check(AvailabilityRequest))
            .DisposeWith(_disposables);
        // A dearer model can put the same clip out of reach, so the estimate
        // has to be re-asked when the choice changes.
        ModelPicker.Selected.Subscribe(_ => _availabilityTracker.Check(AvailabilityRequest))
            .DisposeWith(_disposables);
        EstimatedUsage = new AiUsageEstimateViewModel(
                Usage,
                _availabilityTracker.State)
            .DisposeWith(_disposables);

        ResolutionOptions.Replace(
            DefaultResolutions.Select(value => new AiVideoResolutionOption(value)));
        SelectedResolution = new ReactivePropertySlim<AiVideoResolutionOption>(ResolutionOptions[0])
            .DisposeWith(_disposables);

        // Resolution says how many pixels; this says what shape they are in.
        // Without it a vertical clip could not be asked for at all.
        AspectRatioOptions.Replace(
            DefaultAspectRatios.Select(value => new AiVideoAspectRatioOption(value)));
        SelectedAspectRatio = new ReactivePropertySlim<AiVideoAspectRatioOption>(
                AiAspectRatioSuggestion.Choose(AspectRatioOptions, option => option.Value, editViewModel?.Scene.FrameSize))
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
        ModelPicker.Filter = model => AiGenerativeModelCatalog.IsOffered(Operation.Value, model);
        SelectedDuration.RememberChoice(() => _applyingCapabilities, option => _chosenDuration = option)
            .DisposeWith(_disposables);
        SelectedResolution.RememberChoice(() => _applyingCapabilities, option => _chosenResolution = option)
            .DisposeWith(_disposables);
        SelectedAspectRatio.RememberChoice(() => _applyingCapabilities, option => _chosenAspectRatio = option)
            .DisposeWith(_disposables);
        GenerateAudio.RememberChoice(() => _applyingCapabilities, value => _chosenAudio = value)
            .DisposeWith(_disposables);
        Seed.RememberChoice(() => _applyingCapabilities, seed => _chosenSeed = seed)
            .DisposeWith(_disposables);
        ModelPicker.Selected.Subscribe(option => ApplyModelCapabilities(option?.Model))
            .DisposeWith(_disposables);
        // A load can bring other qualities without moving the selection.
        ModelPicker.IsLoaded.Where(loaded => loaded).Subscribe(_ => RefreshQualities())
            .DisposeWith(_disposables);
        // The shape, and whether frames may guide the clip at all, follow the
        // chosen model; replacing the list under an outstanding name would
        // rewrite the request waiting to be collected.
        ModelPicker.KeepOffered = operation => _requestKey.PersistedModels(operation);
        ModelPicker.CanReload = _ => !_requestKey.HasOutstandingName.Value;

        IsGenerating = new ReactivePropertySlim<bool>(false)
            .DisposeWith(_disposables);

        InitializeVideoInputs();

        PromptValidationError = Prompt
            .CombineLatest(
                Style,
                Composition,
                Motion,
                Exclusions,
                (prompt, style, composition, motion, exclusions) =>
                    IsSourceVideo ? (string.IsNullOrWhiteSpace(prompt) ? Strings.AiPromptRequired : null) : AiPromptComposer.GetValidationError(new AiPromptParts(
                        prompt,
                        style,
                        composition,
                        motion,
                        exclusions)))
            .CombineLatest(MaxPromptLength, (error, max) => error ??
                (ComposePrompt().Length > max ? string.Format(Strings.AiPromptTooLongFormat, max) : null))
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

        CanGenerate = PromptValidationError.CombineLatest(InputError, (prompt, input) => prompt ?? input)
            .CombineLatest(IsGenerating, (error, generating) => error is null && !generating)
            .CombineLatest(HasRequiredInputs, (can, ready) => can && ready)
            .CombineLatest(
                FirstFramePath,
                LastFramePath,
                (canGenerate, firstFrame, lastFrame) =>
                    canGenerate && (string.IsNullOrEmpty(lastFrame) || !string.IsNullOrEmpty(firstFrame)))
            .WhenAffordable(EstimatedUsage.CanAfford, _requestKey.HasOutstandingName)
            .WhenSomeModelUsable(ModelPicker.OffersNothingUsable, _requestKey.HasOutstandingName)
            .WhenModelsLoaded(ModelPicker.IsLoaded)
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

        _requestRecoveryContext.IdentityChanged += OnIdentityChanged;

        if (IsGeneration)
        {
            CoreObject? selectedObject = editViewModel?.GetService<IEditorSelection>()?.SelectedObject.Value;
            SetFrame(
                isFirstFrame: true,
                AiImageEditDialogViewModel.GetSelectedImageSourcePath(selectedObject),
                selectedObject is Element selectedElement ? selectedElement.Id.ToString("N") : null);
        }

        _ = LoadEntitlementsAsync();
        _recovery.TryAutoRecoverSingleAttempt();
    }

    /// <summary>The lengths the chosen model publishes; the client's defaults when it publishes none.</summary>
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
    /// False for a model that produces no sound. The control is hidden rather than left to fail:
    /// a request carrying it would be refused, and a switch that does nothing is worse than none.
    /// </summary>
    public ReactivePropertySlim<bool> SupportsAudio { get; }

    /// <summary>False for a model that takes no seed; the control is hidden for the same reason.</summary>
    public ReactivePropertySlim<bool> SupportsSeed { get; }

    /// <summary>
    /// Repeating a seed with the same prompt reproduces the same clip. Null
    /// leaves the choice to the server, which is a different clip every run.
    /// </summary>
    public ReactivePropertySlim<int?> Seed { get; }

    public decimal SeedMinimum => AiRequestLimits.MinSeed;

    public decimal SeedMaximum => AiRequestLimits.MaxSeed;

    // Where the model sits in the request's parts.
    private const int ModelPartIndex = 6;

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

        _applyingCapabilities = true;
        try
        {
            ApplyModelCapabilitiesCore(video);
            RefreshQualities();
        }
        finally
        {
            _applyingCapabilities = false;
        }
        RefreshVideoInputs();
    }

    private void ApplyModelCapabilitiesCore(AiVideoModelCapabilities video)
    {
        // The model's own lists, already narrowed to what the server accepts.
        // The client's own are a fallback for a server that publishes none.
        IEnumerable<int> durations = CanChooseDuration && video.DurationsSeconds.IsSpecified
            ? video.DurationsSeconds.Values
            : DefaultDurations;
        var availableDurations = durations.ToList();
        if (_recovery.Active?.Form?.DurationSeconds is { } recoveredDuration
            && !availableDurations.Contains(recoveredDuration))
            availableDurations.Add(recoveredDuration);
        DurationOptions.Replace(availableDurations.Select(seconds => new AiVideoDurationOption(seconds)));
        SelectedDuration.Value = NearestDuration(
            _chosenDuration ?? SelectedDuration.Value,
            DurationOptions);
        MaxDurationIndex.Value = DurationOptions.Count - 1;
        DurationIndex.Value = IndexOfDuration(SelectedDuration.Value);

        IEnumerable<string> resolutions = IsGeneration && video.Resolutions.IsSpecified
            ? video.Resolutions.Values
            : DefaultResolutions;
        var availableResolutions = resolutions.ToList();
        if (_recovery.Active?.Form?.Resolution is { } recoveredResolution
            && !availableResolutions.Contains(recoveredResolution, StringComparer.Ordinal))
            availableResolutions.Add(recoveredResolution);
        ResolutionOptions.Replace(availableResolutions.Select(value => new AiVideoResolutionOption(value)));
        SelectedResolution.Value =
            ResolutionOptions.FirstOrDefault(option => option.Value == _chosenResolution?.Value)
            ?? ResolutionOptions[0];

        IEnumerable<string> aspectRatios = IsGeneration && video.AspectRatios.IsSpecified
            ? video.AspectRatios.Values
            : DefaultAspectRatios;
        var availableAspectRatios = aspectRatios.ToList();
        if (_recovery.Active?.Form?.AspectRatio is { } recoveredAspect
            && !availableAspectRatios.Contains(recoveredAspect, StringComparer.Ordinal))
            availableAspectRatios.Add(recoveredAspect);
        AspectRatioOptions.Replace(
            availableAspectRatios.Select(value => new AiVideoAspectRatioOption(value)));
        SelectedAspectRatio.Value =
            AspectRatioOptions.FirstOrDefault(option => option.Value == _chosenAspectRatio?.Value)
            // The shape it would have started on, which is the one nearest the
            // scene rather than whichever the model happens to list first.
            ?? AiAspectRatioSuggestion.Choose(AspectRatioOptions, option => option.Value, _editViewModel?.Scene.FrameSize);

        SupportsAudio.Value = IsGeneration && (_recovery.Active?.Form?.SupportsAudio
            ?? video.SupportsAudio);
        GenerateAudio.Value = SupportsAudio.Value && _chosenAudio;
        SupportsSeed.Value = IsGeneration && (_recovery.Active?.Form?.SupportsSeed
            ?? video.SupportsSeed);
        Seed.Value = SupportsSeed.Value ? _chosenSeed : null;
        // A last frame is only sent with a first one, so a model without first frames gets neither.
        SupportsFirstFrame.Value = IsGeneration && (_recovery.Active?.Form?.SupportsFirstFrame
            ?? video.SupportsFirstFrame);
        SupportsLastFrame.Value = SupportsFirstFrame.Value
            && (_recovery.Active?.Form?.SupportsLastFrame ?? video.SupportsLastFrame);
        // Set aside rather than thrown away: a model that takes no frame is
        // shown none, and going back to one that does puts the same frames back.
        // The temporary file they were captured into is held for as long as a
        // name that points at it is outstanding, so it is still there to go back
        // to.
        SetFrameCore(
            isFirstFrame: true,
            SupportsFirstFrame.Value ? _chosenFirstFrame.Path : null,
            SupportsFirstFrame.Value ? _chosenFirstFrame.ElementId : null);
        SetFrameCore(
            isFirstFrame: false,
            SupportsLastFrame.Value ? _chosenLastFrame.Path : null,
            SupportsLastFrame.Value ? _chosenLastFrame.ElementId : null);
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

    public ReactivePropertySlim<string> Prompt { get; } = new();

    public ReactivePropertySlim<string> Style { get; } = new();

    public ReactivePropertySlim<string> Composition { get; } = new();

    public ReactivePropertySlim<string> Motion { get; } = new();

    public ReactivePropertySlim<string> Exclusions { get; } = new();

    /// <summary>Whether the chosen model conditions on a starting frame.</summary>
    public ReactivePropertySlim<bool> SupportsFirstFrame { get; } = new(true);

    /// <summary>Whether it conditions on an ending one, which is not the same.</summary>
    public ReactivePropertySlim<bool> SupportsLastFrame { get; } = new(true);

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

    public ReactivePropertySlim<AiPendingAttempt?> SelectedRecoveryAttempt => _recovery.SelectedAttempt;

    public ReadOnlyReactivePropertySlim<IReadOnlyList<AiPendingAttempt>> RecoveryAttempts => _recovery.Attempts;

    public ReadOnlyReactivePropertySlim<bool> RecoveryAvailable => _recovery.Available;

    public ReactiveCommand RecoverSelectedAttempt => _recovery.RecoverSelected;

    public ReactiveCommand AbandonSelectedAttempt => _recovery.AbandonSelected;

    public ReadOnlyReactivePropertySlim<bool> CanAddToScene { get; }

    public AsyncReactiveCommand AddToScene { get; }

    public AsyncReactiveCommand SaveToFile { get; }

    public ReactiveCommand OpenResult { get; }

    public ReactiveCommand OpenAiPlan { get; }

    internal IAiPlanCoordinator AiPlanCoordinator => _aiPlanCoordinator;

    internal void RefreshAvailability()
        => _availabilityTracker.Check(AvailabilityRequest);

    private AiOperationAvailabilityRequest.Video AvailabilityRequest
        => new(Operation, RequestDuration, ModelPicker.SelectedModel);

    public ReactivePropertySlim<string?> ResultVideoPath { get; } = new();

    private string InitialStatusText => IsGeneration ? Strings.AiVideoIdle : Strings.AiVideoEditingIdle;

    public ReactivePropertySlim<string> StatusText { get; } = new(Strings.AiVideoIdle);

    internal AiUsageViewModel Usage { get; }

    internal AiModelPickerViewModel ModelPicker { get; }

    internal AiUsageEstimateViewModel EstimatedUsage { get; }

    internal AiPromptLibraryViewModel PromptLibrary { get; }

    internal Func<CancellationToken, Task<Bitmap>>? CurrentFrameRenderer { get; set; }

    // Null in production. Tests can suspend the picker/importer at a precise
    // point while changing the authenticated identity.
    internal Func<CancellationToken, Task<string?>>? FramePicker { get; set; }

    internal Func<CancellationToken, Task<AiSaveFileDestination?>>? SaveFilePicker { get; set; }

    internal Func<string, AiResultImportOptions, CancellationToken, Task<ElementAddResult>>?
        ResultImporter
    { get; set; }

    public ReadOnlyReactivePropertySlim<bool> ShowJoinPro { get; }

    public ReactivePropertySlim<string?> Error { get; } = new();

    public void Dispose() => _ = BeginDisposeAsync();

    public ValueTask DisposeAsync() => new(BeginDisposeAsync());

    private IdentityOperationLifetime.Operation? TryEnterIdentityOperation()
        => _identityOperations.TryEnter(_operations);

    private Task BeginDisposeAsync() => _disposal.Run(DisposeCoreAsync);

    private async Task DisposeCoreAsync()
    {
        _identityOperations.Dispose();
        await _operations.DisposeAsync(
            _availabilityLifetimeCts.Cancel,
            async () =>
        {

            string[] temporaryFiles;
            CancellationTokenSource? pollingCts;
            lock (_lifetimeGate)
            {
                temporaryFiles = _temporaryFiles.ToArray();
                _temporaryFiles.Clear();
                _temporaryFileLeases.Clear();
                _temporaryFilesPendingDeletion.Clear();
                _framesHeldByName.Clear();
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
            FirstFramePath.Dispose();
            LastFramePath.Dispose();
            Error.Dispose();
            _requestRecoveryContext.IdentityChanged -= OnIdentityChanged;
            _requestKey.Dispose();
            _recovery.Dispose();
            _disposables.Dispose();
            _availabilityTracker.Dispose();
            _availabilityLifetimeCts.Dispose();
            foreach (string path in temporaryFiles)
            {
                DeleteTemporaryFile(path);
            }
        });
    }

    /// <inheritdoc />
    public void RefreshModels() => _ = RefreshModelsAsync();

    private async Task RefreshModelsAsync()
    {
        using IdentityOperationLifetime.Operation? operation = TryEnterIdentityOperation();
        if (operation is null)
            return;
        // A clip waiting to be collected is named partly by the model it was
        // sent with, and what the picker allows — the shape, and whether frames
        // may guide it at all — follows that model. Moving the picker under an
        // outstanding name would rename the request and buy it again, so an
        // operator's change waits until the name is settled.
        if (_requestKey.HasOutstandingName.Value)
            return;

        try
        {
            await _entitlements.RefreshAsync(operation.CancellationToken);
            await ModelPicker.LoadAsync(
                Operation,
                operation.CancellationToken);
            _recovery.SelectRecoveredModel();
        }
        catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to reload the AI models for video generation.");
        }
    }

    private async Task LoadEntitlementsAsync()
    {
        using IdentityOperationLifetime.Operation? operation = TryEnterIdentityOperation();
        if (operation is null)
            return;
        try
        {
            await _entitlements.RefreshAsync(operation.CancellationToken);
            // Never under an outstanding name: the model the picker lands on is
            // part of what names the clip waiting to be collected.
            if (!ModelPicker.IsLoaded.Value || !_requestKey.HasOutstandingName.Value)
            {
                await ModelPicker.LoadAsync(
                    Operation,
                    _requestKey.PreferredPersistedModel(Operation),
                    _requestKey.HasExplicitNullPersistedModel(Operation),
                    operation.CancellationToken);
                _recovery.SelectRecoveredModel();
            }
        }
        catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load AI entitlements.");
        }
    }

    private async Task GenerateCore()
    {
        using IdentityOperationLifetime.Operation? operation = TryEnterIdentityOperation();
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
        AiRequestName issued = default;
        AiRequestRecoveryLease? claim = null;
        try
        {
            string prompt = ComposePrompt();
            string promptField = Prompt.Value;
            string style = Style.Value;
            string composition = Composition.Value;
            string motion = Motion.Value;
            string exclusions = Exclusions.Value;
            RefreshVideoInputs();
            if (InputError.Value is { } inputError) throw new VideoInputException(inputError);
            int durationSeconds = RequestDuration;
            string resolution = IsGeneration ? SelectedResolution.Value.Value : string.Empty;
            string aspectRatio = IsGeneration ? SelectedAspectRatio.Value.Value : string.Empty;
            bool generateAudio = GenerateAudio.Value;
            int? seed = Seed.Value;
            string? firstFramePath = FirstFramePath.Value;
            string? lastFramePath = LastFramePath.Value;
            string? firstFrameElementId = _firstFrameElementId;
            string? lastFrameElementId = _lastFrameElementId;
            double? sourceSeconds = SourceDuration.Value;
            string orientation = Orientation.Value.Value;
            string quality = Quality.Value.Value;
            int promptLimit = MaxPromptLength.Value;
            InputSnapshot[] inputs = await ReadVideoInputsAsync(operation.CancellationToken, firstFramePath is null);
            if (IsSourceVideo && _recovery.Active is null)
            {
                sourceSeconds = await ReadSourceSnapshotDurationAsync(
                    inputs.Single(input => input.Role == SourceVideoRole), operation.CancellationToken);
                if (!operation.TryPublish(() => SourceDuration.Value = sourceSeconds))
                    return;
                if (SourceMode == AiSourceVideoMode.Edit)
                    durationSeconds = RequestDuration;
            }
            if (IsSourceVideo)
            {
                // Live paths can change after capture; validate the same bytes that will be uploaded.
                ValidateSourceInputs(inputs.Single(input => input.Role == SourceVideoRole).Upload,
                    inputs.FirstOrDefault(input => input.Role == CharacterImageRole)?.Upload,
                    sourceSeconds, ModelPicker.Selected.Value?.Model.Video ?? AiVideoModelCapabilities.Unrestricted);
            }

            // Read once, and named by that reading. Reading again to send would
            // name one set of bytes and upload another if a frame changed in
            // between, and the answer would be recorded under a name that
            // describes something else.
            AiRequestRecoverySource? existingFirstSource = FindRecoveredFrameSource(FirstFrameRole, firstFramePath);
            AiRequestRecoverySource? existingLastSource = FindRecoveredFrameSource(LastFrameRole, lastFramePath);
            (AiUploadSource? firstFrame, string firstFrameStamp, byte[]? firstFrameBytes, string? firstFrameName) = await ReadFrameAsync(
                firstFramePath,
                operation.CancellationToken,
                existingFirstSource);
            (AiUploadSource? lastFrame, string lastFrameStamp, byte[]? lastFrameBytes, string? lastFrameName) = await ReadFrameAsync(
                lastFramePath,
                operation.CancellationToken,
                existingLastSource);
            string?[] requestParts =
            [
                prompt,
                durationSeconds.ToString(CultureInfo.InvariantCulture),
                resolution,
                aspectRatio,
                generateAudio ? "audio" : "silent",
                seed?.ToString(CultureInfo.InvariantCulture),
                null,
                firstFrameStamp,
                lastFrameStamp,
            ];
            // Video endpoints identify media by content/type, not multipart filenames.
            // Renaming identical input must keep the key for the already-paid request.
            if (inputs.Length > 0 || IsSourceVideo)
                requestParts = requestParts.Concat(new string?[] { null, IsMotionControl ? orientation : null, IsMotionControl ? quality : null }
                    .Concat(inputs.Select(input => input.Role + ":" + input.Upload.MediaType + ":" + AiRequestKey.ContentStamp(input.Bytes)))).ToArray();
            AiModelId? model = _recovery.ModelForRequest(ModelPicker.SelectedModel);
            requestParts[ModelPartIndex] = model?.Value;
            AiRequestFormSnapshot form = new(
                Prompt: promptField,
                Style: style,
                Composition: composition,
                Motion: motion,
                Exclusions: exclusions,
                AspectRatio: aspectRatio,
                Resolution: resolution,
                DurationSeconds: durationSeconds,
                GenerateAudio: generateAudio,
                Seed: seed,
                SupportsAudio: SupportsAudio.Value,
                SupportsSeed: SupportsSeed.Value,
                SupportsFirstFrame: SupportsFirstFrame.Value,
                SupportsLastFrame: SupportsLastFrame.Value,
                FirstFrameElementId: firstFrameElementId,
                LastFrameElementId: lastFrameElementId,
                SourceVideoSeconds: sourceSeconds,
                VideoOrientation: IsMotionControl ? orientation : null,
                VideoQuality: IsMotionControl ? quality : null,
                VideoPromptLimit: promptLimit);
            var recoverySources = new List<AiRequestRecoverySource>(2);
            try
            {
                foreach (InputSnapshot input in inputs)
                    recoverySources.Add(FileAiRequestRecoveryStore.CreateExternalSource(input.Role, input.Path, input.Name, input.Bytes));
                if (firstFramePath is { } firstPath && firstFrame is not null && firstFrameBytes is not null)
                {
                    recoverySources.Add(CreateFrameRecoverySource(
                        FirstFrameRole,
                        firstPath,
                        firstFrameName,
                        firstFrameBytes,
                        firstFrameElementId));
                }
                if (lastFramePath is { } lastPath && lastFrame is not null && lastFrameBytes is not null)
                {
                    recoverySources.Add(CreateFrameRecoverySource(
                        LastFrameRole,
                        lastPath,
                        lastFrameName,
                        lastFrameBytes,
                        lastFrameElementId));
                }
            }
            catch
            {
                _requestKey.CleanupUncommittedSources(recoverySources);
                throw;
            }
            if (_recovery.Active is { } selected
                && !_requestKey.MatchesPending(selected, requestParts))
            {
                _requestKey.CleanupUncommittedSources(recoverySources);
                operation.TryPublish(() => Error.Value = Strings.AiRequestChanged);
                return;
            }
            AiRequestName name = _requestKey.NameFor(requestParts, form, recoverySources);
            issued = name;
            using IDisposable authenticatedScope = _requestKey.EnterAuthenticatedScope(name);
            claim = _requestKey.TryClaim(name);
            if (claim is null)
            {
                operation.TryPublish(() => Error.Value = Strings.AiResultUnavailable);
                return;
            }
            // Held for as long as the name is, not just for as long as the
            // request is in the air. A frame captured from the scene lives in a
            // temporary file, and the request is named partly by that file as it
            // stands — deleted, the request can never be asked for again, and
            // choosing another model is enough to delete it.
            HoldFramesFor(name, firstFramePath, lastFramePath);

            AiVideoGenerationResult response = await AiMeteredDispatch.SendAsync(
                _requestKey,
                name,
                claim,
                token => _availability.CheckAsync(
                    new AiOperationAvailabilityRequest.Video(
                        Operation,
                        durationSeconds,
                        model),
                    token),
                async token => SourceMode is { } mode
                    ? await _videos.CreateFromSourceAsync(new AiSourceVideoRequest(mode, prompt,
                        sourceVideo: inputs.FirstOrDefault(input => input.Role == SourceVideoRole)?.Upload
                            ?? throw new InvalidDataException(Strings.AiVideoInputUnavailable),
                        durationSeconds: mode == AiSourceVideoMode.Edit ? null : durationSeconds,
                        characterImage: inputs.FirstOrDefault(input => input.Role == CharacterImageRole)?.Upload,
                        orientation: orientation, quality: quality, model: model, idempotencyKey: name.Key), token)
                    : await _videos.CreateAsync(
                    new AiVideoGenerationRequest(
                        prompt,
                        durationSeconds,
                        new AiVideoResolutionId(resolution),
                        new AiVideoAspectRatioId(aspectRatio),
                        generateAudio,
                        seed: seed,
                        firstFrame: firstFrame,
                        lastFrame: lastFrame,
                        model: model,
                        idempotencyKey: name.Key,
                        inputReferences: inputs.Select(input => input.Upload).ToArray()),
                    token),
                WithdrawRequestName,
                operation.CancellationToken);

            // Past here the clip has been reserved and paid for. Whatever goes
            // wrong while it is waited on, the name stays: it is the way back.
            persistedServerJob = true;

            double resultDurationSeconds = ResultDurationSeconds(sourceSeconds, durationSeconds);
            if (!operation.TryPublish(() =>
                {
                    if (IsGeneration) PromptLibrary.Record(prompt);
                }))
            {
                return;
            }

            // Retired only once the server has settled the job. A clip whose
            // result never reached the client is still recoverable under the key
            // that created it, and asking again under a new one would pay twice.
            if (await PollJobAsync(response.JobId, operation, resultDurationSeconds))
            {
                RetireRequestName(name);
            }
        }
        // Every refusal that reserves nothing withdrew its name where it was
        // raised, next to the request that never left.
        catch (Exception ex) when (AiRequestFailure.Classify(ex) is { } failure)
        {
            if (failure.RetiresName)
                RetireRequestName(issued);
            if (failure.IsResultDownloadFailure)
                _logger.LogError(ex, "Failed to download the AI result.");
            operation.TryPublish(() => Error.Value = failure.Message);
        }
        catch (VideoInputException ex)
        {
            operation.TryPublish(() => Error.Value = ex.Message);
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
            // A dispatched request's fence remains durable after disposal, and
            // AiRequestKey can reacquire that exact owner on an immediate
            // same-key refresh. Pre-dispatch claims are released normally.
            claim?.Dispose();
            if (ReferenceEquals(_runningRequest, operation))
                _runningRequest = null;
            operation.TryPublish(() => IsGenerating.Value = false);
        }
    }

    private string ComposePrompt() => IsSourceVideo ? Prompt.Value.Trim() : AiPromptComposer.Compose(new AiPromptParts(
        Prompt.Value,
        Style.Value,
        Composition.Value,
        Motion.Value,
        Exclusions.Value));

    // How long the clip that comes back runs: an edit keeps the source's length, and an
    // extension adds to it.
    private double ResultDurationSeconds(double? sourceSeconds, int durationSeconds)
        => SourceMode switch
        {
            AiSourceVideoMode.Edit when sourceSeconds is { } exactSeconds => exactSeconds,
            AiSourceVideoMode.Extend when sourceSeconds is { } originalSeconds => originalSeconds + durationSeconds,
            _ => durationSeconds,
        };

}

internal sealed record AiVideoDurationOption(int Seconds)
{
    public override string ToString() => $"{Seconds} {Strings.AiVideoSeconds}";
}

internal sealed record AiVideoAspectRatioOption(string Value)
{
    public override string ToString() => Value;
}

internal sealed record AiVideoResolutionOption(string Value)
{
    public override string ToString() => Value;
}
