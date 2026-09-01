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

internal sealed class AiVideoGenerationDialogViewModel : IDisposable, IAsyncDisposable, IAiModelListConsumer
{
    private readonly CompositeDisposable _disposables = [];
    private readonly AsyncOperationLifetime _operations = new();
    private readonly IdentityOperationLifetime _identityOperations = new();
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
    private readonly AiRequestKey _requestKey;
    private readonly AiRequestRecoveryContext? _requestRecoveryContext;
    // The model the outstanding name was built from. A refresh that withdraws
    // that model would otherwise rebuild the name around whatever the picker
    // fell back to, and the job the first attempt paid for would be left behind.
    private readonly CancellationTokenSource _availabilityLifetimeCts = new();
    private readonly EditViewModel? _editViewModel;
    private readonly HashSet<string> _temporaryFiles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _temporaryFileLeases = new(StringComparer.Ordinal);
    // 決着していない名前ごとに、その依頼が名乗ったフレームの一時ファイルを
    // 抱えておくもの。
    private readonly Dictionary<string, IDisposable> _framesHeldByName =
        new(StringComparer.Ordinal);
    // 利用者が選んだもの。画面に出ているものとは別に持つ——モデルを選び直すと
    // 画面のほうは、そのモデルが取れる範囲へ寄せられてしまう。元のモデルに
    // 戻したときにここから戻せないと、同じつもりの依頼が別の依頼になり、
    // 出してある名前が指すものへ届かなくなる。
    private AiVideoDurationOption? _chosenDuration;
    private AiVideoResolutionOption? _chosenResolution;
    private AiVideoAspectRatioOption? _chosenAspectRatio;
    private bool _chosenAudio = true;
    private int? _chosenSeed;
    private (string? Path, string? ElementId) _chosenFirstFrame;
    private (string? Path, string? ElementId) _chosenLastFrame;
    // モデルの都合で画面を書き換えている最中。そのあいだの変化は利用者の選択では
    // ないので、覚えない。
    private bool _applyingCapabilities;
    private readonly HashSet<string> _temporaryFilesPendingDeletion = new(StringComparer.Ordinal);
    private readonly object _lifetimeGate = new();
    private CancellationTokenSource? _pollingCts;
    private IdentityOperationLifetime.Operation? _runningRequest;
    private string? _firstFrameElementId;
    private string? _lastFrameElementId;
    private AiVideoResultSnapshot? _resultSnapshot;
    private AiPendingAttempt? _selectedRecovery;
    private readonly ReactivePropertySlim<int> _recoveryRevision = new();
    private Task? _disposeTask;

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
        AiRequestRecoveryContext requestRecoveryContext)
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
        _requestRecoveryContext = requestRecoveryContext;
        _requestKey = new(
            recoveryContext: requestRecoveryContext,
            operation: "video.generate");
        Usage = new AiUsageViewModel(_entitlements.Entitlements).DisposeWith(_disposables);
        ModelPicker = new AiModelPickerViewModel(_modelCatalog, _entitlements)
            .DisposeWith(_disposables);
        PromptLibrary = new AiPromptLibraryViewModel(
                PromptTaskKind.Video,
                ComposePrompt,
                prompt => Prompt.Value = prompt,
                recoveryContext: requestRecoveryContext)
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
        SelectedDuration.Subscribe(option =>
            {
                if (!_applyingCapabilities)
                    _chosenDuration = option;
            })
            .DisposeWith(_disposables);
        SelectedResolution.Subscribe(option =>
            {
                if (!_applyingCapabilities)
                    _chosenResolution = option;
            })
            .DisposeWith(_disposables);
        SelectedAspectRatio.Subscribe(option =>
            {
                if (!_applyingCapabilities)
                    _chosenAspectRatio = option;
            })
            .DisposeWith(_disposables);
        GenerateAudio.Subscribe(value =>
            {
                if (!_applyingCapabilities)
                    _chosenAudio = value;
            })
            .DisposeWith(_disposables);
        Seed.Subscribe(seed =>
            {
                if (!_applyingCapabilities)
                    _chosenSeed = seed;
            })
            .DisposeWith(_disposables);
        ModelPicker.Selected.Subscribe(option => ApplyModelCapabilities(option?.Model))
            .DisposeWith(_disposables);
        // The shape, and whether frames may guide the clip at all, follow the
        // chosen model; replacing the list under an outstanding name would
        // rewrite the request waiting to be collected.
        ModelPicker.KeepOffered = operation => _requestKey.PersistedModels(operation);
        ModelPicker.CanReload = _ => !_requestKey.HasOutstandingName.Value;

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
            .CombineLatest(
                EstimatedUsage.CanAfford,
                _requestKey.HasOutstandingName,
                // Or a name already handed out: the server answers a repeat with the
                // job that name made before it looks at the balance, so the request
                // that spent the last of it is exactly the one that must stay
                // collectable.
                (canGenerate, canAfford, outstanding) =>
                    canGenerate && (canAfford || outstanding))
            .CombineLatest(
                ModelPicker.OffersNothingUsable,
                _requestKey.HasOutstandingName,
                // Every model the operation registered was ruled out, so a new
                // request would be refused however it is shaped — but a name
                // already handed out is answered from the job it made, whatever
                // the catalog says now.
                (can, nothingUsable, outstanding) =>
                    can && (!nothingUsable || outstanding))
            // Until the list has been asked for, a request would name no model
            // and run on the server's default, which may cost more than what
            // this screen was about to offer.
            .CombineLatest(ModelPicker.IsLoaded, (can, loaded) => can && loaded)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        Generate = new AsyncReactiveCommand(CanGenerate)
            .WithSubscribe(GenerateCore);
        StopGenerating = new ReactiveCommand(IsGenerating);
        StopGenerating.Subscribe(StopGeneratingCore).DisposeWith(_disposables);

        RecoverSelectedAttempt = new ReactiveCommand();
        RecoverSelectedAttempt.Subscribe(() =>
        {
            if (SelectedRecoveryAttempt!.Value is { } attempt)
                TryRecoverPendingAttempt(attempt);
        }).DisposeWith(_disposables);
        AbandonSelectedAttempt = new ReactiveCommand();
        AbandonSelectedAttempt.Subscribe(() =>
        {
            if (SelectedRecoveryAttempt!.Value is { } attempt)
                AbandonPendingAttempt(attempt);
        }).DisposeWith(_disposables);

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

        SelectedRecoveryAttempt = new ReactivePropertySlim<AiPendingAttempt?>()
            .DisposeWith(_disposables);
        RecoveryAttempts = _recoveryRevision
            .Select(_ => (IReadOnlyList<AiPendingAttempt>)GetPendingRecoveryAttempts())
            .ToReadOnlyReactivePropertySlim(Array.Empty<AiPendingAttempt>())
            .DisposeWith(_disposables);
        RecoveryAvailable = RecoveryAttempts
            .Select(attempts => attempts.Count != 0)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
        if (_requestRecoveryContext is not null)
            _requestRecoveryContext.IdentityChanged += OnIdentityChanged;

        CoreObject? selectedObject = editViewModel?.GetService<IEditorSelection>()?.SelectedObject.Value;
        SetFrame(
            isFirstFrame: true,
            AiImageEditDialogViewModel.GetSelectedImageSourcePath(selectedObject),
            selectedObject is Element selectedElement ? selectedElement.Id.ToString("N") : null);

        _ = LoadEntitlementsAsync();
        TryAutoRecoverSingleAttempt();
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

    // Where the model sits in the request's parts. It is filled in last: which
    // model a request carries depends on whether a name is already outstanding
    // for the rest of it.
    private const int ModelPartIndex = 6;

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

        // What the user asked for is remembered apart from what the model on
        // screen will take. Reading the choice back off the screen loses it the
        // moment a model that takes something narrower is picked, and going
        // back to the first model then rebuilds a different request — one the
        // name already handed out does not belong to.
        _applyingCapabilities = true;
        try
        {
            ApplyModelCapabilitiesCore(video);
        }
        finally
        {
            _applyingCapabilities = false;
        }
    }

    private void ApplyModelCapabilitiesCore(AiVideoModelCapabilities video)
    {
        // The model's own lists, already narrowed to what the server accepts.
        // The client's own are a fallback for a server that publishes none.
        IEnumerable<int> durations = video.DurationsSeconds.IsSpecified
            ? video.DurationsSeconds.Values
            : DefaultDurations;
        var availableDurations = durations.ToList();
        if (_selectedRecovery?.Form?.DurationSeconds is { } recoveredDuration
            && !availableDurations.Contains(recoveredDuration))
            availableDurations.Add(recoveredDuration);
        Replace(DurationOptions, availableDurations.Select(seconds => new AiVideoDurationOption(seconds)));
        SelectedDuration.Value = NearestDuration(
            _chosenDuration ?? SelectedDuration.Value,
            DurationOptions);
        MaxDurationIndex.Value = DurationOptions.Count - 1;
        DurationIndex.Value = IndexOfDuration(SelectedDuration.Value);

        IEnumerable<string> resolutions = video.Resolutions.IsSpecified
            ? video.Resolutions.Values
            : DefaultResolutions;
        var availableResolutions = resolutions.ToList();
        if (_selectedRecovery?.Form?.Resolution is { } recoveredResolution
            && !availableResolutions.Contains(recoveredResolution, StringComparer.Ordinal))
            availableResolutions.Add(recoveredResolution);
        Replace(ResolutionOptions, availableResolutions.Select(value => new AiVideoResolutionOption(value)));
        SelectedResolution.Value =
            ResolutionOptions.FirstOrDefault(option => option.Value == _chosenResolution?.Value)
            ?? ResolutionOptions[0];

        IEnumerable<string> aspectRatios = video.AspectRatios.IsSpecified
            ? video.AspectRatios.Values
            : DefaultAspectRatios;
        var availableAspectRatios = aspectRatios.ToList();
        if (_selectedRecovery?.Form?.AspectRatio is { } recoveredAspect
            && !availableAspectRatios.Contains(recoveredAspect, StringComparer.Ordinal))
            availableAspectRatios.Add(recoveredAspect);
        Replace(
            AspectRatioOptions,
            availableAspectRatios.Select(value => new AiVideoAspectRatioOption(value)));
        SelectedAspectRatio.Value =
            AspectRatioOptions.FirstOrDefault(option => option.Value == _chosenAspectRatio?.Value)
            // The shape it would have started on, which is the one nearest the
            // scene rather than whichever the model happens to list first.
            ?? GetSuggestedAspectRatio(AspectRatioOptions, _editViewModel?.Scene.FrameSize);

        SupportsAudio.Value = _selectedRecovery?.Form?.SupportsAudio
            ?? video.SupportsAudio;
        GenerateAudio.Value = SupportsAudio.Value && _chosenAudio;
        SupportsSeed.Value = _selectedRecovery?.Form?.SupportsSeed
            ?? video.SupportsSeed;
        Seed.Value = SupportsSeed.Value ? _chosenSeed : null;
        // A model conditions on the frames it publishes, and one of the two is
        // not the other. A picker left up for a frame the model does not take
        // only produces a request refused after the shape has been checked.
        //
        // A last frame is only ever sent alongside a first one — the endpoint
        // takes no request without one — so a model that publishes a last frame
        // and no first frame can be given neither.
        SupportsFirstFrame.Value = _selectedRecovery?.Form?.SupportsFirstFrame
            ?? video.SupportsFirstFrame;
        SupportsLastFrame.Value = SupportsFirstFrame.Value
            && (_selectedRecovery?.Form?.SupportsLastFrame ?? video.SupportsLastFrame);
        SupportsFrameGuidance.Value = SupportsFirstFrame.Value;
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

    /// <summary>Pending video attempts that can be explicitly recovered or abandoned.</summary>
    internal IReadOnlyList<AiPendingAttempt> PendingRecoveryAttempts
        => GetPendingRecoveryAttempts();

    internal ReactivePropertySlim<AiPendingAttempt?> SelectedRecoveryAttempt { get; }

    internal ReadOnlyReactivePropertySlim<IReadOnlyList<AiPendingAttempt>> RecoveryAttempts { get; }

    internal ReadOnlyReactivePropertySlim<bool> RecoveryAvailable { get; }

    internal ReactiveCommand RecoverSelectedAttempt { get; }

    internal ReactiveCommand AbandonSelectedAttempt { get; }

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

    // Null in production. Tests can suspend the picker/importer at a precise
    // point while changing the authenticated identity.
    internal Func<CancellationToken, Task<string?>>? FramePicker { get; set; }

    internal Func<CancellationToken, Task<AiSaveFileDestination?>>? SaveFilePicker { get; set; }

    internal Func<string, AiResultImportOptions, CancellationToken, Task<ElementAddResult>>?
        ResultImporter
    { get; set; }

    internal Func<TimeSpan, CancellationToken, Task> PollDelayAsync { get; set; } = Task.Delay;

    internal TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    internal TimeSpan MaximumTransientPollDelay { get; set; } = TimeSpan.FromSeconds(30);

    public ReadOnlyReactivePropertySlim<bool> ShowJoinPro { get; }

    public ReactivePropertySlim<string?> Error { get; } = new();

    public void Dispose() => _ = BeginDisposeAsync();

    public ValueTask DisposeAsync() => new(BeginDisposeAsync());

    private IdentityOperationLifetime.Operation? TryEnterIdentityOperation()
        => _identityOperations.TryEnter(_operations);

    private Task BeginDisposeAsync()
    {
        lock (_disposeGate)
        {
            if (_disposeTask is not null)
                return _disposeTask;

            var completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _disposeTask = completion.Task;
            _ = CompleteDisposeAsync(completion);
            return completion.Task;
        }
    }

    private async Task CompleteDisposeAsync(TaskCompletionSource completion)
    {
        try
        {
            await DisposeCoreAsync();
            completion.TrySetResult();
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
    }

    private async Task DisposeCoreAsync()
    {
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
            SupportsFrameGuidance.Dispose();
            FirstFramePath.Dispose();
            LastFramePath.Dispose();
            Error.Dispose();
            if (_requestRecoveryContext is not null)
                _requestRecoveryContext.IdentityChanged -= OnIdentityChanged;
            _requestKey.Dispose();
            _recoveryRevision.Dispose();
            _disposables.Dispose();
            _availabilityTracker.Dispose();
            _availabilityLifetimeCts.Dispose();
            foreach (string path in temporaryFiles)
            {
                DeleteTemporaryFile(path);
            }
        });
        _identityOperations.Dispose();
    }

    /// <summary>
    /// Re-reads the model list. The catalog is cached with a freshness window,
    /// so this costs nothing while it is fresh and picks up a model an operator
    /// added, removed or reordered once it is not — which a workspace tab left
    /// open would otherwise never see.
    /// </summary>
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
            await ModelPicker.LoadAsync(
                AiOperations.VideoGeneration,
                operation.CancellationToken);
            SelectRecoveredModel();
        }
        catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to reload the AI models for video generation.");
        }
    }

    private IReadOnlyList<AiPendingAttempt> GetPendingRecoveryAttempts()
    {
        try
        {
            return _requestKey.PendingAttempts(AiOperations.VideoGeneration);
        }
        catch (InvalidDataException ex)
        {
            _logger.LogError(ex, "Failed to read video-generation recovery attempts.");
            return Array.Empty<AiPendingAttempt>();
        }
    }

    private void TryAutoRecoverSingleAttempt()
    {
        IReadOnlyList<AiPendingAttempt> attempts = GetPendingRecoveryAttempts();
        if (attempts.Count == 1 && attempts[0].HasCanonicalForm)
        {
            if (!TryRecoverPendingAttempt(attempts[0]))
                ClearActiveRecovery();
        }

        _recoveryRevision.Value++;
    }

    private void OnIdentityChanged()
    {
        string? account = _requestKey.CurrentAccountId;
        _identityOperations.Switch(() =>
        {
            _runningRequest = null;
            IsGenerating.Value = false;
            IsWaitingForJob.Value = false;
            ClearActiveRecovery();
            _chosenDuration = null;
            _chosenResolution = null;
            _chosenAspectRatio = null;
            _chosenAudio = true;
            _chosenSeed = null;
            SetFrameCore(isFirstFrame: true, null, null);
            SetFrameCore(isFirstFrame: false, null, null);
            Prompt.Value = string.Empty;
            Style.Value = string.Empty;
            Composition.Value = string.Empty;
            Motion.Value = string.Empty;
            Exclusions.Value = string.Empty;
            if (ResultVideoPath.Value is { } resultPath)
                RequestTemporaryFileDeletion(resultPath);
            ResultVideoPath.Value = null;
            _resultSnapshot = null;
            StatusText.Value = Strings.AiVideoIdle;
            Error.Value = null;
            ModelPicker.ReconcileRecoveryModels();
            ApplyModelCapabilities(ModelPicker.Selected.Value?.Model);
            _recoveryRevision.Value++;
        });
        if (account is not null)
            TryAutoRecoverSingleAttempt();
    }

    internal bool TryRecoverPendingAttempt(AiPendingAttempt attempt)
    {
        if (_requestKey.CurrentAccountId is not { } account
            || !StringComparer.Ordinal.Equals(account, attempt.AccountId))
        {
            Error.Value = Strings.AiAuthenticationRequired;
            return false;
        }
        if (!attempt.HasCanonicalForm
            || !string.Equals(attempt.Operation, AiOperations.VideoGeneration.Value, StringComparison.Ordinal))
        {
            Error.Value = Strings.AiResultUnavailable;
            return false;
        }

        IReadOnlyList<string> paths;
        try
        {
            paths = _requestKey.ResolveSources(attempt);
            foreach (AiRequestRecoverySource source in attempt.EffectiveSources)
                _ = _requestKey.ReadSourceBytes(source);
        }
        catch (InvalidDataException ex)
        {
            // Keep the row and key until the user explicitly abandons it.
            _logger.LogWarning(ex, "Video-generation recovery source is unavailable.");
            Error.Value = Strings.AiResultUnavailable;
            return false;
        }

        AiRequestFormSnapshot form = attempt.Form!;
        if (paths.Count != attempt.EffectiveSources.Count)
        {
            Error.Value = Strings.AiResultUnavailable;
            return false;
        }

        _applyingCapabilities = true;
        try
        {
            Prompt.Value = form.Prompt ?? string.Empty;
            Style.Value = form.Style ?? string.Empty;
            Composition.Value = form.Composition ?? string.Empty;
            Motion.Value = form.Motion ?? string.Empty;
            Exclusions.Value = form.Exclusions ?? string.Empty;
            _chosenDuration = form.DurationSeconds is { } seconds
                ? new AiVideoDurationOption(seconds)
                : SelectedDuration.Value;
            _chosenResolution = form.Resolution is { } resolution
                ? new AiVideoResolutionOption(resolution)
                : SelectedResolution.Value;
            _chosenAspectRatio = form.AspectRatio is { } aspect
                ? new AiVideoAspectRatioOption(aspect)
                : SelectedAspectRatio.Value;
            _chosenAudio = form.GenerateAudio ?? true;
            _chosenSeed = form.Seed;
            ApplyRecoveredScalarSelections(form);
        }
        finally
        {
            _applyingCapabilities = false;
        }

        string? firstPath = null;
        string? lastPath = null;
        string? firstElement = form.FirstFrameElementId;
        string? lastElement = form.LastFrameElementId;
        for (int index = 0; index < attempt.EffectiveSources.Count; index++)
        {
            AiRequestRecoverySource source = attempt.EffectiveSources[index];
            string path = paths[index];
            if (source.Role == "first-frame")
            {
                firstPath = path;
                firstElement ??= source.ElementId;
            }
            else if (source.Role == "last-frame")
            {
                lastPath = path;
                lastElement ??= source.ElementId;
            }
        }

        SetFrameCore(isFirstFrame: true, firstPath, firstElement);
        SetFrameCore(isFirstFrame: false, lastPath, lastElement);
        ActivateRecovery(attempt);
        ApplyModelCapabilities(ModelPicker.Selected.Value?.Model);
        _recoveryRevision.Value++;
        SelectRecoveredModel();
        return true;
    }

    private void ApplyRecoveredScalarSelections(AiRequestFormSnapshot form)
    {
        if (form.DurationSeconds is { } duration
            && DurationOptions.FirstOrDefault(option => option.Seconds == duration) is { } durationOption)
            SelectedDuration.Value = durationOption;
        if (form.Resolution is { } resolution
            && ResolutionOptions.FirstOrDefault(option => option.Value == resolution) is { } resolutionOption)
            SelectedResolution.Value = resolutionOption;
        if (form.AspectRatio is { } aspect
            && AspectRatioOptions.FirstOrDefault(option => option.Value == aspect) is { } aspectOption)
            SelectedAspectRatio.Value = aspectOption;
        GenerateAudio.Value = form.GenerateAudio ?? true;
        Seed.Value = form.Seed;
    }

    internal void AbandonPendingAttempt(AiPendingAttempt attempt)
    {
        try
        {
            _requestKey.Abandon(attempt);
            if (_selectedRecovery is { } selected
                && selected.AccountId == attempt.AccountId
                && selected.Operation == attempt.Operation
                && selected.Fingerprint == attempt.Fingerprint)
            {
                ClearActiveRecovery();
                ModelPicker.ReconcileRecoveryModels();
                ApplyModelCapabilities(ModelPicker.Selected.Value?.Model);
            }

            _recoveryRevision.Value++;
        }
        catch (Exception ex) when (ex is InvalidDataException or AuthenticationRequiredException)
        {
            _logger.LogWarning(ex, "Failed to abandon video-generation recovery attempt.");
            Error.Value = Strings.AiResultUnavailable;
        }
    }

    private AiModelId? ModelForRequest(AiModelId? selected)
        => _selectedRecovery is { } attempt
            ? attempt.Model is { } model ? new AiModelId(model) : null
            : selected;

    private void ActivateRecovery(AiPendingAttempt attempt)
    {
        _selectedRecovery = attempt;
        SelectedRecoveryAttempt.Value = attempt;
        ModelPicker.IsSelectionEnabled.Value = false;
    }

    private void ClearActiveRecovery()
    {
        _selectedRecovery = null;
        SelectedRecoveryAttempt.Value = null;
        ModelPicker.IsSelectionEnabled.Value = true;
    }

    private void SelectRecoveredModel()
    {
        if (_selectedRecovery is not { } recovery)
            return;
        if (recovery.Model is not { } model)
        {
            ModelPicker.Selected.Value = null;
            return;
        }
        AiModelId id = new(model);
        ModelPicker.Selected.Value = ModelPicker.Options.FirstOrDefault(option => option.Id == id);
    }

    // The model a request should carry: the one the outstanding name was built
    // from while there is one, and the picker's otherwise.
    // Only this one request is settled. Retiring the whole run instead would
    // throw away the name of anything else still waiting to be collected.
    private void RetireRequestName(AiRequestName name)
    {
        if (!_requestKey.Retire(name))
            return;
        ReleaseFramesOf(name);
        if (_selectedRecovery is { } selected
            && (string.Equals(selected.Key, name.Key, StringComparison.Ordinal)
                || !_requestKey.IsCurrentPending(selected)))
        {
            ReleaseFramesOf(new AiRequestName(selected.Key, IsRepeat: true));
            ClearActiveRecovery();
            ModelPicker.ReconcileRecoveryModels();
            ApplyModelCapabilities(ModelPicker.Selected.Value?.Model);
            _recoveryRevision.Value++;
        }
        // Reloads were held back while that name was outstanding, so this is
        // where an operator's change to the model list finally lands.
        _ = RefreshModelsAsync();
    }

    // A name the server never made a job under. Withdrawing it lets the picker
    // move again and puts the balance check back in front of the next attempt.
    private void WithdrawRequestName(AiRequestName name)
    {
        if (!_requestKey.WithdrawAfterNoReservation(name))
            return;
        ReleaseFramesOf(name);
        if (_selectedRecovery is { } selected
            && (string.Equals(selected.Key, name.Key, StringComparison.Ordinal)
                || !_requestKey.IsCurrentPending(selected)))
        {
            ReleaseFramesOf(new AiRequestName(selected.Key, IsRepeat: true));
            ClearActiveRecovery();
            ModelPicker.ReconcileRecoveryModels();
            ApplyModelCapabilities(ModelPicker.Selected.Value?.Model);
            _recoveryRevision.Value++;
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
                    AiOperations.VideoGeneration,
                    _requestKey.PreferredPersistedModel(AiOperations.VideoGeneration),
                    _requestKey.HasExplicitNullPersistedModel(AiOperations.VideoGeneration),
                    operation.CancellationToken);
                SelectRecoveredModel();
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

    private async Task SelectFrameAsync(bool isFirstFrame)
    {
        using IdentityOperationLifetime.Operation? operation = TryEnterIdentityOperation();
        if (operation is null)
            return;
        string? path;
        if (FramePicker is { } picker)
        {
            path = await picker(operation.CancellationToken);
        }
        else
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime
                { MainWindow: { } window }
                || TopLevel.GetTopLevel(window)?.StorageProvider is not { } storage)
                return;
            IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(
                SharedFilePickerOptions.OpenAiVideoFrame());
            path = files.Count > 0 ? files[0].Path.LocalPath : null;
        }
        if (path is not null)
        {
            operation.TryPublish(() =>
                SetFrameCore(isFirstFrame, path, sourceElementId: null));
        }
    }

    private async Task CaptureCurrentFrameAsync()
    {
        using IdentityOperationLifetime.Operation? operation = TryEnterIdentityOperation();
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
        using IdentityOperationLifetime.Operation? operation = TryEnterIdentityOperation();
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

        // 利用者が選んだフレーム。モデルの都合で外しているだけのときは書き換え
        // ない——元のモデルへ戻したときに、同じフレームを持つ同じ依頼に戻れる
        // ようにしておく。
        if (!_applyingCapabilities)
        {
            if (isFirstFrame)
            {
                _chosenFirstFrame = (path, sourceElementId);
            }
            else
            {
                _chosenLastFrame = (path, sourceElementId);
            }
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

        // 脇に置いているだけなら、その一時ファイルは捨てない。捨ててしまうと、
        // 元のモデルへ戻しても同じ依頼を組み立て直せない。
        if (!_applyingCapabilities
            && !string.Equals(previousPath, path, StringComparison.Ordinal))
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
        using IdentityOperationLifetime.Operation? operation = TryEnterIdentityOperation();
        if (operation is null)
            return;
        if (ResultVideoPath.Value is not { } path || !File.Exists(path))
            return;

        try
        {
            operation.TryPublish(() =>
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "open" }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open the generated AI video preview.");
            operation.TryPublish(() => Error.Value = Strings.AiVideoPreviewOpenFailed);
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
            int durationSeconds = SelectedDuration.Value.Seconds;
            string resolution = SelectedResolution.Value.Value;
            string aspectRatio = SelectedAspectRatio.Value.Value;
            bool generateAudio = GenerateAudio.Value;
            string? firstFramePath = FirstFramePath.Value;
            string? lastFramePath = LastFramePath.Value;
            string? firstFrameElementId = _firstFrameElementId;
            string? lastFrameElementId = _lastFrameElementId;

            // The model's place is left empty until it is known, because which
            // model this request carries depends on whether a name is already
            // outstanding for the rest of it.
            // Read once, and named by that reading. Reading again to send would
            // name one set of bytes and upload another if a frame changed in
            // between, and the answer would be recorded under a name that
            // describes something else.
            AiRequestRecoverySource? existingFirstSource = _selectedRecovery?.EffectiveSources
                .FirstOrDefault(source => source.Role == "first-frame");
            AiRequestRecoverySource? existingLastSource = _selectedRecovery?.EffectiveSources
                .FirstOrDefault(source => source.Role == "last-frame");
            if (existingFirstSource is not null
                && !RecoverySourceMatchesPath(existingFirstSource, firstFramePath))
                existingFirstSource = null;
            if (existingLastSource is not null
                && !RecoverySourceMatchesPath(existingLastSource, lastFramePath))
                existingLastSource = null;
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
                Seed.Value?.ToString(CultureInfo.InvariantCulture),
                null,
                firstFrameStamp,
                lastFrameStamp,
            ];
            AiModelId? model = ModelForRequest(ModelPicker.SelectedModel);
            requestParts[ModelPartIndex] = model?.Value;
            AiRequestFormSnapshot form = new(
                Prompt: Prompt.Value,
                Style: Style.Value,
                Composition: Composition.Value,
                Motion: Motion.Value,
                Exclusions: Exclusions.Value,
                AspectRatio: aspectRatio,
                Resolution: resolution,
                DurationSeconds: durationSeconds,
                GenerateAudio: generateAudio,
                Seed: Seed.Value,
                SupportsAudio: SupportsAudio.Value,
                SupportsSeed: SupportsSeed.Value,
                SupportsFirstFrame: SupportsFirstFrame.Value,
                SupportsLastFrame: SupportsLastFrame.Value,
                FirstFrameElementId: firstFrameElementId,
                LastFrameElementId: lastFrameElementId);
            var recoverySources = new List<AiRequestRecoverySource>(2);
            try
            {
                if (firstFramePath is { } firstPath && firstFrame is not null && firstFrameBytes is not null)
                {
                    recoverySources.Add(
                        IsTemporaryFile(firstPath) && _requestKey.HasDurableRecovery
                            ? _requestKey.CreateDurableSource(
                                "first-frame",
                                firstFrameName ?? Path.GetFileName(firstPath),
                                firstFrameBytes,
                                firstFrameElementId)
                            : FileAiRequestRecoveryStore.CreateExternalSource(
                                "first-frame",
                                firstPath,
                                firstFrameName ?? Path.GetFileName(firstPath),
                                firstFrameBytes,
                                firstFrameElementId));
                }
                if (lastFramePath is { } lastPath && lastFrame is not null && lastFrameBytes is not null)
                {
                    recoverySources.Add(
                        IsTemporaryFile(lastPath) && _requestKey.HasDurableRecovery
                            ? _requestKey.CreateDurableSource(
                                "last-frame",
                                lastFrameName ?? Path.GetFileName(lastPath),
                                lastFrameBytes,
                                lastFrameElementId)
                            : FileAiRequestRecoveryStore.CreateExternalSource(
                                "last-frame",
                                lastPath,
                                lastFrameName ?? Path.GetFileName(lastPath),
                                lastFrameBytes,
                                lastFrameElementId));
                }
            }
            catch
            {
                _requestKey.CleanupUncommittedSources(recoverySources);
                throw;
            }
            if (_selectedRecovery is { } selected
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
            if (_requestKey.HasDurableRecovery && claim is null)
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

            // Before it goes out. A name that ends here reached nothing.
            try
            {
                // Not for a repeat: the server looks up the job this name
                // already made before it looks at the balance, so refusing here
                // would refuse to collect something already paid for.
                if (!name.IsRepeat
                    && !await _availabilityTracker.CheckNowAsync(
                        new AiOperationAvailabilityRequest.Video(durationSeconds, model),
                        operation.CancellationToken))
                {
                    throw new AiUsageLimitExceededException();
                }
            }
            catch (Exception ex) when (AiRequestOutcome.ReservedNothing(ex))
            {
                WithdrawRequestName(name);
                throw;
            }

            AiVideoGenerationResult response;
            try
            {
                _requestKey.MarkClaimDispatched(claim);
                response = await _videos.CreateAsync(
                    new AiVideoGenerationRequest(
                        prompt,
                        durationSeconds,
                        new AiVideoResolutionId(resolution),
                        new AiVideoAspectRatioId(aspectRatio),
                        generateAudio,
                        seed: Seed.Value,
                        firstFrame: firstFrame,
                        lastFrame: lastFrame,
                        model: model,
                        idempotencyKey: name.Key),
                    operation.CancellationToken);
            }
            catch (Exception ex) when (AiRequestOutcome.ReservedNothing(ex))
            {
                WithdrawRequestName(name);
                throw;
            }

            // Past here the clip has been reserved and paid for. Whatever goes
            // wrong while it is waited on, the name stays: it is the way back.
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
                RetireRequestName(name);
            }
        }
        // Every refusal that reserves nothing withdrew its name where it was
        // raised, next to the request that never left. These only say what
        // happened.
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
            operation.TryPublish(() => Error.Value = Strings.AiModelUnavailable);
        }
        // Refused before the operation was reserved, so nothing was charged;
        // what has to change is the model or the shape of the request.
        catch (AiModelDoesNotSupportRequestException)
        {
            operation.TryPublish(() => Error.Value = Strings.AiModelDoesNotSupportRequest);
        }
        // The server settled this job as failed and refunded it. Its name would
        // keep answering with that failure, so the next attempt takes a new one.
        catch (AiProviderErrorException)
        {
            RetireRequestName(issued);
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
        // 送った名前が、別の依頼のものだった。画面の中身を戻せばその依頼として
        // 送り直せるので、名前は残す——ここで捨てると、支払い済みかもしれない
        // job へ戻る道が閉じる。
        catch (AiRequestChangedException)
        {
            operation.TryPublish(() => Error.Value = Strings.AiRequestChanged);
        }
        catch (AiRequestWasDeletedException)
        {
            RetireRequestName(issued);
            operation.TryPublish(() => Error.Value = Strings.AiRequestWasDeleted);
        }
        catch (AiJobNotFoundException)
        {
            // The create key points at a job the server no longer retains. Keeping the
            // key would make every poll/retry return the same terminal absence.
            RetireRequestName(issued);
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
            // A dispatched request's fence remains durable after disposal, and
            // AiRequestKey can reacquire that exact owner on an immediate
            // same-key refresh. Pre-dispatch claims are released normally.
            claim?.Dispose();
            if (ReferenceEquals(_runningRequest, operation))
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
        IdentityOperationLifetime.Operation operation,
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
                if (status.IsTerminal)
                {
                    operation.TryPublish(() =>
                    {
                        StatusText.Value = Strings.AiVideoFailed;
                        Error.Value = AiErrorMessage.Localize(job.Error) ?? Strings.AiProviderError;
                    });
                    return true;
                }

                if (!status.ShouldPoll)
                {
                    // An unknown status is not a terminal outcome. The server may
                    // have introduced it during a rolling upgrade, so keep the
                    // request recoverable and require a later history refresh.
                    operation.TryPublish(() =>
                    {
                        StatusText.Value = Strings.AiResultUnavailable;
                        Error.Value = Strings.AiResultUnavailable;
                    });
                    return false;
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
        IdentityOperationLifetime.Operation operation)
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

    // 未回収の名前が指しているフレームの一時ファイルを、その名前が決着するまで
    // 抱えておく。決着していない依頼はそのファイルの姿ごと名前になっているので、
    // 消えると二度と同じ依頼を出せない——モデルを選び直しただけで消える。
    private void HoldFramesFor(AiRequestName name, string? firstFrame, string? lastFrame)
    {
        if (string.IsNullOrEmpty(name.Key))
            return;

        var held = new CompositeDisposable(
            AcquireTemporaryFileLease(firstFrame),
            AcquireTemporaryFileLease(lastFrame));
        lock (_lifetimeGate)
        {
            if (_framesHeldByName.Remove(name.Key, out IDisposable? previous))
                previous.Dispose();
            _framesHeldByName.Add(name.Key, held);
        }
    }

    private void ReleaseFramesOf(AiRequestName name)
    {
        if (string.IsNullOrEmpty(name.Key))
            return;

        IDisposable? held;
        lock (_lifetimeGate)
        {
            if (!_framesHeldByName.Remove(name.Key, out held))
                return;
        }

        held.Dispose();
    }

    // 送るものと、名前に使うものを、同じ一度の読み取りから作る。読み直すと、
    // 名前を付けた中身と実際に送る中身が食い違い、答えは別のものを指す名前で
    // 記録される。
    private async Task<(
        AiUploadSource? Frame,
        string Stamp,
        byte[]? Bytes,
        string? Name)> ReadFrameAsync(
        string? path,
        CancellationToken cancellationToken,
        AiRequestRecoverySource? recoveredSource = null)
    {
        if (string.IsNullOrEmpty(path))
            return (null, string.Empty, null, null);

        string fileName = recoveredSource?.Name ?? Path.GetFileName(path);
        byte[] bytes = recoveredSource is not null
            ? _requestKey.ReadSourceBytes(recoveredSource)
            : await AiUploadBytes.ReadWithinAsync(
                path,
                AiRequestLimits.MaxFrameUploadBytes,
                cancellationToken);
        // 名前は数えない。サーバーはフレームを中身と種類だけで見分ける——場面から
        // 切り出したフレームは、その都度ちがう名前のファイルに落ちるので、名前を
        // 数えると、同じ一枚で送り直すたびに別の依頼になって買い直しになる。
        return (
            AiUploadSource.FromBytes(fileName, bytes),
            AiRequestKey.ContentStamp(bytes),
            bytes,
            fileName);
    }

    private bool IsTemporaryFile(string path)
    {
        lock (_lifetimeGate)
            return _temporaryFiles.Contains(path);
    }

    private static bool RecoverySourceMatchesPath(
        AiRequestRecoverySource source,
        string? path)
    {
        if (path is null)
            return false;
        return source.DurableFile is { } durable
            ? string.Equals(Path.GetFileName(path), durable, StringComparison.Ordinal)
            : string.Equals(
                Path.GetFullPath(source.Path ?? string.Empty),
                Path.GetFullPath(path),
                StringComparison.Ordinal);
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
        using IdentityOperationLifetime.Operation? operation = TryEnterIdentityOperation();
        if (operation is null)
            return;
        if (_editViewModel == null || ResultVideoPath.Value is not { } filePath)
            return;
        if (!operation.IsCurrent)
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
            AiResultImportOptions options = new(
                start,
                TimeSpan.FromSeconds(durationSeconds),
                layer,
                Strings.AiVideoGeneration);
            ElementAddResult result;
            if (ResultImporter is { } importer)
            {
                result = await importer(filePath, options, operation.CancellationToken);
            }
            else
            {
                var defaultImporter = new AiResultImporter(
                    _editViewModel.Scene,
                    _editViewModel.GetRequiredService<IElementAdder>());
                result = await defaultImporter.ImportVideoAsync(
                    filePath,
                    options,
                    operation.CancellationToken);
            }

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
        using IdentityOperationLifetime.Operation? operation = TryEnterIdentityOperation();
        if (operation is null)
            return;
        if (ResultVideoPath.Value is not { } filePath)
            return;
        using IDisposable fileLease = AcquireTemporaryFileLease(filePath);

        AiSaveFileDestination? destination;
        if (SaveFilePicker is { } picker)
        {
            destination = await picker(operation.CancellationToken);
        }
        else
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime
                { MainWindow: { } window }
                || TopLevel.GetTopLevel(window)?.StorageProvider is not { } storage)
                return;
            FilePickerSaveOptions options = SharedFilePickerOptions.SaveVideo();
            options.SuggestedFileName = $"AI Video {DateTime.Now:yyyy-MM-dd HHmmss}";
            options.SuggestedStartLocation = await storage.TryGetWellKnownFolderAsync(WellKnownFolder.Videos);
            options.DefaultExtension = Path.GetExtension(filePath).TrimStart('.');
            IStorageFile? file = await storage.SaveFilePickerAsync(options);
            destination = file is null
                ? null
                : new AiSaveFileDestination(
                    file.Path.LocalPath,
                    _ => file.OpenWriteAsync());
        }

        if (destination is null || !operation.IsCurrent)
            return;

        try
        {
            byte[] bytes = await File.ReadAllBytesAsync(filePath, operation.CancellationToken);
            await using Stream destinationStream = await destination.OpenWriteAsync(operation.CancellationToken);
            if (!operation.TryPublish(() =>
                {
                    operation.CancellationToken.ThrowIfCancellationRequested();
                    destinationStream.SetLength(0);
                    destinationStream.Write(bytes);
                }))
                return;
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
