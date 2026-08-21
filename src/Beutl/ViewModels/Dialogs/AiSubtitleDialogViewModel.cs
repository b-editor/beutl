using Beutl.Animation;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Audio;
using Beutl.Collections;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Editor.Services.Captions;
using Beutl.Engine;
using Beutl.Graphics.Shapes;
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

public sealed partial class AiSubtitleDialogViewModel : IDisposable, IAiModelListConsumer
{
    private readonly CompositeDisposable _disposables = [];
    private readonly LifetimeCancellationSource _lifetimeCts = new();
    private CancellationTokenSource? _requestCts;
    private readonly ILogger _logger = Log.CreateLogger<AiSubtitleDialogViewModel>();
    private readonly SubtitleAiCapabilities _aiService;
    private readonly IAiEntitlementService _entitlements;
    private readonly IAiOperationAvailabilityService _availability;
    private readonly IAiModelCatalogService _modelCatalog;
    private readonly IAiPlanCoordinator _aiPlanCoordinator;
    private readonly EditViewModel? _editViewModel;
    private readonly CaptionCodecRegistry _captionCodecs;
    private readonly CaptionTemplateRegistry _captionTemplates;
    private readonly CaptionDocumentSerializer _captionSerializer;
    private readonly ICaptionDraftStore _captionDraftStore;
    private readonly IObservable<CaptionDraftScope?> _captionDraftScopes;
    private bool _disposed;

    internal AiSubtitleDialogViewModel(
        IAiEntitlementService entitlements,
        IAiOperationAvailabilityService availability,
        IAiModelCatalogService modelCatalog,
        IAiPlanCoordinator aiPlanCoordinator,
        IAiTranscriptionService transcription,
        IAiCaptionTranslationService translation,
        CaptionCatalog captionCatalog,
        ICaptionDraftStore captionDraftStore,
        IObservable<CaptionDraftScope?> captionDraftScopes,
        EditViewModel? editViewModel = null)
    {
        _entitlements = entitlements ?? throw new ArgumentNullException(nameof(entitlements));
        _availability = availability ?? throw new ArgumentNullException(nameof(availability));
        _modelCatalog = modelCatalog ?? throw new ArgumentNullException(nameof(modelCatalog));
        _aiPlanCoordinator = aiPlanCoordinator
            ?? throw new ArgumentNullException(nameof(aiPlanCoordinator));
        _aiService = new SubtitleAiCapabilities(
            transcription ?? throw new ArgumentNullException(nameof(transcription)),
            translation ?? throw new ArgumentNullException(nameof(translation)));
        ArgumentNullException.ThrowIfNull(captionCatalog);
        _editViewModel = editViewModel;
        _captionDraftStore = captionDraftStore
            ?? throw new ArgumentNullException(nameof(captionDraftStore));
        _captionDraftScopes = captionDraftScopes
            ?? throw new ArgumentNullException(nameof(captionDraftScopes));
        _captionCodecs = captionCatalog.Codecs;
        _captionTemplates = captionCatalog.Templates;
        _captionSerializer = captionCatalog.Serializer;
        Usage = new AiUsageViewModel(_entitlements.Entitlements).DisposeWith(_disposables);
        // Two operations on one screen, so two pickers: transcription and
        // translation have their own models and their own prices.
        TranscriptionModelPicker = new AiModelPickerViewModel(_modelCatalog, _entitlements)
            .DisposeWith(_disposables);
        TranslationModelPicker = new AiModelPickerViewModel(_modelCatalog, _entitlements)
            .DisposeWith(_disposables);

        AudioSources = new ReactivePropertySlim<IReadOnlyList<AudioSourceItem>>([])
            .DisposeWith(_disposables);
        SelectedAudioSource = new ReactivePropertySlim<AudioSourceItem?>()
            .DisposeWith(_disposables);
        SelectedAudioSource.Subscribe(_ =>
        {
            ResultSegments.Value = null;
            Error.Value = null;
            InvalidatePartialResultResume();
        }).DisposeWith(_disposables);

        CaptionTemplates = captionCatalog.Templates.Templates;
        SelectedCaptionTemplate = new ReactivePropertySlim<CaptionTemplateDescriptor>(CaptionTemplates[0])
            .DisposeWith(_disposables);
        CaptionTemplates.CollectionChanged += OnCaptionTemplatesChanged;

        IsTranscribing = new ReactivePropertySlim<bool>(false)
            .DisposeWith(_disposables);

        InitializeCaptionEditing();

        CanTranscribe = CanTranscribeInput
            .CombineLatest(
                IsTranscribing,
                IsTranslating,
                (hasInput, transcribing, translating) => hasInput && !transcribing && !translating)
            .CombineLatest(
                TranscriptionEstimate.CanAfford,
                HasOutstandingTranscriptionRequest,
                // Or a run that has already named pieces: the server answers a
                // repeat with the job that name made before it looks at the
                // balance, so a run whose last piece spent the balance has to
                // stay collectable.
                (canTranscribe, canAfford, outstanding) =>
                    canTranscribe && (canAfford || outstanding))
            .CombineLatest(
                TranscriptionModelPicker.OffersNothingUsable,
                HasOutstandingTranscriptionRequest,
                // Every model the operation registered was ruled out, so a new
                // request would be refused however it is shaped — but a run
                // already holding a name is answered from the job it made.
                (can, nothingUsable, outstanding) =>
                    can && (!nothingUsable || outstanding))
            // Until the list has been asked for, a request would name no model
            // and run on the server's default, which may cost more than what
            // this screen was about to offer.
            .CombineLatest(
                TranscriptionModelPicker.IsLoaded,
                (can, loaded) => can && loaded)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        Transcribe = new AsyncReactiveCommand(CanTranscribe)
            .WithSubscribe(TranscribeCore);

        StopRequest = new ReactiveCommand(
            IsTranscribing.CombineLatest(IsTranslating, (a, b) => a || b));
        StopRequest.Subscribe(() => _requestCts?.Cancel()).DisposeWith(_disposables);

        CanAddToScene = HasValidCues
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        AddToScene = new AsyncReactiveCommand(CanAddToScene)
            .WithSubscribe(AddToSceneCore);

        OpenAiPlan = new ReactiveCommand();
        OpenAiPlan.Subscribe(aiPlanCoordinator.OpenAiPlan).DisposeWith(_disposables);

        ShowJoinPro = Usage.HasSnapshot
            .CombineLatest(Usage.CanUseAi, (hasSnapshot, canUseAi) => hasSnapshot && !canUseAi)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        _ = LoadEntitlementsAsync();
        _ = LoadAudioSourcesAsync();
    }

