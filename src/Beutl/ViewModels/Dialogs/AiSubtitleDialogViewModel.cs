using Beutl.Animation;
using Beutl.Api;
using System.Text.Json.Nodes;
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
using Beutl.Services.PrimitiveImpls;
using Beutl.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.ViewModels.Dialogs;

public sealed partial class AiSubtitleDialogViewModel : IToolContext
{
    private readonly CompositeDisposable _disposables = [];
    private readonly LifetimeCancellationSource _lifetimeCts = new();
    private readonly ILogger _logger = Log.CreateLogger<AiSubtitleDialogViewModel>();
    private readonly SubtitleAiCapabilities _aiService;
    private readonly IAiEntitlementService _entitlements;
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
        IAiPlanCoordinator aiPlanCoordinator,
        IAiTranscriptionService transcription,
        IAiCaptionTranslationService translation,
        CaptionCatalog captionCatalog,
        ICaptionDraftStore captionDraftStore,
        IObservable<CaptionDraftScope?> captionDraftScopes,
        EditViewModel? editViewModel = null)
    {
        _entitlements = entitlements ?? throw new ArgumentNullException(nameof(entitlements));
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
                (canTranscribe, canAfford) => canTranscribe && canAfford)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        Transcribe = new AsyncReactiveCommand(CanTranscribe)
            .WithSubscribe(TranscribeCore);

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

    public ToolTabExtension Extension => AiSubtitleTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

    public IReadOnlyReactiveProperty<string> Header { get; } = new ReactivePropertySlim<string>(Strings.AiSubtitle);

    public ReactivePropertySlim<IReadOnlyList<AudioSourceItem>> AudioSources { get; }

    public ReactivePropertySlim<AudioSourceItem?> SelectedAudioSource { get; }

    public ICoreReadOnlyList<CaptionTemplateDescriptor> CaptionTemplates { get; }

    public ReactivePropertySlim<CaptionTemplateDescriptor> SelectedCaptionTemplate { get; }

    public ReactivePropertySlim<bool> IsTranscribing { get; }

    public ReadOnlyReactivePropertySlim<bool> CanTranscribe { get; }

    public AsyncReactiveCommand Transcribe { get; }

    public ReadOnlyReactivePropertySlim<bool> CanAddToScene { get; }

    public AsyncReactiveCommand AddToScene { get; }

    public ReactiveCommand OpenAiPlan { get; }

    internal IAiPlanCoordinator AiPlanCoordinator => _aiPlanCoordinator;

    public ReactivePropertySlim<AiTranscriptionSegment[]?> ResultSegments { get; } = new();

    internal AiUsageViewModel Usage { get; }

    public ReadOnlyReactivePropertySlim<bool> ShowJoinPro { get; }

    public ReactivePropertySlim<string?> Error { get; } = new();

    public object? GetService(Type serviceType) => _editViewModel?.GetService(serviceType);

    public void ReadFromJson(JsonObject json)
    {
        // Transcripts and caption drafts are intentionally kept out of the persisted dock layout.
    }

    public void WriteToJson(JsonObject json)
    {
        // Transcripts and caption drafts are intentionally kept out of the persisted dock layout.
    }

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
        IsSelected.Dispose();
        _disposables.Dispose();
        _lifetimeCts.Dispose();
    }

    private async Task LoadEntitlementsAsync()
    {
        try
        {
            await _entitlements.RefreshAsync(_lifetimeCts.Token);
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
                        sound.Speed.Animation as KeyFrameAnimation<float>));
                }
            }
        }

        AudioSources.Value = items;
        SelectedAudioSource.Value = items.FirstOrDefault();
        return Task.CompletedTask;
    }

    private async Task TranscribeCore()
    {
        if (SelectedAudioSource.Value is not { } source)
            return;

        long draftScopeRevision = Interlocked.Read(ref _captionDraftScopeRevision);
        Error.Value = null;
        IsTranscribing.Value = true;
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
        catch (AiProviderErrorException)
        {
            SetCaptionErrorIfCurrent(draftScopeRevision, Strings.AiProviderError);
        }
        catch (SubtitleInputException ex)
        {
            SetCaptionErrorIfCurrent(draftScopeRevision, ex.Message);
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to transcribe audio.");
            SetCaptionErrorIfCurrent(draftScopeRevision, Strings.AiUnexpectedError);
        }
        finally
        {
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
            CancellationToken cancellationToken)
            => translation.TranslateAsync(request, cancellationToken);
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

    public AudioSourceItem(
        string name,
        string filePath,
        TimeSpan duration,
        TimeSpan elementStart = default,
        TimeSpan? elementLength = null,
        TimeSpan sourceOffset = default,
        float speed = 100,
        KeyFrameAnimation<float>? speedAnimation = null)
    {
        Name = name;
        FilePath = filePath;
        Duration = duration;
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

    internal static AudioSourceItem CreateSceneMix(string name, TimeSpan sceneStart, TimeSpan duration)
        => new(name, sceneStart, duration);

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
