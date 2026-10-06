using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Reactive.Disposables;
using Beutl.Api.Services;
using Beutl.Editor.Services;
using Beutl.Editor.Services.Captions;
using Beutl.Graphics.Shapes;
using Beutl.Language;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Services.AI;
using Reactive.Bindings;

namespace Beutl.ViewModels.Dialogs;

public sealed partial class AiSubtitleDialogViewModel
{
    private const int TranscribePageIndex = 0;
    private const int EditPageIndex = 1;
    private const int TranslatePageIndex = 2;
    private readonly CompositeDisposable _captionDisposables = [];
    private readonly ObservableCollection<EditableCaptionCueViewModel> _editableCues = [];
    private readonly ReactivePropertySlim<long> _transcriptionEstimateRevision = new();
    private readonly ReactivePropertySlim<long> _translationEstimateRevision = new();
    private readonly ReactivePropertySlim<bool> _canDeleteCue = new();
    private readonly ReactivePropertySlim<bool> _canSplitCue = new();
    private readonly ReactivePropertySlim<bool> _canMergeCue = new();
    private string? _lastCaptionLanguage;
    private long _captionDocumentRevision;
    private long _sceneAudioRevision;
    private TranslationOperation? _pendingTranslation;
    private SceneTranscriptionOperation? _pendingSceneTranscription;
    private SourceTranscriptionOperation? _pendingSourceTranscription;
    private readonly List<CaptionDraftEntry> _retainedCaptionRecoveries = [];
    // Which model a restored run was named for, until the pickers have loaded
    // and can be put back on it.
    private AiModelId? _restoredTranscriptionModel;
    private AiModelId? _restoredTranslationModel;
    private ICaptionDraftSession? _captionDraftSession;
    private string? _captionDraftJobId;
    private long _captionDraftScopeRevision;
    private AiOperationAvailabilityTracker _transcriptionAvailability = null!;
    private AiOperationAvailabilityTracker _translationAvailability = null!;

    internal long SceneAudioRevision => Interlocked.Read(ref _sceneAudioRevision);

    public ReadOnlyObservableCollection<EditableCaptionCueViewModel> Cues { get; private set; } = null!;

    public ReactivePropertySlim<EditableCaptionCueViewModel?> SelectedCue { get; private set; } = null!;

    /// <summary>
    /// Whether there is anything to show in place of the cue list's empty state.
    /// </summary>
    public ReactivePropertySlim<bool> HasCues { get; private set; } = null!;

    internal ReadOnlyReactivePropertySlim<bool> CanTranscribeInput { get; private set; } = null!;

    internal ReactivePropertySlim<bool> HasValidCues { get; private set; } = null!;

    private ReactivePropertySlim<bool> HasTimingValidCues { get; set; } = null!;

    public ReactivePropertySlim<int> MaximumLineLength { get; private set; } = null!;

    public ReactivePropertySlim<int> MaximumLineCount { get; private set; } = null!;

    public ReactivePropertySlim<string?> CaptionValidationMessage { get; private set; } = null!;

    public ReactivePropertySlim<string> TemplatePreviewText { get; private set; } = null!;

    public ReactivePropertySlim<double> TemplatePreviewFontSize { get; private set; } = null!;

    internal ReactivePropertySlim<int> SelectedSubtitlePageIndex { get; private set; } = null!;
    internal ReadOnlyReactivePropertySlim<bool> IsTranscribePage { get; private set; } = null!;
    internal ReadOnlyReactivePropertySlim<bool> IsEditPage { get; private set; } = null!;
    internal ReadOnlyReactivePropertySlim<bool> IsTranslatePage { get; private set; } = null!;
    internal ReadOnlyReactivePropertySlim<bool> IsSubtitleOperationActive { get; private set; } = null!;

    /// <summary>
    /// The rendered output of the selected caption template. Keeping this as a bitmap makes the
    /// preview use the same Beutl renderer as the element that will be added to the scene instead
    /// of approximating a template with an Avalonia text block.
    /// </summary>
    internal ReactivePropertySlim<Ref<Beutl.Media.Bitmap>?> TemplatePreviewImage { get; private set; } = null!;

    public IReadOnlyList<CaptionLanguageOption> SourceLanguages { get; private set; } = null!;

    public IReadOnlyList<CaptionLanguageOption> TargetLanguages { get; private set; } = null!;

    public ReactivePropertySlim<CaptionLanguageOption> SelectedSourceLanguage { get; private set; } = null!;

    public ReactivePropertySlim<CaptionLanguageOption> SelectedTargetLanguage { get; private set; } = null!;