    public ReactivePropertySlim<IReadOnlyList<AudioSourceItem>> AudioSources { get; }

    public ReactivePropertySlim<AudioSourceItem?> SelectedAudioSource { get; }

    public ICoreReadOnlyList<CaptionTemplateDescriptor> CaptionTemplates { get; }

    public ReactivePropertySlim<CaptionTemplateDescriptor> SelectedCaptionTemplate { get; }

    public ReactivePropertySlim<bool> IsTranscribing { get; }

    public ReadOnlyReactivePropertySlim<bool> CanTranscribe { get; }

    public AsyncReactiveCommand Transcribe { get; }

    /// <summary>
    /// Abandons the transcription or translation in flight. A long scene takes
    /// minutes, so a wrong audio source must not mean closing the tab.
    /// </summary>
    public ReactiveCommand StopRequest { get; }

    public ReadOnlyReactivePropertySlim<bool> CanAddToScene { get; }

    public AsyncReactiveCommand AddToScene { get; }

    public ReactiveCommand OpenAiPlan { get; }

    internal IAiPlanCoordinator AiPlanCoordinator => _aiPlanCoordinator;

    internal void RefreshAvailability()
    {
        _transcriptionEstimateRevision.Value++;
        RefreshTranslationEstimate();
    }

    public ReactivePropertySlim<AiTranscriptionSegment[]?> ResultSegments { get; } = new();

    internal AiUsageViewModel Usage { get; }

    internal AiModelPickerViewModel TranscriptionModelPicker { get; }

    internal AiModelPickerViewModel TranslationModelPicker { get; }

    public ReadOnlyReactivePropertySlim<bool> ShowJoinPro { get; }

    public ReactivePropertySlim<string?> Error { get; } = new();

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _lifetimeCts.Cancel();
        CaptionTemplates.CollectionChanged -= OnCaptionTemplatesChanged;
        DisposeCaptionEditing();
        AudioSources.Dispose();
        SelectedAudioSource.Dispose();
        SelectedCaptionTemplate.Dispose();
        IsTranscribing.Dispose();
        ResultSegments.Dispose();
        PartialResultMessage.Dispose();
        HasPartialResult.Dispose();
        HasPendingHistoryResult.Dispose();
        HistoryOverwriteMessage.Dispose();
        Error.Dispose();
        _disposables.Dispose();
        _lifetimeCts.Dispose();
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
        // A run waiting to be collected names its unfinished pieces partly by
        // the model it started on. The run itself holds that model, so a reload
        // could not rename them — but moving the picker under a run that is
        // still going says the wrong thing about what the next piece will cost.
        if (HasOutstandingTranscriptionRequest.Value
            || HasOutstandingTranslationRequest.Value)
        {
            return;
        }