    public ReactivePropertySlim<string?> DetectedLanguageText { get; private set; } = null!;

    public ReactivePropertySlim<bool> IsTranslating { get; private set; } = null!;

    /// <summary>The line most recently translated, while a run is going on.</summary>
    public ReactivePropertySlim<string?> TranslationPreview { get; private set; } = null!;

    /// <summary>How many lines have come back so far in the run going on.</summary>
    public ReactivePropertySlim<int> TranslatedLineCount { get; private set; } = null!;

    public ReactivePropertySlim<string?> PartialResultMessage { get; } = new();

    public ReactivePropertySlim<bool> HasPartialResult { get; } = new();

    /// <summary>
    /// Whether a transcription run holds a name the server may answer from a
    /// job it has already been paid for.
    /// </summary>
    /// <remarks>
    /// Transcribing and translating are separate operations, charged
    /// separately. A name held by one says nothing about the other, so they are
    /// reported apart: shared, a transcription waiting to be collected would
    /// open the translation button past a balance that cannot pay for it.
    /// </remarks>
    public ReactivePropertySlim<bool> HasOutstandingTranscriptionRequest { get; } = new();

    /// <summary>
    /// Whether a translation run holds a name the server may answer from a job
    /// it has already been paid for.
    /// </summary>
    public ReactivePropertySlim<bool> HasOutstandingTranslationRequest { get; } = new();

    public ReactivePropertySlim<bool> HasPendingHistoryResult { get; } = new();