        try
        {
            await TranscriptionModelPicker.LoadAsync(
                AiOperations.Transcription,
                _restoredTranscriptionModel,
                _lifetimeCts.Token);
            await TranslationModelPicker.LoadAsync(
                AiOperations.CaptionTranslation,
                _restoredTranslationModel,
                _lifetimeCts.Token);
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to reload the AI models for subtitles.");
        }
    }

    private async Task LoadEntitlementsAsync()
    {
        try
        {
            await _entitlements.RefreshAsync(_lifetimeCts.Token);
            // A restored run is put back on the model it was named for; without
            // that, its unfinished pieces would be named differently and bought
            // a second time.
            await TranscriptionModelPicker.LoadAsync(
                AiOperations.Transcription,
                _restoredTranscriptionModel,
                _lifetimeCts.Token);
            await TranslationModelPicker.LoadAsync(
                AiOperations.CaptionTranslation,
                _restoredTranslationModel,
                _lifetimeCts.Token);
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load AI entitlements.");
        }
    }

    private Task LoadAudioSourcesAsync()
    {
        if (_editViewModel == null)
        {
            AudioSources.Value = [];
            return Task.CompletedTask;
        }

        var items = new List<AudioSourceItem>();
        Scene scene = _editViewModel.Scene;
        items.Add(AudioSourceItem.CreateSceneMix(
            Strings.AiSubtitle_SceneMix,
            scene.Start,
            scene.Duration));
        foreach (Element element in scene.Children)
        {
            foreach (EngineObject obj in element.Objects)
            {
                if (obj is SourceSound sound
                    && sound.Source.CurrentValue is { } source
                    && source.HasUri
                    && source.Uri.IsFile)
                {
                    // Use the original audio duration. Element length is user-editable and cannot
                    // be trusted for usage calculation.
                    TimeSpan duration = sound.TryGetOriginalDuration(out TimeSpan original)
                        ? original
                        : element.Length;
                    items.Add(new AudioSourceItem(
                        element.Name,
                        source.Uri.LocalPath,
                        duration,
                        element.Start,
                        element.Length,
                        sound.OffsetPosition.CurrentValue,
                        sound.Speed.CurrentValue,
                        sound.Speed.Animation as KeyFrameAnimation<float>,
                        elementId: element.Id));
                }
            }
        }

        AudioSources.Value = items;
        SelectedAudioSource.Value = items.FirstOrDefault();
        return Task.CompletedTask;
    }

    /// <summary>
    /// The token the request in flight runs under: the tab's lifetime, plus the
    /// person's own request to stop.
    /// </summary>
    private CancellationToken RequestToken => _requestCts?.Token ?? _lifetimeCts.Token;

    private bool IsRequestCanceled
        => _lifetimeCts.IsCancellationRequested || _requestCts?.IsCancellationRequested == true;

    private async Task TranscribeCore()
    {
        if (SelectedAudioSource.Value is not { } source)
            return;

        long draftScopeRevision = Interlocked.Read(ref _captionDraftScopeRevision);
        Error.Value = null;
        IsTranscribing.Value = true;
        using CancellationTokenSource requestCts =
            CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        _requestCts = requestCts;
        try
        {
            await TranscribeSelectedSourceAsync(source);
        }
        catch (AuthenticationRequiredException)
        {
            SetCaptionErrorIfCurrent(draftScopeRevision, Strings.AiAuthenticationRequired);
        }
        catch (AiPlanRequiredException)
        {
            SetCaptionErrorIfCurrent(draftScopeRevision, Strings.AiProRequired);
        }
        catch (AiUsageLimitExceededException)
        {
            SetCaptionErrorIfCurrent(draftScopeRevision, Strings.AiUsageLimitExceeded);
        }
        catch (AiFileTooLargeException)
        {
            SetCaptionErrorIfCurrent(draftScopeRevision, Strings.AiFileTooLarge);
        }
        catch (AiResultUnavailableException)
        {
            SetCaptionErrorIfCurrent(draftScopeRevision, Strings.AiResultUnavailable);
        }
        catch (AiModelUnavailableException)
        {
            SetCaptionErrorIfCurrent(draftScopeRevision, Strings.AiModelUnavailable);
        }
        catch (AiModelDoesNotSupportRequestException)
        {
            SetCaptionErrorIfCurrent(draftScopeRevision, Strings.AiModelDoesNotSupportRequest);
        }
        catch (AiProviderErrorException)
        {
            SetCaptionErrorIfCurrent(draftScopeRevision, Strings.AiProviderError);
        }
        // Reachable because a chunk keeps its name across attempts: asking again
        // for one the server is still working on is how its result is recovered
        // rather than bought twice, and until it finishes the answer is this.
        catch (AiRequestInProgressException)
        {
            SetCaptionErrorIfCurrent(draftScopeRevision, Strings.AiRequestInProgress);
        }
        catch (AiRequestWasDeletedException)
        {
            // The job those names made is gone, so the rest of the run needs new
            // ones — written to the draft as well, or a run resumed after a
            // restart would ask under the same deleted name and stop there for
            // good. The pieces already paid for stay paid.
            RetireDeletedTranscriptionNames();
            SetCaptionErrorIfCurrent(draftScopeRevision, Strings.AiRequestWasDeleted);
        }
        catch (SubtitleInputException ex)
        {
            SetCaptionErrorIfCurrent(draftScopeRevision, ex.Message);
        }
        catch (OperationCanceledException) when (IsRequestCanceled)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to transcribe audio.");
            SetCaptionErrorIfCurrent(draftScopeRevision, Strings.AiUnexpectedError);
        }
        finally
        {
            _requestCts = null;
            if (!_disposed && IsCurrentCaptionDraftScope(draftScopeRevision))
            {
                IsTranscribing.Value = false;
            }
        }
    }

    private async Task AddToSceneCore()
    {
        if (_editViewModel == null
            || !TryBuildCaptionDocument(out CaptionDocument? document, out _)
            || document is null)
            return;

        try
        {
            CaptionSceneImportResult result = await AiCaptionSceneImporter.AddAsync(
                _editViewModel.Scene,
                _editViewModel.GetRequiredService<IElementAdder>(),
                document,
                _captionTemplates,
                SelectedCaptionTemplate.Value.Id,
                _lifetimeCts.Token);

            if (result.IsSuccess)
            {
                NotificationService.ShowSuccess(Strings.AiSubtitle, Strings.AiSubtitleAddedToScene);
            }
            else if (result.FailureId == ElementAddFailureIds.LockedLayer)
            {
                NotificationService.ShowWarning(Strings.Lock, Strings.LayerIsLocked);
            }
            else
            {
                throw new InvalidOperationException(
                    $"Failed to add caption elements: {result.FailureId}.");
            }
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add subtitles to the scene.");
            Error.Value = Strings.AiUnexpectedError;
        }
    }

    private void OnCaptionTemplatesChanged(
        object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (_disposed)
            return;

        CaptionTemplateDescriptor? selected = CaptionTemplates
            .FirstOrDefault(template => template.Id == SelectedCaptionTemplate.Value.Id);
        SelectedCaptionTemplate.Value = selected
            ?? CaptionTemplates.FirstOrDefault(template => template.Id == CaptionTemplateIds.DefaultText)
            ?? CaptionTemplates[0];
    }

    // This private adapter keeps the partial implementation cohesive while the public API exposes
    // transcription and translation as independent capabilities.
    private sealed class SubtitleAiCapabilities(
        IAiTranscriptionService transcription,
        IAiCaptionTranslationService translation)
    {
        public Task<AiTranscriptionResponse> TranscribeAsync(
            AiTranscriptionRequest request,
            CancellationToken cancellationToken)
            => transcription.TranscribeAsync(request, cancellationToken);

        public Task<AiCaptionTranslationResponse> TranslateAsync(
            AiCaptionTranslationRequest request,
            IProgress<AiCaptionTranslationSegment>? progress,
            CancellationToken cancellationToken)
            => translation.TranslateAsync(request, progress, cancellationToken);
    }

}