    public ReactivePropertySlim<string?> HistoryOverwriteMessage { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> CanTranslate { get; private set; } = null!;

    public AsyncReactiveCommand Translate { get; private set; } = null!;

    public ReactiveCommand ApplyPartialResult { get; private set; } = null!;

    public ReactiveCommand DiscardPartialResult { get; private set; } = null!;

    public ReactivePropertySlim<bool> HasRejectedTranscriptionResult { get; } = new();

    public ReactiveCommand DiscardRejectedTranscriptionResult { get; private set; } = null!;

    public AsyncReactiveCommand ImportCaptions { get; private set; } = null!;

    public AsyncReactiveCommand ExportCaptions { get; private set; } = null!;

    public ReactiveCommand AddCue { get; private set; } = null!;

    public ReactiveCommand DeleteCue { get; private set; } = null!;

    public ReactiveCommand SplitCue { get; private set; } = null!;

    public ReactiveCommand MergeCue { get; private set; } = null!;

    public ReactiveCommand WrapCues { get; private set; } = null!;

    internal AiUsageEstimateViewModel TranscriptionEstimate { get; private set; } = null!;

    internal AiUsageEstimateViewModel TranslationEstimate { get; private set; } = null!;

    private void InitializeCaptionEditing()
    {
        Cues = new ReadOnlyObservableCollection<EditableCaptionCueViewModel>(_editableCues);
        SelectedCue = new ReactivePropertySlim<EditableCaptionCueViewModel?>()
            .DisposeWith(_captionDisposables);
        MaximumLineLength = new ReactivePropertySlim<int>(42).DisposeWith(_captionDisposables);
        MaximumLineCount = new ReactivePropertySlim<int>(2).DisposeWith(_captionDisposables);
        CaptionValidationMessage = new ReactivePropertySlim<string?>().DisposeWith(_captionDisposables);
        TemplatePreviewText = new ReactivePropertySlim<string>(Strings.AiSubtitle_PreviewSample)
            .DisposeWith(_captionDisposables);
        TemplatePreviewFontSize = new ReactivePropertySlim<double>(24).DisposeWith(_captionDisposables);
        SelectedSubtitlePageIndex = new ReactivePropertySlim<int>(TranscribePageIndex)
            .DisposeWith(_captionDisposables);
        IsTranscribePage = SelectedSubtitlePageIndex.Select(value => value == TranscribePageIndex)
            .ToReadOnlyReactivePropertySlim().DisposeWith(_captionDisposables);
        IsEditPage = SelectedSubtitlePageIndex.Select(value => value == EditPageIndex)
            .ToReadOnlyReactivePropertySlim().DisposeWith(_captionDisposables);
        IsTranslatePage = SelectedSubtitlePageIndex.Select(value => value == TranslatePageIndex)
            .ToReadOnlyReactivePropertySlim().DisposeWith(_captionDisposables);
        TemplatePreviewImage = new ReactivePropertySlim<Ref<Beutl.Media.Bitmap>?>()
            .DisposeWith(_captionDisposables);
        DetectedLanguageText = new ReactivePropertySlim<string?>().DisposeWith(_captionDisposables);
        HasCues = new ReactivePropertySlim<bool>(false).DisposeWith(_captionDisposables);
        _editableCues.CollectionChanged += OnEditableCuesChanged;
        HasValidCues = new ReactivePropertySlim<bool>(false).DisposeWith(_captionDisposables);
        HasTimingValidCues = new ReactivePropertySlim<bool>(false).DisposeWith(_captionDisposables);
        IsTranslating = new ReactivePropertySlim<bool>(false).DisposeWith(_captionDisposables);
        IsSubtitleOperationActive = IsTranscribing.CombineLatest(
                IsTranslating,
                (transcribing, translating) => transcribing || translating)
            .ToReadOnlyReactivePropertySlim().DisposeWith(_captionDisposables);
        TranslationPreview = new ReactivePropertySlim<string?>().DisposeWith(_captionDisposables);
        TranslatedLineCount = new ReactivePropertySlim<int>().DisposeWith(_captionDisposables);

        SourceLanguages = CreateLanguageOptions(includeAuto: true);
        TargetLanguages = CreateLanguageOptions(includeAuto: false);
        SelectedSourceLanguage = new ReactivePropertySlim<CaptionLanguageOption>(SourceLanguages[0])
            .DisposeWith(_captionDisposables);
        SelectedTargetLanguage = new ReactivePropertySlim<CaptionLanguageOption>(
                GetDefaultTargetLanguage())
            .DisposeWith(_captionDisposables);

        // The scene mix is transcribed over the scene's own range, so retiming the
        // scene has to reach the estimate without the account re-picking anything.
        IObservable<TimeSpan> sceneRange = CreateSceneRangeObservable();
        CanTranscribeInput = SelectedAudioSource
            .CombineLatest(
                sceneRange,
                (source, duration) => source is not null
                    && (!source.IsSceneMix || duration > TimeSpan.Zero))
            .ToReadOnlyReactivePropertySlim(false)
            .DisposeWith(_captionDisposables);

        _transcriptionAvailability = new AiOperationAvailabilityTracker(
            _availability,
            _lifetimeCts.Token);
        _translationAvailability = new AiOperationAvailabilityTracker(
            _availability,
            _lifetimeCts.Token);
        IObservable<(AudioSourceItem? Source, TimeSpan Duration, long Revision)>
            transcriptionInputs = SelectedAudioSource.CombineLatest(
                sceneRange,
                _transcriptionEstimateRevision,
                (source, duration, revision) => (source, duration, revision));
        transcriptionInputs.Subscribe(input =>
                _transcriptionAvailability.Check(
                    CreateTranscriptionAvailabilityRequest(input.Source)))
            .DisposeWith(_captionDisposables);
        _translationEstimateRevision.Subscribe(_ => RefreshTranslationAvailability())
            .DisposeWith(_captionDisposables);
        TranscriptionEstimate = new AiUsageEstimateViewModel(
                Usage,
                _transcriptionAvailability.State)
            .DisposeWith(_captionDisposables);
        TranslationEstimate = new AiUsageEstimateViewModel(
                Usage,
                _translationAvailability.State)
            .DisposeWith(_captionDisposables);

        CanTranslate = CreateCanTranslate();
        InitializeCaptionCommands();

        ResultSegments.Subscribe(ApplyTranscriptionSegments).DisposeWith(_captionDisposables);
        MaximumLineLength.Subscribe(_ => RefreshCaptionState()).DisposeWith(_captionDisposables);
        MaximumLineCount.Subscribe(_ => RefreshCaptionState()).DisposeWith(_captionDisposables);
        SelectedCaptionTemplate.Subscribe(_ => RefreshTemplatePreview()).DisposeWith(_captionDisposables);
        SelectedSubtitlePageIndex.Subscribe(_ => RefreshTemplatePreview()).DisposeWith(_captionDisposables);
        SelectedCue.Subscribe(_ =>
        {
            RefreshCueCommandStates();
            RefreshTemplatePreview();
        }).DisposeWith(_captionDisposables);
        SelectedSourceLanguage.Subscribe(_ => InvalidatePartialResultResume()).DisposeWith(_captionDisposables);
        SelectedTargetLanguage.Subscribe(_ => RefreshTranslationEstimate()).DisposeWith(_captionDisposables);
        _captionDraftScopes
            .DistinctUntilChanged()
            .Subscribe(HandleCaptionDraftScopeChanged)
            .DisposeWith(_captionDisposables);
        if (_editViewModel is { } editViewModel)
        {
            editViewModel.Scene.GetObservable(Scene.FrameSizeProperty)
                .DistinctUntilChanged()
                .Skip(1)
                .Subscribe(_ => RefreshTemplatePreview())
                .DisposeWith(_captionDisposables);
            editViewModel.Scene.Edited += OnCaptionSceneEdited;
            Disposable.Create(() => editViewModel.Scene.Edited -= OnCaptionSceneEdited)
                .DisposeWith(_captionDisposables);
        }
    }

    private ReadOnlyReactivePropertySlim<bool> CreateCanTranslate()
        => HasTimingValidCues
            .CombineLatest(
                IsTranslating,
                IsTranscribing,
                TranslationEstimate.CanAfford,
                HasOutstandingTranslationRequest,
                // Or a run that has already named pieces: the server answers a
                // repeat with the job that name made before it looks at the
                // balance, so a run whose last piece spent the balance has to
                // stay collectable.
                (hasCues, translating, transcribing, canAfford, outstanding) =>
                    hasCues && !translating && !transcribing
                    && (canAfford || outstanding))
            .WhenSomeModelUsable(
                TranslationModelPicker.OffersNothingUsable,
                HasOutstandingTranslationRequest)
            .WhenModelsLoaded(TranslationModelPicker.IsLoaded)
            .ToReadOnlyReactivePropertySlim(false)
            .DisposeWith(_captionDisposables);

    private void InitializeCaptionCommands()
    {
        Translate = new AsyncReactiveCommand(CanTranslate)
            .WithSubscribe(TranslateCore)
            .DisposeWith(_captionDisposables);
        ApplyPartialResult = new ReactiveCommand(HasPartialResult)
            .DisposeWith(_captionDisposables);
        ApplyPartialResult.Subscribe(ApplyPartialResultCore).DisposeWith(_captionDisposables);
        DiscardPartialResult = new ReactiveCommand(HasPartialResult)
            .DisposeWith(_captionDisposables);
        DiscardPartialResult.Subscribe(ClearPartialResult).DisposeWith(_captionDisposables);
        DiscardRejectedTranscriptionResult = new ReactiveCommand(
                HasRejectedTranscriptionResult.CombineLatest(IsTranscribing,
                    (rejected, busy) => rejected && !busy))
            .DisposeWith(_captionDisposables);
        DiscardRejectedTranscriptionResult.Subscribe(() =>
        {
            // Only an explicit user action abandons the paid but unusable response.
            // Completed chunks are retained; the next request buys only the remainder.
            RetireTranscriptionRunNames();
            HasRejectedTranscriptionResult.Value = false;
            Error.Value = null;
            _transcriptionEstimateRevision.Value++;
        }).DisposeWith(_captionDisposables);
        ImportCaptions = new AsyncReactiveCommand()
            .WithSubscribe(ImportCaptionsCore)
            .DisposeWith(_captionDisposables);
        ExportCaptions = new AsyncReactiveCommand(HasValidCues)
            .WithSubscribe(ExportCaptionsCore)
            .DisposeWith(_captionDisposables);
        AddCue = new ReactiveCommand().DisposeWith(_captionDisposables);
        AddCue.Subscribe(AddCueCore).DisposeWith(_captionDisposables);
        DeleteCue = new ReactiveCommand(_canDeleteCue, initialValue: false)
            .DisposeWith(_captionDisposables);
        DeleteCue.Subscribe(DeleteCueCore).DisposeWith(_captionDisposables);
        SplitCue = new ReactiveCommand(_canSplitCue, initialValue: false)
            .DisposeWith(_captionDisposables);
        SplitCue.Subscribe(SplitCueCore).DisposeWith(_captionDisposables);
        MergeCue = new ReactiveCommand(_canMergeCue, initialValue: false)
            .DisposeWith(_captionDisposables);
        MergeCue.Subscribe(MergeCueCore).DisposeWith(_captionDisposables);
        WrapCues = new ReactiveCommand().DisposeWith(_captionDisposables);
        WrapCues.Subscribe(WrapCuesCore).DisposeWith(_captionDisposables);
    }

    private void OnEditableCuesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => HasCues.Value = _editableCues.Count > 0;

    private void DisposeCaptionEditing()
    {
        _editableCues.CollectionChanged -= OnEditableCuesChanged;
        foreach (EditableCaptionCueViewModel cue in _editableCues)
        {
            cue.PropertyChanged -= OnCuePropertyChanged;
        }
        _editableCues.Clear();
        _captionDraftSession?.Dispose();
        _captionDraftSession = null;
        StopTemplatePreviewAdmission();
        ReplaceTemplatePreviewImage(null);
        _captionDisposables.Dispose();
        _transcriptionEstimateRevision.Dispose();
        _translationEstimateRevision.Dispose();
        _transcriptionAvailability.Dispose();
        _translationAvailability.Dispose();
        _canDeleteCue.Dispose();
        _canSplitCue.Dispose();
        _canMergeCue.Dispose();
    }

    private void OnCaptionSceneEdited(object? sender, EventArgs e)
    {
        Interlocked.Increment(ref _sceneAudioRevision);
        LoadAudioSources();
    }

    private static CaptionDocument CreateCaptionDocument(
        IEnumerable<AiTranscriptionSegment> segments,
        string? language)
        => new(segments.Select(segment => new CaptionCue(
            TimeSpan.FromSeconds(segment.Start),
            TimeSpan.FromSeconds(segment.End),
            segment.Text,
            language: language)));

    private static AiTranscriptionSegment[] CloneSegments(
        IEnumerable<AiTranscriptionSegment> segments)
        => segments.Select(segment => new AiTranscriptionSegment
        {
            Start = segment.Start,
            End = segment.End,
            Text = segment.Text,
        }).ToArray();

    private CaptionLanguageOption GetDefaultTargetLanguage()
    {
        string targetCode = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja" ? "en" : "ja";
        return TargetLanguages.First(option => option.Code == targetCode);
    }

    private static IReadOnlyList<CaptionLanguageOption> CreateLanguageOptions(bool includeAuto)
    {
        var result = new List<CaptionLanguageOption>();
        if (includeAuto)
        {
            result.Add(new CaptionLanguageOption(null, Strings.AiLanguageAuto));
        }
        result.AddRange(
        [
            new CaptionLanguageOption("ja", "日本語 (ja)"),
            new CaptionLanguageOption("en", "English (en)"),
            new CaptionLanguageOption("zh", "中文 (zh)"),
            new CaptionLanguageOption("ko", "한국어 (ko)"),
            new CaptionLanguageOption("es", "Español (es)"),
            new CaptionLanguageOption("fr", "Français (fr)"),
            new CaptionLanguageOption("de", "Deutsch (de)"),
            new CaptionLanguageOption("pt", "Português (pt)"),
            new CaptionLanguageOption("it", "Italiano (it)"),
            new CaptionLanguageOption("ru", "Русский (ru)"),
            new CaptionLanguageOption("ar", "العربية (ar)"),
            new CaptionLanguageOption("hi", "हिन्दी (hi)"),
        ]);
        return result;
    }

    // The model a run is named for. The picker's until the run has named
    // anything, and from then on the run's own — including when the run named
    // its pieces with no model at all, which is not the same as having named
    // nothing yet.
    private static AiModelId? ModelOfRun(
        bool hasNamedAnything,
        string recorded,
        AiModelId? selected)
        => !hasNamedAnything
            ? selected
            : ModelIdOrNull(recorded);

    // A model or a key seed is written down as an empty string when there is none.
    private static AiModelId? ModelIdOrNull(string? model)
        => string.IsNullOrEmpty(model) ? null : new AiModelId(model);

    private static string? SeedOrNull(string? seed)
        => string.IsNullOrEmpty(seed) ? null : seed;

    private async Task EnsureAvailableAsync(AiOperationAvailabilityRequest request)
    {
        AiOperationAvailabilityTracker tracker = request is AiOperationAvailabilityRequest.Translation
            ? _translationAvailability
            : _transcriptionAvailability;
        if (!await tracker.CheckNowAsync(request, RequestToken))
            throw new AiUsageLimitExceededException();
    }

    // The refusals both runs report to the person as they are.
    private static string? KnownRunFailureMessage(Exception exception)
        => exception switch
        {
            AuthenticationRequiredException => Strings.AiAuthenticationRequired,
            AiPlanRequiredException => Strings.AiProRequired,
            AiUsageLimitExceededException => Strings.AiUsageLimitExceeded,
            AiProviderErrorException => Strings.AiProviderError,
            // Reachable because a piece keeps its name across attempts: asking again for one the
            // server is still working on is how its result is recovered rather than bought twice.
            // Neither of these is a settlement, so the run keeps its names and can be resumed;
            // saying "an unexpected error" instead sent the user back to a button that looked
            // like it would start over.
            AiResultUnavailableException => Strings.AiResultUnavailable,
            AiRequestInProgressException => Strings.AiRequestInProgress,
            AiModelUnavailableException => Strings.AiModelUnavailable,
            AiModelDoesNotSupportRequestException => Strings.AiModelDoesNotSupportRequest,
            _ => null,
        };

    private static string? CreateDetectedLanguageText(string? language)
        => string.IsNullOrWhiteSpace(language)
            ? null
            : string.Format(Strings.AiSubtitle_DetectedLanguage, language);
}