public sealed class AudioSourceItem
{
    private const int AnimationSampleRate = 1000;
    private readonly TimeSpan _elementStart;
    private readonly TimeSpan _elementLength;
    private readonly TimeSpan _sourceOffset;
    private readonly float _speed;
    private readonly KeyFrameAnimation<float>? _speedAnimation;
    private readonly Guid _elementId;

    public AudioSourceItem(
        string name,
        string filePath,
        TimeSpan duration,
        TimeSpan elementStart = default,
        TimeSpan? elementLength = null,
        TimeSpan sourceOffset = default,
        float speed = 100,
        KeyFrameAnimation<float>? speedAnimation = null,
        Guid elementId = default)
    {
        Name = name;
        FilePath = filePath;
        Duration = duration;
        _elementId = elementId;
        _elementStart = elementStart;
        _elementLength = elementLength ?? duration;
        _sourceOffset = sourceOffset;
        _speed = Math.Max(speed, 0);
        _speedAnimation = speedAnimation;
    }

    private AudioSourceItem(string name, TimeSpan sceneStart, TimeSpan duration)
    {
        Name = name;
        FilePath = null;
        Duration = duration;
        IsSceneMix = true;
        SceneStart = sceneStart;
        _elementStart = sceneStart;
        _elementLength = duration;
        _speed = 100;
    }

    public string Name { get; }

    public string? FilePath { get; }

    public TimeSpan Duration { get; }

    public bool IsSceneMix { get; }

    public TimeSpan SceneStart { get; }

    internal Guid ElementId => _elementId;

    internal static AudioSourceItem CreateSceneMix(string name, TimeSpan sceneStart, TimeSpan duration)
        => new(name, sceneStart, duration);

    internal static bool CanResume(AudioSourceItem? candidate, AudioSourceItem? previous)
    {
        if (candidate is null || previous is null)
            return false;
        if (ReferenceEquals(candidate, previous))
            return true;
        if (candidate.IsSceneMix || previous.IsSceneMix)
            return false;

        return FilePathsEqual(candidate.FilePath!, previous.FilePath!)
            && candidate.Duration == previous.Duration
            && candidate._elementId == previous._elementId
            && candidate._elementStart == previous._elementStart
            && candidate._elementLength == previous._elementLength
            && candidate._sourceOffset == previous._sourceOffset
            && candidate._speed == previous._speed
            && ReferenceEquals(candidate._speedAnimation, previous._speedAnimation);
    }

    internal static bool FilePathsEqual(string first, string second)
        => GetFilePathComparer().Equals(Path.GetFullPath(first), Path.GetFullPath(second));

    private static StringComparer GetFilePathComparer()
        => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    internal AiTranscriptionSegment[] MapSegmentsToScene(IEnumerable<AiTranscriptionSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        double elementLength = Math.Max(_elementLength.TotalSeconds, 0);
        if (elementLength <= 0)
            return [];

        using var integrator = new SpeedIntegrator(AnimationSampleRate);
        if (_speedAnimation != null)
        {
            integrator.EnsureCache(_speedAnimation);
        }

        double SourceElapsedAt(double localSeconds)
        {
            TimeSpan localTime = TimeSpan.FromSeconds(Math.Clamp(localSeconds, 0, elementLength));
            if (_speedAnimation == null)
                return localTime.TotalSeconds * (_speed / 100d);

            TimeSpan elapsed = _speedAnimation.UseGlobalClock
                ? integrator.Integrate(_elementStart + localTime, _speedAnimation)
                  - integrator.Integrate(_elementStart, _speedAnimation)
                : integrator.Integrate(localTime, _speedAnimation);
            return Math.Max(elapsed.TotalSeconds, 0);
        }

        double totalSourceElapsed = SourceElapsedAt(elementLength);
        if (totalSourceElapsed <= 0)
            return [];

        double sourceWindowStart = _sourceOffset.TotalSeconds;
        double sourceWindowEnd = sourceWindowStart + totalSourceElapsed;

        double ToLocalTime(double sourceSeconds)
        {
            double target = Math.Clamp(sourceSeconds - sourceWindowStart, 0, totalSourceElapsed);
            double low = 0;
            double high = elementLength;
            for (int iteration = 0; iteration < 50; iteration++)
            {
                double middle = (low + high) / 2;
                if (SourceElapsedAt(middle) < target)
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }
            return (low + high) / 2;
        }

        var result = new List<AiTranscriptionSegment>();
        foreach (AiTranscriptionSegment segment in segments)
        {
            if (!double.IsFinite(segment.Start)
                || !double.IsFinite(segment.End)
                || segment.End <= segment.Start)
            {
                continue;
            }

            double clippedStart = Math.Max(segment.Start, sourceWindowStart);
            double clippedEnd = Math.Min(segment.End, sourceWindowEnd);
            if (clippedEnd <= clippedStart)
                continue;

            double sceneStart = _elementStart.TotalSeconds + ToLocalTime(clippedStart);
            double sceneEnd = _elementStart.TotalSeconds + ToLocalTime(clippedEnd);
            if (sceneEnd <= sceneStart)
                continue;

            result.Add(new AiTranscriptionSegment
            {
                Start = sceneStart,
                End = sceneEnd,
                Text = segment.Text,
            });
        }

        return result.ToArray();
    }

    public override string ToString() => $"{Name} ({Duration.TotalSeconds:F1}s)";
}
