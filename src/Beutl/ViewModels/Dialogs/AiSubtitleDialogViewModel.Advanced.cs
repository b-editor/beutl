using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Reactive.Disposables;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Beutl.Api.Services;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Editor.Services.Captions;
using Beutl.Graphics.Shapes;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Services.AI;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.ViewModels.Dialogs;

public sealed partial class AiSubtitleDialogViewModel
{
    private const int TranslationBatchSegmentLimit = 200;
    private const int TranslationBatchCharacterLimit = 20_000;
    private static readonly TimeSpan s_sceneMixChunkDuration = TimeSpan.FromMinutes(10);
    private readonly CompositeDisposable _captionDisposables = [];
    private readonly ObservableCollection<EditableCaptionCueViewModel> _editableCues = [];
    private readonly ReactivePropertySlim<long> _transcriptionEstimateRevision = new();
    private readonly ReactivePropertySlim<bool> _canStartTranslation = new();
    private string? _lastCaptionLanguage;
    private TimeSpan _sceneMixChunkDuration = s_sceneMixChunkDuration;
    private long _captionDocumentRevision;
    private RecoverableCaptionResult? _partialResult;
    private TranslationOperation? _pendingTranslation;
    private SceneTranscriptionOperation? _pendingSceneTranscription;
    private AiCaptionHistoryResult? _pendingHistoryResult;
    private ICaptionDraftSession? _captionDraftSession;
    private CaptionDraftScope? _captionDraftBaseScope;
    private string? _captionDraftJobId;
    private long _captionDraftScopeRevision;
    private bool _captionDraftScopeInitialized;

    internal void LoadHistoryResult(AiCaptionHistoryResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        // The tool tab is reusable, so a history import can arrive while the user still has
        // unsaved captions or a paid partial result in this tab. Ask before discarding them.
        if (HasUnsavedCaptionWork())
        {
            _pendingHistoryResult = result;
            HistoryOverwriteMessage.Value = Strings.AiSubtitle_HistoryOverwritePrompt;
            HasPendingHistoryResult.Value = true;
            return;
        }

        ApplyHistoryResult(result);
    }

    internal void ConfirmPendingHistoryResult()
    {
        if (_pendingHistoryResult is not { } result)
            return;

        DiscardPendingHistoryResult();
        ApplyHistoryResult(result);
    }

    internal void DiscardPendingHistoryResult()
    {
        _pendingHistoryResult = null;
        HasPendingHistoryResult.Value = false;
        HistoryOverwriteMessage.Value = null;
    }

    private bool HasUnsavedCaptionWork()
        => HasPartialResult.Value || _editableCues.Count > 0;

    private void ApplyHistoryResult(AiCaptionHistoryResult result)
    {
        ChangeCaptionDraftJob(result.JobId.Value, deleteCurrent: true);
        _pendingTranslation = null;
        _pendingSceneTranscription = null;
        _partialResult = null;
        HasPartialResult.Value = false;
        PartialResultMessage.Value = null;
        _lastCaptionLanguage = result.Language;
        DetectedLanguageText.Value = CreateDetectedLanguageText(result.Language);
        ResultSegments.Value = CloneSegments(result.Segments);
        Error.Value = null;
    }

    internal TimeSpan SceneMixChunkDuration
    {
        get => _sceneMixChunkDuration;
        set
        {
            if (value <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(value));

            _sceneMixChunkDuration = value;
        }
    }

    internal Func<TimeSpan, TimeSpan, CancellationToken, Task<AudioFrameSnapshot?>>?
        SceneMixAudioComposer
    { get; set; }

    public ReadOnlyObservableCollection<EditableCaptionCueViewModel> Cues { get; private set; } = null!;

    public ReactivePropertySlim<EditableCaptionCueViewModel?> SelectedCue { get; private set; } = null!;

    public ReactivePropertySlim<string> SceneRangeStartText { get; private set; } = null!;

    public ReactivePropertySlim<string> SceneRangeEndText { get; private set; } = null!;

    public ReadOnlyReactivePropertySlim<bool> UseSceneMix { get; private set; } = null!;

    internal ReadOnlyReactivePropertySlim<bool> CanTranscribeInput { get; private set; } = null!;

    internal ReactivePropertySlim<bool> HasValidCues { get; private set; } = null!;

    private ReactivePropertySlim<bool> HasTimingValidCues { get; set; } = null!;

    public ReactivePropertySlim<int> MaximumLineLength { get; private set; } = null!;

    public ReactivePropertySlim<int> MaximumLineCount { get; private set; } = null!;

    public ReactivePropertySlim<string?> CaptionValidationMessage { get; private set; } = null!;

    public ReactivePropertySlim<string> TemplatePreviewText { get; private set; } = null!;

    public ReactivePropertySlim<double> TemplatePreviewFontSize { get; private set; } = null!;

    public IReadOnlyList<CaptionLanguageOption> SourceLanguages { get; private set; } = null!;

    public IReadOnlyList<CaptionLanguageOption> TargetLanguages { get; private set; } = null!;

    public ReactivePropertySlim<CaptionLanguageOption> SelectedSourceLanguage { get; private set; } = null!;

    public ReactivePropertySlim<CaptionLanguageOption> SelectedTargetLanguage { get; private set; } = null!;

    public ReactivePropertySlim<string?> DetectedLanguageText { get; private set; } = null!;

    public ReactivePropertySlim<bool> IsTranslating { get; private set; } = null!;

    public ReactivePropertySlim<string?> PartialResultMessage { get; } = new();

    public ReactivePropertySlim<bool> HasPartialResult { get; } = new();

    public ReactivePropertySlim<bool> HasPendingHistoryResult { get; } = new();

    public ReactivePropertySlim<string?> HistoryOverwriteMessage { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> CanTranslate { get; private set; } = null!;

    public AsyncReactiveCommand Translate { get; private set; } = null!;

    public ReactiveCommand ApplyPartialResult { get; private set; } = null!;

    public ReactiveCommand DiscardPartialResult { get; private set; } = null!;

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
        TimeSpan rangeStart = _editViewModel?.Scene.Start ?? TimeSpan.Zero;
        TimeSpan rangeEnd = rangeStart + (_editViewModel?.Scene.Duration ?? TimeSpan.FromMinutes(5));
        SceneRangeStartText = new ReactivePropertySlim<string>(EditableCaptionCueViewModel.FormatTime(rangeStart))
            .DisposeWith(_captionDisposables);
        SceneRangeEndText = new ReactivePropertySlim<string>(EditableCaptionCueViewModel.FormatTime(rangeEnd))
            .DisposeWith(_captionDisposables);
        MaximumLineLength = new ReactivePropertySlim<int>(42).DisposeWith(_captionDisposables);
        MaximumLineCount = new ReactivePropertySlim<int>(2).DisposeWith(_captionDisposables);
        CaptionValidationMessage = new ReactivePropertySlim<string?>().DisposeWith(_captionDisposables);
        TemplatePreviewText = new ReactivePropertySlim<string>(Strings.AiSubtitle_PreviewSample)
            .DisposeWith(_captionDisposables);
        TemplatePreviewFontSize = new ReactivePropertySlim<double>(24).DisposeWith(_captionDisposables);
        DetectedLanguageText = new ReactivePropertySlim<string?>().DisposeWith(_captionDisposables);
        HasValidCues = new ReactivePropertySlim<bool>(false).DisposeWith(_captionDisposables);
        HasTimingValidCues = new ReactivePropertySlim<bool>(false).DisposeWith(_captionDisposables);
        IsTranslating = new ReactivePropertySlim<bool>(false).DisposeWith(_captionDisposables);

        SourceLanguages = CreateLanguageOptions(includeAuto: true);
        TargetLanguages = CreateLanguageOptions(includeAuto: false);
        SelectedSourceLanguage = new ReactivePropertySlim<CaptionLanguageOption>(SourceLanguages[0])
            .DisposeWith(_captionDisposables);
        SelectedTargetLanguage = new ReactivePropertySlim<CaptionLanguageOption>(
                GetDefaultTargetLanguage())
            .DisposeWith(_captionDisposables);

        UseSceneMix = SelectedAudioSource
            .Select(source => source?.IsSceneMix == true)
            .ToReadOnlyReactivePropertySlim(false)
            .DisposeWith(_captionDisposables);
        CanTranscribeInput = SelectedAudioSource
            .CombineLatest(
                SceneRangeStartText,
                SceneRangeEndText,
                (source, start, end) => source is not null
                    && (!source.IsSceneMix || TryGetSceneRange(start, end, out _, out _)))
            .ToReadOnlyReactivePropertySlim(false)
            .DisposeWith(_captionDisposables);

        IObservable<(AudioSourceItem? Source, string Start, string End, AiEntitlements? Entitlements)>
            transcriptionInputs = SelectedAudioSource
            .CombineLatest(
                SceneRangeStartText,
                SceneRangeEndText,
                _entitlements.Entitlements,
                (source, start, end, entitlements) => (source, start, end, entitlements));
        // The server decides whether a transcription can start; the client no longer
        // knows the per-minute price, so it only tracks that there is input to send.
        IObservable<bool> canStartTranscription = transcriptionInputs.CombineLatest(
            _transcriptionEstimateRevision,
            (input, _) =>
                input.Source is not null
                && (input.Entitlements?.Availability.CanStart(AiOperations.Transcription)
                    ?? false));
        TranscriptionEstimate = new AiUsageEstimateViewModel(Usage, canStartTranscription)
            .DisposeWith(_captionDisposables);
        TranslationEstimate = new AiUsageEstimateViewModel(Usage, _canStartTranslation)
            .DisposeWith(_captionDisposables);

        CanTranslate = HasTimingValidCues
            .CombineLatest(
                IsTranslating,
                IsTranscribing,
                TranslationEstimate.CanAfford,
                (hasCues, translating, transcribing, canAfford) =>
                    hasCues && !translating && !transcribing && canAfford)
            .ToReadOnlyReactivePropertySlim(false)
            .DisposeWith(_captionDisposables);
        Translate = new AsyncReactiveCommand(CanTranslate)
            .WithSubscribe(TranslateCore)
            .DisposeWith(_captionDisposables);
        ApplyPartialResult = new ReactiveCommand(HasPartialResult)
            .DisposeWith(_captionDisposables);
        ApplyPartialResult.Subscribe(ApplyPartialResultCore).DisposeWith(_captionDisposables);
        DiscardPartialResult = new ReactiveCommand(HasPartialResult)
            .DisposeWith(_captionDisposables);
        DiscardPartialResult.Subscribe(ClearPartialResult).DisposeWith(_captionDisposables);
        ImportCaptions = new AsyncReactiveCommand()
            .WithSubscribe(ImportCaptionsCore)
            .DisposeWith(_captionDisposables);
        ExportCaptions = new AsyncReactiveCommand(HasValidCues)
            .WithSubscribe(ExportCaptionsCore)
            .DisposeWith(_captionDisposables);
        AddCue = new ReactiveCommand().DisposeWith(_captionDisposables);
        AddCue.Subscribe(AddCueCore).DisposeWith(_captionDisposables);
        DeleteCue = new ReactiveCommand().DisposeWith(_captionDisposables);
        DeleteCue.Subscribe(DeleteCueCore).DisposeWith(_captionDisposables);
        SplitCue = new ReactiveCommand().DisposeWith(_captionDisposables);
        SplitCue.Subscribe(SplitCueCore).DisposeWith(_captionDisposables);
        MergeCue = new ReactiveCommand().DisposeWith(_captionDisposables);
        MergeCue.Subscribe(MergeCueCore).DisposeWith(_captionDisposables);
        WrapCues = new ReactiveCommand().DisposeWith(_captionDisposables);
        WrapCues.Subscribe(WrapCuesCore).DisposeWith(_captionDisposables);

        ResultSegments.Subscribe(ApplyTranscriptionSegments).DisposeWith(_captionDisposables);
        MaximumLineLength.Subscribe(_ => RefreshCaptionState()).DisposeWith(_captionDisposables);
        MaximumLineCount.Subscribe(_ => RefreshCaptionState()).DisposeWith(_captionDisposables);
        SelectedCaptionTemplate.Subscribe(_ => RefreshTemplatePreview()).DisposeWith(_captionDisposables);
        SelectedCue.Subscribe(_ => RefreshTemplatePreview()).DisposeWith(_captionDisposables);
        SelectedSourceLanguage.Subscribe(_ => RefreshTranslationEstimate()).DisposeWith(_captionDisposables);
        SelectedTargetLanguage.Subscribe(_ => RefreshTranslationEstimate()).DisposeWith(_captionDisposables);
        _entitlements.Entitlements.Subscribe(_ => RefreshTranslationEstimate()).DisposeWith(_captionDisposables);
        _captionDraftScopes
            .DistinctUntilChanged()
            .Subscribe(HandleCaptionDraftScopeChanged)
            .DisposeWith(_captionDisposables);
    }

    private void DisposeCaptionEditing()
    {
        foreach (EditableCaptionCueViewModel cue in _editableCues)
        {
            cue.PropertyChanged -= OnCuePropertyChanged;
        }
        _editableCues.Clear();
        _captionDraftSession?.Dispose();
        _captionDraftSession = null;
        _captionDisposables.Dispose();
        _transcriptionEstimateRevision.Dispose();
        _canStartTranslation.Dispose();
    }

    private async Task TranscribeSelectedSourceAsync(AudioSourceItem source)
    {
        ChangeCaptionDraftJob(null, deleteCurrent: true);
        long captionRevision = Interlocked.Read(ref _captionDocumentRevision);
        long draftScopeRevision = Interlocked.Read(ref _captionDraftScopeRevision);
        string? language = SelectedSourceLanguage.Value.Code;
        if (source.IsSceneMix)
        {
            await TranscribeSceneMixAsync(source, language, draftScopeRevision);
            return;
        }

        if (source.FilePath is not { } filePath)
            throw new SubtitleInputException(Strings.AiSubtitle_NoAudioInRange);

        AiTranscriptionResponse response = await _aiService.TranscribeAsync(
            new AiTranscriptionRequest(AiUploadSource.FromFile(filePath), language),
            _lifetimeCts.Token);
        string? resultLanguage = response.Language ?? language;
        AiTranscriptionSegment[] mappedSegments = source.MapSegmentsToScene(response.Segments);
        RecordCaptionDraftJob(response.JobId, draftScopeRevision);
        if (!ReferenceEquals(SelectedAudioSource.Value, source)
            || captionRevision != Interlocked.Read(ref _captionDocumentRevision)
            || !IsCurrentCaptionDraftScope(draftScopeRevision)
            || !string.Equals(
                SelectedSourceLanguage.Value.Code,
                language,
                StringComparison.Ordinal))
        {
            if (SetPartialResult(new RecoverableCaptionResult(
                CreateCaptionDocument(mappedSegments, resultLanguage),
                resultLanguage,
                mappedSegments,
                PartialResultKind.Transcription,
                1,
                1,
                draftScopeRevision)))
            {
                PartialResultMessage.Value = Strings.AiSubtitle_CompletedResultAvailable;
            }
            return;
        }

        _lastCaptionLanguage = resultLanguage;
        DetectedLanguageText.Value = CreateDetectedLanguageText(_lastCaptionLanguage);
        ResultSegments.Value = mappedSegments;
        ClearPartialResult();
    }

    private async Task TranscribeSceneMixAsync(
        AudioSourceItem source,
        string? language,
        long draftScopeRevision)
    {
        string startText = SceneRangeStartText.Value;
        string endText = SceneRangeEndText.Value;
        if (_editViewModel is null
            || !TryGetSceneRange(startText, endText, out TimeSpan rangeStart, out TimeSpan duration))
        {
            throw new SubtitleInputException(Strings.AiSubtitle_InvalidRange);
        }

        int chunkCount = (int)Math.Ceiling(duration.TotalSeconds / SceneMixChunkDuration.TotalSeconds);
        SceneTranscriptionOperation operation = CanResumeSceneTranscription(
            source,
            startText,
            endText,
            language,
            rangeStart,
            duration,
            chunkCount)
            ? _pendingSceneTranscription!
            : new SceneTranscriptionOperation(
                source,
                startText,
                endText,
                language,
                rangeStart,
                duration,
                SceneMixChunkDuration,
                chunkCount,
                Interlocked.Read(ref _captionDocumentRevision),
                draftScopeRevision,
                _editViewModel.Scene.Id);
        operation.Source = source;
        _pendingSceneTranscription = operation;

        for (int index = operation.CompletedChunkCount; index < chunkCount; index++)
        {
            _lifetimeCts.Token.ThrowIfCancellationRequested();
            TimeSpan chunkOffset = TimeSpan.FromTicks(
                Math.Min(duration.Ticks, index * operation.ChunkDuration.Ticks));
            TimeSpan chunkDuration = TimeSpan.FromTicks(
                Math.Min(operation.ChunkDuration.Ticks, duration.Ticks - chunkOffset.Ticks));
            AudioFrameSnapshot? snapshot = SceneMixAudioComposer is { } composer
                ? await composer(rangeStart + chunkOffset, chunkDuration, _lifetimeCts.Token)
                : await ((IPreviewPlayer)_editViewModel.Player)
                    .ComposeAudioAsync(rangeStart + chunkOffset, chunkDuration, _lifetimeCts.Token);
            _lifetimeCts.Token.ThrowIfCancellationRequested();
            if (snapshot is null || snapshot.SampleCount == 0)
                throw new SubtitleInputException(Strings.AiSubtitle_NoAudioInRange);

            string directory = Path.Combine(Path.GetTempPath(), "Beutl", "AI", "Audio");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, $"scene-mix-{Guid.NewGuid():N}.wav");
            try
            {
                WriteSpeechWave(snapshot, path, _lifetimeCts.Token);
                AiTranscriptionResponse response = await _aiService.TranscribeAsync(
                    new AiTranscriptionRequest(AiUploadSource.FromFile(path), language),
                    _lifetimeCts.Token);
                operation.DetectedLanguage ??= response.Language;
                foreach (AiTranscriptionSegment segment in response.Segments)
                {
                    operation.Segments.Add(new AiTranscriptionSegment
                    {
                        Start = (rangeStart + chunkOffset).TotalSeconds + segment.Start,
                        End = (rangeStart + chunkOffset).TotalSeconds + segment.End,
                        Text = segment.Text,
                    });
                }
                RecordCaptionDraftJob(response.JobId, operation.ExpectedDraftScopeRevision);
                operation.CompletedChunkCount++;
                if (!PublishSceneTranscriptionPartial(operation))
                    return;
            }
            finally
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to remove temporary scene-mix audio {Path}.", path);
                }
            }
        }

        if (!ReferenceEquals(SelectedAudioSource.Value, source)
            || SceneRangeStartText.Value != startText
            || SceneRangeEndText.Value != endText
            || !string.Equals(
                SelectedSourceLanguage.Value.Code,
                operation.Language,
                StringComparison.Ordinal)
            || operation.ExpectedCaptionRevision != Interlocked.Read(ref _captionDocumentRevision)
            || !IsCurrentCaptionDraftScope(operation.ExpectedDraftScopeRevision))
        {
            return;
        }

        _lastCaptionLanguage = operation.DetectedLanguage ?? language;
        DetectedLanguageText.Value = CreateDetectedLanguageText(_lastCaptionLanguage);
        ResultSegments.Value = CloneSegments(operation.Segments);
        ClearPartialResult();
    }

    private async Task TranslateCore()
    {
        long captionRevision = Interlocked.Read(ref _captionDocumentRevision);
        long draftScopeRevision = Interlocked.Read(ref _captionDraftScopeRevision);
        if (!TryBuildCaptionDocumentCore(out CaptionDocument? document, out _) || document is null)
            return;
        if (captionRevision != Interlocked.Read(ref _captionDocumentRevision))
            return;

        Error.Value = null;
        IsTranslating.Value = true;
        try
        {
            string targetLanguage = SelectedTargetLanguage.Value.Code!;
            string? selectedSourceLanguage = SelectedSourceLanguage.Value.Code;
            string? sourceLanguage = selectedSourceLanguage ?? _lastCaptionLanguage;
            TranslationOperation operation = CanResumeTranslation(
                captionRevision,
                targetLanguage,
                selectedSourceLanguage)
                ? _pendingTranslation!
                : CreateTranslationOperation(
                    document,
                    captionRevision,
                    sourceLanguage,
                    selectedSourceLanguage,
                    targetLanguage,
                    draftScopeRevision);
            _pendingTranslation = operation;
            if (operation.Batches.Count == 0)
            {
                _pendingTranslation = null;
                return;
            }

            for (int index = operation.CompletedBatchCount; index < operation.Batches.Count; index++)
            {
                TranslationBatch batch = operation.Batches[index];
                AiCaptionTranslationResponse response = await _aiService.TranslateAsync(
                    new AiCaptionTranslationRequest(
                        batch.Pieces.Select(piece => new AiCaptionTranslationSegment
                        {
                            Id = piece.Id,
                            Text = piece.Text,
                            Context = new AiCaptionTranslationSegmentContext(
                                piece.GroupId,
                                piece.PartIndex,
                                piece.Start,
                                piece.End),
                        }).ToArray(),
                        operation.TargetLanguage,
                        operation.SourceLanguage),
                    _lifetimeCts.Token);
                AddTranslatedBatch(operation, batch, response);
                RecordCaptionDraftJob(response.JobId, operation.ExpectedDraftScopeRevision);
                operation.CompletedBatchCount++;
                if (!PublishTranslationPartial(operation))
                    return;
            }

            if (operation.ExpectedCaptionRevision != Interlocked.Read(ref _captionDocumentRevision)
                || !IsCurrentCaptionDraftScope(operation.ExpectedDraftScopeRevision)
                || !string.Equals(
                    SelectedTargetLanguage.Value.Code,
                    targetLanguage,
                    StringComparison.Ordinal)
                || !string.Equals(
                    SelectedSourceLanguage.Value.Code,
                    selectedSourceLanguage,
                    StringComparison.Ordinal))
            {
                return;
            }

            _lastCaptionLanguage = targetLanguage;
            DetectedLanguageText.Value = CreateDetectedLanguageText(targetLanguage);
            ReplaceCues(BuildTranslationDocument(operation, includeUntranslatedParts: false));
            ClearPartialResult();
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
        catch (AiProviderErrorException)
        {
            SetCaptionErrorIfCurrent(draftScopeRevision, Strings.AiProviderError);
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to translate subtitles.");
            SetCaptionErrorIfCurrent(draftScopeRevision, Strings.AiUnexpectedError);
        }
        finally
        {
            if (!_disposed && IsCurrentCaptionDraftScope(draftScopeRevision))
            {
                IsTranslating.Value = false;
            }
        }
    }

    private bool CanResumeTranslation(
        long captionRevision,
        string targetLanguage,
        string? selectedSourceLanguage)
        => _pendingTranslation is
        {
            CompletedBatchCount: > 0,
        } operation
            && operation.CompletedBatchCount < operation.Batches.Count
            && operation.ExpectedCaptionRevision == captionRevision
            && IsCurrentCaptionDraftScope(operation.ExpectedDraftScopeRevision)
            && string.Equals(operation.TargetLanguage, targetLanguage, StringComparison.Ordinal)
            && string.Equals(
                operation.SelectedSourceLanguage,
                selectedSourceLanguage,
                StringComparison.Ordinal);

    private static TranslationOperation CreateTranslationOperation(
        CaptionDocument document,
        long captionRevision,
        string? sourceLanguage,
        string? selectedSourceLanguage,
        string targetLanguage,
        long draftScopeRevision)
    {
        var sourceDocument = new CaptionDocument(document.Cues.Select(cue => cue with { }));
        return new TranslationOperation(
            sourceDocument,
            captionRevision,
            sourceLanguage,
            selectedSourceLanguage,
            targetLanguage,
            draftScopeRevision,
            CreateTranslationBatches(sourceDocument));
    }

    private static void AddTranslatedBatch(
        TranslationOperation operation,
        TranslationBatch batch,
        AiCaptionTranslationResponse response)
    {
        var expectedIds = batch.Pieces
            .Select(piece => piece.Id)
            .ToHashSet(StringComparer.Ordinal);
        var responseById = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (AiCaptionTranslationSegment segment in response.Segments)
        {
            if (!expectedIds.Contains(segment.Id)
                || string.IsNullOrWhiteSpace(segment.Text)
                || !responseById.TryAdd(segment.Id, segment.Text))
            {
                throw new AiProviderErrorException(new InvalidDataException(
                    "The translation provider returned an invalid segment set."));
            }
        }

        if (responseById.Count != expectedIds.Count)
        {
            throw new AiProviderErrorException(new InvalidDataException(
                "The translation provider omitted one or more requested segments."));
        }

        foreach ((string id, string text) in responseById)
        {
            operation.TranslatedPieces.Add(id, text);
        }
    }

    private static CaptionDocument BuildTranslationDocument(
        TranslationOperation operation,
        bool includeUntranslatedParts)
    {
        TranslationPiece[] pieces = operation.Batches
            .SelectMany(batch => batch.Pieces)
            .ToArray();
        var translatedCues = new List<CaptionCue>(operation.SourceDocument.Count);
        for (int cueIndex = 0; cueIndex < operation.SourceDocument.Count; cueIndex++)
        {
            CaptionCue cue = operation.SourceDocument[cueIndex];
            TranslationPiece[] cuePieces = pieces
                .Where(piece => piece.CueIndex == cueIndex)
                .OrderBy(piece => piece.PartIndex)
                .ToArray();
            if (cuePieces.Length == 0)
            {
                translatedCues.Add(cue);
                continue;
            }

            bool fullyTranslated = cuePieces.All(piece =>
                operation.TranslatedPieces.ContainsKey(piece.Id));
            if (!fullyTranslated && !includeUntranslatedParts)
            {
                throw new InvalidOperationException("The translation result is incomplete.");
            }

            translatedCues.Add(cue with
            {
                Text = string.Concat(cuePieces.Select(piece =>
                    operation.TranslatedPieces.TryGetValue(piece.Id, out string? translated)
                        ? translated
                        : piece.Text)),
                Language = fullyTranslated ? operation.TargetLanguage : cue.Language,
            });
        }

        return new CaptionDocument(translatedCues);
    }

    private bool PublishTranslationPartial(TranslationOperation operation)
    {
        if (!IsCurrentCaptionDraftScope(operation.ExpectedDraftScopeRevision))
            return false;

        _pendingTranslation = operation;
        if (!SetPartialResult(new RecoverableCaptionResult(
            BuildTranslationDocument(operation, includeUntranslatedParts: true),
            operation.TargetLanguage,
            null,
            PartialResultKind.Translation,
            operation.CompletedBatchCount,
            operation.Batches.Count,
            operation.ExpectedDraftScopeRevision)))
        {
            return false;
        }
        PartialResultMessage.Value = string.Format(
            operation.CompletedBatchCount == operation.Batches.Count
                ? Strings.AiSubtitle_CompletedResultAvailable
                : Strings.AiSubtitle_PartialTranslationAvailable,
            operation.CompletedBatchCount,
            operation.Batches.Count);
        RefreshTranslationEstimate();
        return true;
    }

    private bool CanResumeSceneTranscription(
        AudioSourceItem source,
        string startText,
        string endText,
        string? language,
        TimeSpan rangeStart,
        TimeSpan duration,
        int chunkCount)
        => _pendingSceneTranscription is
        {
            CompletedChunkCount: > 0,
        } operation
            && operation.CompletedChunkCount < operation.ChunkCount
            && (operation.Source is null && source.IsSceneMix
                || ReferenceEquals(operation.Source, source))
            && operation.StartText == startText
            && operation.EndText == endText
            && operation.Language == language
            && operation.RangeStart == rangeStart
            && operation.Duration == duration
            && operation.ChunkDuration == SceneMixChunkDuration
            && operation.ChunkCount == chunkCount
            && IsCurrentCaptionDraftScope(operation.ExpectedDraftScopeRevision)
            && operation.SceneId == _editViewModel?.Scene.Id;

    private bool PublishSceneTranscriptionPartial(SceneTranscriptionOperation operation)
    {
        if (!IsCurrentCaptionDraftScope(operation.ExpectedDraftScopeRevision))
            return false;

        _pendingSceneTranscription = operation;
        string? detectedLanguage = operation.DetectedLanguage ?? operation.Language;
        AiTranscriptionSegment[] segments = CloneSegments(operation.Segments);
        if (!SetPartialResult(new RecoverableCaptionResult(
            CreateCaptionDocument(segments, detectedLanguage),
            detectedLanguage,
            segments,
            PartialResultKind.Transcription,
            operation.CompletedChunkCount,
            operation.ChunkCount,
            operation.ExpectedDraftScopeRevision)))
        {
            return false;
        }
        PartialResultMessage.Value = string.Format(
            operation.CompletedChunkCount == operation.ChunkCount
                ? Strings.AiSubtitle_CompletedResultAvailable
                : Strings.AiSubtitle_PartialTranscriptionAvailable,
            operation.CompletedChunkCount,
            operation.ChunkCount);
        _transcriptionEstimateRevision.Value++;
        return true;
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

    private bool SetPartialResult(RecoverableCaptionResult result)
    {
        if (!IsCurrentCaptionDraftScope(result.DraftScopeRevision))
            return false;

        _partialResult = result;
        HasPartialResult.Value = true;
        if (_captionDraftSession is null)
            return true;

        try
        {
            _captionDraftSession.Save(new CaptionDraftEntry(
                _captionDraftJobId,
                CreateCaptionDraft(result)));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist a recoverable paid caption result.");
        }
        return true;
    }

    private CaptionDraft CreateCaptionDraft(RecoverableCaptionResult result)
    {
        CaptionTranslationResume? translationResume = null;
        if (result.Kind == PartialResultKind.Translation
            && _pendingTranslation is { } translation)
        {
            translationResume = new CaptionTranslationResume(
                StoreCues(translation.SourceDocument.Cues),
                translation.SourceLanguage,
                translation.SelectedSourceLanguage,
                translation.TargetLanguage,
                new Dictionary<string, string>(translation.TranslatedPieces, StringComparer.Ordinal),
                translation.CompletedBatchCount);
        }

        CaptionSceneTranscriptionResume? sceneResume = null;
        if (result.Kind == PartialResultKind.Transcription
            && result.Segments is { } resultSegments
            && _pendingSceneTranscription is { } scene
            && scene.CompletedChunkCount == result.CompletedSteps
            && scene.ChunkCount == result.TotalSteps
            && SegmentsEqual(scene.Segments, resultSegments))
        {
            sceneResume = new CaptionSceneTranscriptionResume(
                scene.SceneId,
                scene.StartText,
                scene.EndText,
                scene.Language,
                scene.RangeStart,
                scene.Duration,
                scene.ChunkDuration,
                scene.ChunkCount,
                CloneSegments(scene.Segments),
                scene.DetectedLanguage,
                scene.CompletedChunkCount);
        }

        return new CaptionDraft(
            FileCaptionDraftStore.CurrentVersion,
            StoreCues(result.Document.Cues),
            result.Language,
            result.Segments is null ? null : CloneSegments(result.Segments),
            result.Kind == PartialResultKind.Translation
                ? CaptionDraftKind.Translation
                : CaptionDraftKind.Transcription,
            result.CompletedSteps,
            result.TotalSteps,
            translationResume,
            sceneResume);
    }

    private void RestoreCaptionDraft()
    {
        if (_captionDraftSession is null)
            return;

        CaptionDraftEntry? entry;
        try
        {
            entry = _captionDraftSession.Load();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to restore the recoverable paid caption result.");
            return;
        }

        if (entry is null)
            return;

        try
        {
            _captionDraftJobId = entry.JobId;
            CaptionDraft draft = entry.Draft;
            long draftScopeRevision = Interlocked.Read(ref _captionDraftScopeRevision);
            var document = new CaptionDocument(RestoreCues(draft.Cues));
            PartialResultKind kind = draft.Kind == CaptionDraftKind.Translation
                ? PartialResultKind.Translation
                : PartialResultKind.Transcription;
            _partialResult = new RecoverableCaptionResult(
                document,
                draft.Language,
                draft.Segments is null ? null : CloneSegments(draft.Segments),
                kind,
                draft.CompletedSteps,
                draft.TotalSteps,
                draftScopeRevision);

            if (draft.TranslationResume is { } translation)
            {
                try
                {
                    var sourceDocument = new CaptionDocument(
                        RestoreCues(translation.SourceCues));
                    List<TranslationBatch> batches = CreateTranslationBatches(sourceDocument);
                    if (translation.CompletedBatchCount <= 0
                        || translation.CompletedBatchCount > batches.Count)
                    {
                        throw new InvalidDataException("The stored translation progress is invalid.");
                    }

                    var operation = new TranslationOperation(
                        sourceDocument,
                        Interlocked.Read(ref _captionDocumentRevision),
                        translation.SourceLanguage,
                        translation.SelectedSourceLanguage,
                        translation.TargetLanguage,
                        draftScopeRevision,
                        batches)
                    {
                        CompletedBatchCount = translation.CompletedBatchCount,
                    };
                    HashSet<string> completedIds = batches
                        .Take(operation.CompletedBatchCount)
                        .SelectMany(batch => batch.Pieces)
                        .Select(piece => piece.Id)
                        .ToHashSet(StringComparer.Ordinal);
                    if (translation.TranslatedPieces.Count != completedIds.Count
                        || translation.TranslatedPieces.Keys.Any(id => !completedIds.Contains(id)))
                    {
                        throw new InvalidDataException("The stored translated segments are invalid.");
                    }
                    foreach ((string id, string text) in translation.TranslatedPieces)
                    {
                        operation.TranslatedPieces.Add(id, text);
                    }
                    _pendingTranslation = operation;
                    CaptionLanguageOption? sourceOption = SourceLanguages.FirstOrDefault(option =>
                        string.Equals(
                            option.Code,
                            translation.SelectedSourceLanguage,
                            StringComparison.Ordinal));
                    CaptionLanguageOption? targetOption = TargetLanguages.FirstOrDefault(option =>
                        string.Equals(
                            option.Code,
                            translation.TargetLanguage,
                            StringComparison.Ordinal));
                    if (sourceOption is not null)
                        SelectedSourceLanguage.Value = sourceOption;
                    if (targetOption is not null)
                        SelectedTargetLanguage.Value = targetOption;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Restored a paid caption draft without resumable translation state.");
                }
            }

            if (draft.SceneTranscriptionResume is { } scene)
            {
                _pendingSceneTranscription = new SceneTranscriptionOperation(
                    null,
                    scene.StartText,
                    scene.EndText,
                    scene.Language,
                    scene.RangeStart,
                    scene.Duration,
                    scene.ChunkDuration,
                    scene.ChunkCount,
                    Interlocked.Read(ref _captionDocumentRevision),
                    draftScopeRevision,
                    scene.SceneId)
                {
                    CompletedChunkCount = scene.CompletedChunkCount,
                    DetectedLanguage = scene.DetectedLanguage,
                };
                _pendingSceneTranscription.Segments.AddRange(CloneSegments(scene.Segments));
                SceneRangeStartText.Value = scene.StartText;
                SceneRangeEndText.Value = scene.EndText;
                CaptionLanguageOption? sourceOption = SourceLanguages.FirstOrDefault(option =>
                    string.Equals(option.Code, scene.Language, StringComparison.Ordinal));
                if (sourceOption is not null)
                    SelectedSourceLanguage.Value = sourceOption;
            }

            HasPartialResult.Value = true;
            PartialResultMessage.Value = draft.CompletedSteps == draft.TotalSteps
                ? Strings.AiSubtitle_CompletedResultAvailable
                : string.Format(
                    kind == PartialResultKind.Translation
                        ? Strings.AiSubtitle_PartialTranslationAvailable
                        : Strings.AiSubtitle_PartialTranscriptionAvailable,
                    draft.CompletedSteps,
                    draft.TotalSteps);
            _transcriptionEstimateRevision.Value++;
            RefreshTranslationEstimate();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ignored an invalid recoverable caption draft.");
            ClearPartialResult();
        }
    }

    private static bool SegmentsEqual(
        IReadOnlyList<AiTranscriptionSegment> first,
        IReadOnlyList<AiTranscriptionSegment> second)
    {
        if (first.Count != second.Count)
            return false;

        for (int index = 0; index < first.Count; index++)
        {
            if (first[index].Start != second[index].Start
                || first[index].End != second[index].End
                || first[index].Text != second[index].Text)
            {
                return false;
            }
        }
        return true;
    }

    private static StoredCaptionCue[] StoreCues(IEnumerable<CaptionCue> cues)
        => cues.Select(cue => new StoredCaptionCue(
            cue.Start.Ticks,
            cue.End.Ticks,
            cue.Text,
            cue.Speaker,
            cue.Language,
            cue.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)))
            .ToArray();

    private static CaptionCue[] RestoreCues(IEnumerable<StoredCaptionCue> cues)
        => cues.Select(cue => new CaptionCue(
            TimeSpan.FromTicks(cue.StartTicks),
            TimeSpan.FromTicks(cue.EndTicks),
            cue.Text,
            cue.Speaker,
            cue.Language,
            new CaptionMetadata(cue.Metadata)))
            .ToArray();

    private void ApplyPartialResultCore()
    {
        if (_partialResult is not { } result
            || !IsCurrentCaptionDraftScope(result.DraftScopeRevision))
            return;

        _lastCaptionLanguage = result.Language;
        DetectedLanguageText.Value = CreateDetectedLanguageText(result.Language);
        if (result.Segments is { } segments)
        {
            ResultSegments.Value = CloneSegments(segments);
        }
        else
        {
            ReplaceCues(new CaptionDocument(result.Document.Cues.Select(cue => cue with { })));
        }

        if (result.Kind == PartialResultKind.Translation
            && _pendingTranslation is { } translation)
        {
            translation.ExpectedCaptionRevision = Interlocked.Read(ref _captionDocumentRevision);
        }
        else if (result.Kind == PartialResultKind.Transcription
            && _pendingSceneTranscription is { } transcription)
        {
            transcription.ExpectedCaptionRevision = Interlocked.Read(ref _captionDocumentRevision);
        }
        Error.Value = null;
        if (result.CompletedSteps == result.TotalSteps)
        {
            ClearPartialResult();
        }
        else
        {
            RefreshTranslationEstimate();
        }
    }

    private void ClearPartialResult()
    {
        _partialResult = null;
        HasPartialResult.Value = false;
        PartialResultMessage.Value = null;
        _pendingTranslation = null;
        _pendingSceneTranscription = null;
        try
        {
            _captionDraftSession?.Delete();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove the recoverable caption draft.");
        }
        _transcriptionEstimateRevision.Value++;
        RefreshTranslationEstimate();
    }

    private void InvalidatePartialResultResume()
    {
        _transcriptionEstimateRevision.Value++;
        RefreshTranslationEstimate();
    }

    private async Task ImportCaptionsCore()
    {
        IStorageProvider? storage = GetStorageProvider();
        if (storage is null)
            return;

        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = [CreateCaptionFileType()],
        });
        if (files.Count == 0)
            return;

        try
        {
            if (!_captionCodecs.TryGetByFileName(
                    files[0].Name,
                    out CaptionCodecInfo? codec)
                || !codec.CanDecode)
            {
                throw new NotSupportedException("No caption codec is registered for this file extension.");
            }
            await using Stream stream = await files[0].OpenReadAsync();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory, _lifetimeCts.Token);
            ImportCaptionBytes(memory.ToArray(), codec.Format);
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import captions.");
            Error.Value = Strings.AiSubtitle_ImportFailed;
        }
    }

    private async Task ExportCaptionsCore()
    {
        if (!TryBuildCaptionDocument(out CaptionDocument? document, out _) || document is null)
            return;

        IStorageProvider? storage = GetStorageProvider();
        if (storage is null)
            return;

        IStorageFile? file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = "subtitles.srt",
            DefaultExtension = "srt",
            FileTypeChoices = [CreateCaptionFileType()],
        });
        if (file is null)
            return;

        try
        {
            if (!_captionCodecs.TryGetByFileName(
                    file.Name,
                    out CaptionCodecInfo? codec)
                || !codec.CanEncode)
            {
                throw new NotSupportedException("No caption codec is registered for this file extension.");
            }
            byte[] bytes = ExportCaptionBytes(codec.Format);
            await using Stream stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await stream.WriteAsync(bytes, _lifetimeCts.Token);
            NotificationService.ShowSuccess(Strings.AiSubtitle, Strings.AiSubtitle_Exported);
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export captions.");
            Error.Value = Strings.AiSubtitle_ExportFailed;
        }
    }

    private void AddCueCore()
    {
        TimeSpan start = _editableCues.LastOrDefault()?.TryCreateCue(out CaptionCue? last) == true
            ? last!.End
            : _editViewModel?.Player.CurrentFrame.Value ?? TimeSpan.Zero;
        var cue = new CaptionCue(start, start + TimeSpan.FromSeconds(2), string.Empty);
        var item = new EditableCaptionCueViewModel(_editableCues.Count + 1, cue);
        AttachCue(item);
        _editableCues.Add(item);
        MarkCaptionDocumentChanged();
        SelectedCue.Value = item;
        RefreshCaptionState();
    }

    internal bool ImportCaptionBytes(ReadOnlySpan<byte> bytes, CaptionFormatId format)
    {
        CaptionImportResult result = _captionSerializer.Import(bytes, format);
        if (result.Document is null)
        {
            Error.Value = result.Diagnostics.FirstOrDefault()?.Message ?? Strings.AiSubtitle_ImportFailed;
            return false;
        }

        ChangeCaptionDraftJob(null, deleteCurrent: true);
        _lastCaptionLanguage = null;
        DetectedLanguageText.Value = null;
        ReplaceCues(result.Document);
        Error.Value = result.Diagnostics.FirstOrDefault()?.Message;
        return true;
    }

    private void ChangeCaptionDraftJob(string? jobId, bool deleteCurrent)
    {
        if (deleteCurrent && _captionDraftSession is not null)
        {
            try
            {
                _captionDraftSession.Delete();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to remove the previous recoverable caption draft.");
            }
        }
        _captionDraftJobId = string.IsNullOrWhiteSpace(jobId) ? null : jobId.Trim();
    }

    private void HandleCaptionDraftScopeChanged(CaptionDraftScope? scope)
    {
        if (_captionDraftScopeInitialized && _captionDraftBaseScope == scope)
            return;

        bool resetCaptionState = _captionDraftScopeInitialized;
        _captionDraftScopeInitialized = true;
        Interlocked.Increment(ref _captionDraftScopeRevision);
        _captionDraftBaseScope = scope;
        _captionDraftJobId = null;
        if (resetCaptionState)
        {
            ResetCaptionStateForAccountChange();
        }

        OpenCaptionDraftSession(scope);
        RestoreCaptionDraft();
    }

    private bool IsCurrentCaptionDraftScope(long revision)
        => revision == Interlocked.Read(ref _captionDraftScopeRevision);

    private void SetCaptionErrorIfCurrent(long revision, string error)
    {
        if (IsCurrentCaptionDraftScope(revision))
        {
            Error.Value = error;
        }
    }

    private void RecordCaptionDraftJob(AiJobId? jobId, long revision)
    {
        if (jobId is { } value
            && value.Value.Length > 0
            && IsCurrentCaptionDraftScope(revision))
        {
            _captionDraftJobId = value.Value;
        }
    }

    private void ResetCaptionStateForAccountChange()
    {
        _partialResult = null;
        _pendingTranslation = null;
        _pendingSceneTranscription = null;
        _pendingHistoryResult = null;
        _lastCaptionLanguage = null;
        HasPartialResult.Value = false;
        HasPendingHistoryResult.Value = false;
        PartialResultMessage.Value = null;
        HistoryOverwriteMessage.Value = null;
        DetectedLanguageText.Value = null;
        Error.Value = null;
        IsTranscribing.Value = false;
        IsTranslating.Value = false;
        ResultSegments.Value = null;
        ReplaceCues(new CaptionDocument());
        SelectedSourceLanguage.Value = SourceLanguages[0];
        SelectedTargetLanguage.Value = GetDefaultTargetLanguage();
        TimeSpan rangeStart = _editViewModel?.Scene.Start ?? TimeSpan.Zero;
        TimeSpan rangeEnd = rangeStart + (_editViewModel?.Scene.Duration ?? TimeSpan.FromMinutes(5));
        SceneRangeStartText.Value = EditableCaptionCueViewModel.FormatTime(rangeStart);
        SceneRangeEndText.Value = EditableCaptionCueViewModel.FormatTime(rangeEnd);
        _transcriptionEstimateRevision.Value++;
        RefreshTranslationEstimate();
    }

    private CaptionLanguageOption GetDefaultTargetLanguage()
    {
        string targetCode = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja" ? "en" : "ja";
        return TargetLanguages.First(option => option.Code == targetCode);
    }

    private void OpenCaptionDraftSession(CaptionDraftScope? scope)
    {
        _captionDraftSession?.Dispose();
        _captionDraftSession = null;
        if (scope is not null && _captionDraftStore.TryOpen(scope, out ICaptionDraftSession? session))
        {
            _captionDraftSession = session;
        }
    }

    internal byte[] ExportCaptionBytes(CaptionFormatId format)
    {
        if (!TryBuildCaptionDocument(out CaptionDocument? document, out string? error)
            || document is null)
        {
            throw new CaptionExportException(null, error ?? Strings.AiSubtitle_ExportFailed);
        }
        return _captionSerializer.Export(document, format);
    }

    private void DeleteCueCore()
    {
        if (SelectedCue.Value is not { } selected)
            return;

        int index = _editableCues.IndexOf(selected);
        if (index < 0)
            return;

        selected.PropertyChanged -= OnCuePropertyChanged;
        _editableCues.RemoveAt(index);
        RenumberCues();
        MarkCaptionDocumentChanged();
        SelectedCue.Value = _editableCues.Count == 0
            ? null
            : _editableCues[Math.Min(index, _editableCues.Count - 1)];
        RefreshCaptionState();
    }

    private void SplitCueCore()
    {
        if (SelectedCue.Value is not { } selected
            || !TryBuildCaptionDocumentCore(out CaptionDocument? document, out _)
            || document is null)
        {
            return;
        }

        int index = _editableCues.IndexOf(selected);
        if (index < 0)
            return;

        CaptionCue cue = document[index];
        TimeSpan splitTime = _editViewModel?.Player.CurrentFrame.Value ?? default;
        if (splitTime <= cue.Start || splitTime >= cue.End)
        {
            splitTime = cue.Start + TimeSpan.FromTicks((cue.End - cue.Start).Ticks / 2);
        }
        int textOffset = FindProportionalTextOffset(cue.Text, cue.Start, cue.End, splitTime);
        document.SplitCue(index, splitTime, textOffset);
        ReplaceCues(document);
        SelectedCue.Value = _editableCues[index + 1];
    }

    private void MergeCueCore()
    {
        if (SelectedCue.Value is not { } selected
            || !TryBuildCaptionDocumentCore(out CaptionDocument? document, out _)
            || document is null)
        {
            return;
        }

        int index = _editableCues.IndexOf(selected);
        if (index < 0 || index >= document.Count - 1)
            return;

        document.MergeWithNext(index);
        ReplaceCues(document);
        SelectedCue.Value = _editableCues[index];
    }

    private void WrapCuesCore()
    {
        CaptionTextConstraints constraints = CreateTextConstraints();
        foreach (EditableCaptionCueViewModel cue in _editableCues)
        {
            cue.Text = CaptionTextWrapper.Wrap(cue.Text, constraints);
        }
        RefreshCaptionState();
    }

    private void ApplyTranscriptionSegments(AiTranscriptionSegment[]? segments)
    {
        if (segments is not { Length: > 0 })
        {
            ReplaceCues(new CaptionDocument());
            return;
        }

        ReplaceCues(new CaptionDocument(segments.Select(segment => new CaptionCue(
            TimeSpan.FromSeconds(segment.Start),
            TimeSpan.FromSeconds(segment.End),
            segment.Text,
            language: _lastCaptionLanguage))));
    }

    private void ReplaceCues(CaptionDocument document)
    {
        foreach (EditableCaptionCueViewModel cue in _editableCues)
        {
            cue.PropertyChanged -= OnCuePropertyChanged;
        }
        _editableCues.Clear();
        for (int index = 0; index < document.Count; index++)
        {
            var cue = new EditableCaptionCueViewModel(index + 1, document[index]);
            AttachCue(cue);
            _editableCues.Add(cue);
        }
        MarkCaptionDocumentChanged();
        SelectedCue.Value = _editableCues.FirstOrDefault();
        RefreshCaptionState();
    }

    private void AttachCue(EditableCaptionCueViewModel cue)
        => cue.PropertyChanged += OnCuePropertyChanged;

    private void OnCuePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(EditableCaptionCueViewModel.Number))
        {
            MarkCaptionDocumentChanged();
        }
        RefreshCaptionState();
    }

    private void MarkCaptionDocumentChanged()
        => Interlocked.Increment(ref _captionDocumentRevision);

    private void RenumberCues()
    {
        for (int index = 0; index < _editableCues.Count; index++)
        {
            _editableCues[index].Number = index + 1;
        }
    }

    private void RefreshCaptionState()
    {
        bool timingValid = TryBuildCaptionDocumentCore(out CaptionDocument? document, out string? parseError);
        HasTimingValidCues.Value = timingValid && document is { Count: > 0 };
        if (!timingValid || document is null)
        {
            HasValidCues.Value = false;
            CaptionValidationMessage.Value = parseError;
        }
        else
        {
            IReadOnlyList<CaptionValidationIssue> issues = CaptionDocumentValidator.Validate(
                document,
                CreateTextConstraints());
            CaptionValidationIssue[] blockingIssues = GetBlockingIssues(issues);
            HasValidCues.Value = document.Count > 0 && blockingIssues.Length == 0;
            CaptionValidationMessage.Value = blockingIssues.Length > 0
                ? string.Format(Strings.AiSubtitle_ValidationIssues, blockingIssues.Length)
                : issues.Any(issue => issue.Kind == CaptionValidationIssueKind.Overlap)
                    ? Strings.AiSubtitle_OverlapWarning
                    : null;
        }

        RefreshTranslationEstimate();
        RefreshTemplatePreview();
    }

    private void RefreshTranslationEstimate()
    {
        bool serverAllows = _entitlements.Entitlements.Value?.Availability.CanStart(
            AiOperations.CaptionTranslation) ?? false;
        if (_pendingTranslation is { } operation
            && operation.CompletedBatchCount < operation.Batches.Count
            && operation.ExpectedCaptionRevision == Interlocked.Read(ref _captionDocumentRevision)
            && IsCurrentCaptionDraftScope(operation.ExpectedDraftScopeRevision)
            && string.Equals(
                operation.TargetLanguage,
                SelectedTargetLanguage.Value.Code,
                StringComparison.Ordinal)
            && string.Equals(
                operation.SelectedSourceLanguage,
                SelectedSourceLanguage.Value.Code,
                StringComparison.Ordinal))
        {
            _canStartTranslation.Value = serverAllows
                && operation.Batches.Skip(operation.CompletedBatchCount).Any();
            return;
        }

        _canStartTranslation.Value = serverAllows
            && _editableCues.Any(cue => !string.IsNullOrWhiteSpace(cue.Text));
    }

    private void RefreshTemplatePreview()
    {
        string text = SelectedCue.Value?.Text
            ?? _editableCues.FirstOrDefault()?.Text
            ?? Strings.AiSubtitle_PreviewSample;
        TemplatePreviewText.Value = string.IsNullOrWhiteSpace(text)
            ? Strings.AiSubtitle_PreviewSample
            : text;
        try
        {
            var cue = new CaptionCue(
                TimeSpan.Zero,
                TimeSpan.FromSeconds(2),
                TemplatePreviewText.Value);
            var context = new CaptionElementContext(0, Strings.AiSubtitle);
            using CaptionTemplateLease template = _captionTemplates.Acquire(
                SelectedCaptionTemplate.Value.Id);
            Beutl.Graphics.Shapes.TextBlock? preview = template
                .CreateElements(cue, context)
                .Select(description => description.Source)
                .OfType<ElementSource.EngineObject>()
                .Select(source => source.Factory())
                .OfType<Beutl.Graphics.Shapes.TextBlock>()
                .FirstOrDefault();
            TemplatePreviewFontSize.Value = preview is null
                ? 24
                : Math.Clamp(preview.Size.CurrentValue * 0.5, 12, 42);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to create a subtitle template preview.");
            TemplatePreviewFontSize.Value = 24;
        }
    }

    internal bool TryBuildCaptionDocument(out CaptionDocument? document, out string? error)
    {
        if (!TryBuildCaptionDocumentCore(out document, out error) || document is null)
            return false;

        IReadOnlyList<CaptionValidationIssue> issues = CaptionDocumentValidator.Validate(
            document,
            CreateTextConstraints());
        CaptionValidationIssue[] blockingIssues = GetBlockingIssues(issues);
        if (blockingIssues.Length > 0)
        {
            error = string.Format(Strings.AiSubtitle_ValidationIssues, blockingIssues.Length);
            return false;
        }
        return document.Count > 0;
    }

    private bool TryBuildCaptionDocumentCore(out CaptionDocument? document, out string? error)
    {
        var cues = new List<CaptionCue>(_editableCues.Count);
        foreach (EditableCaptionCueViewModel item in _editableCues)
        {
            if (!item.TryCreateCue(out CaptionCue? cue) || cue is null)
            {
                document = null;
                error = Strings.AiSubtitle_InvalidTiming;
                return false;
            }
            cues.Add(cue);
        }
        document = new CaptionDocument(cues);
        error = null;
        return true;
    }

    private CaptionTextConstraints CreateTextConstraints()
        => new(Math.Max(MaximumLineLength.Value, 1), Math.Max(MaximumLineCount.Value, 1));

    private static CaptionValidationIssue[] GetBlockingIssues(
        IReadOnlyList<CaptionValidationIssue> issues)
        => issues.Where(issue => issue.Kind != CaptionValidationIssueKind.Overlap).ToArray();

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

    private int CalculateRemainingTranscriptionUnits(
        AudioSourceItem? source,
        string startText,
        string endText,
        int rate)
    {
        if (rate <= 0
            || source is null
            || _pendingSceneTranscription is not { } operation
            || operation.CompletedChunkCount >= operation.ChunkCount
            || !(operation.Source is null && source.IsSceneMix
                || ReferenceEquals(operation.Source, source))
            || operation.StartText != startText
            || operation.EndText != endText
            || operation.Language != SelectedSourceLanguage.Value.Code
            || operation.ChunkDuration != SceneMixChunkDuration
            || operation.SceneId != _editViewModel?.Scene.Id)
        {
            return CalculateTranscriptionUnits(source, startText, endText, rate);
        }

        int units = 0;
        for (int index = operation.CompletedChunkCount; index < operation.ChunkCount; index++)
        {
            TimeSpan offset = TimeSpan.FromTicks(Math.Min(
                operation.Duration.Ticks,
                index * operation.ChunkDuration.Ticks));
            TimeSpan chunkDuration = TimeSpan.FromTicks(Math.Min(
                operation.ChunkDuration.Ticks,
                operation.Duration.Ticks - offset.Ticks));
            units += Math.Max(1, (int)Math.Ceiling(chunkDuration.TotalMinutes)) * rate;
        }
        return units;
    }

    private static int CalculateTranscriptionUnits(
        AudioSourceItem? source,
        string startText,
        string endText,
        int rate)
    {
        if (source is null || rate <= 0)
            return 0;
        TimeSpan duration = source.Duration;
        if (source.IsSceneMix
            && (!TryGetSceneRange(startText, endText, out _, out duration) || duration <= TimeSpan.Zero))
        {
            return 0;
        }
        return Math.Max(1, (int)Math.Ceiling(duration.TotalMinutes)) * rate;
    }

    internal static int CalculateTranslationUnits(IEnumerable<string> texts, int rate)
    {
        if (rate <= 0)
            return 0;

        int units = 0;
        int segmentCount = 0;
        int characters = 0;
        foreach (string text in texts.Where(text => !string.IsNullOrWhiteSpace(text)))
        {
            foreach (string part in SplitTranslationText(text))
            {
                if (segmentCount == TranslationBatchSegmentLimit
                    || characters + part.Length > TranslationBatchCharacterLimit)
                {
                    units += Math.Max(1, (int)Math.Ceiling(characters / 1000d)) * rate;
                    segmentCount = 0;
                    characters = 0;
                }
                segmentCount++;
                characters += part.Length;
            }
        }
        if (segmentCount > 0)
        {
            units += Math.Max(1, (int)Math.Ceiling(characters / 1000d)) * rate;
        }
        return units;
    }

    private static int CalculateTranslationBatchUnits(
        IEnumerable<TranslationBatch> batches,
        int rate)
    {
        if (rate <= 0)
            return 0;

        return batches.Sum(batch =>
        {
            int characters = batch.Pieces.Sum(piece => piece.Text.Length);
            return Math.Max(1, (int)Math.Ceiling(characters / 1000d)) * rate;
        });
    }

    private static bool TryGetSceneRange(
        string startText,
        string endText,
        out TimeSpan start,
        out TimeSpan duration)
    {
        duration = default;
        if (!EditableCaptionCueViewModel.TryParseTime(startText, out start)
            || !EditableCaptionCueViewModel.TryParseTime(endText, out TimeSpan end)
            || start < TimeSpan.Zero
            || end <= start)
        {
            return false;
        }
        duration = end - start;
        return true;
    }

    private static string? CreateDetectedLanguageText(string? language)
        => string.IsNullOrWhiteSpace(language)
            ? null
            : string.Format(Strings.AiSubtitle_DetectedLanguage, language);

    private static List<TranslationBatch> CreateTranslationBatches(CaptionDocument document)
    {
        var batches = new List<TranslationBatch>();
        var current = new List<TranslationPiece>();
        int characters = 0;
        for (int cueIndex = 0; cueIndex < document.Count; cueIndex++)
        {
            string text = document[cueIndex].Text;
            if (string.IsNullOrWhiteSpace(text))
                continue;

            int partIndex = 0;
            foreach (string part in SplitTranslationText(text))
            {
                if (current.Count == TranslationBatchSegmentLimit
                    || characters + part.Length > TranslationBatchCharacterLimit)
                {
                    batches.Add(new TranslationBatch(current.ToArray()));
                    current.Clear();
                    characters = 0;
                }
                current.Add(new TranslationPiece(
                    cueIndex,
                    partIndex,
                    $"c{cueIndex}-p{partIndex}",
                    $"c{cueIndex}",
                    document[cueIndex].Start,
                    document[cueIndex].End,
                    part));
                characters += part.Length;
                partIndex++;
            }
        }
        if (current.Count > 0)
        {
            batches.Add(new TranslationBatch(current.ToArray()));
        }
        return batches;
    }

    private static IEnumerable<string> SplitTranslationText(string text)
    {
        int offset = 0;
        while (text.Length - offset > TranslationBatchCharacterLimit)
        {
            int length = TranslationBatchCharacterLimit;
            if (char.IsHighSurrogate(text[offset + length - 1])
                && char.IsLowSurrogate(text[offset + length]))
            {
                length--;
            }
            yield return text.Substring(offset, length);
            offset += length;
        }
        if (offset < text.Length)
        {
            yield return text[offset..];
        }
    }

    private static int FindProportionalTextOffset(
        string text,
        TimeSpan start,
        TimeSpan end,
        TimeSpan split)
    {
        int[] boundaries = StringInfo.ParseCombiningCharacters(text);
        if (boundaries.Length <= 1)
            return text.Length;

        double fraction = (split - start).TotalSeconds / (end - start).TotalSeconds;
        int elementIndex = Math.Clamp((int)Math.Round(boundaries.Length * fraction), 1, boundaries.Length - 1);
        return boundaries[elementIndex];
    }

    internal static void WriteSpeechWave(
        AudioFrameSnapshot snapshot,
        string path,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (snapshot.SampleRate <= 0 || snapshot.ChannelCount <= 0 || snapshot.SampleCount <= 0)
            throw new SubtitleInputException(Strings.AiSubtitle_NoAudioInRange);

        int outputRate = Math.Min(snapshot.SampleRate, 16_000);
        double sourceFramesPerOutputFrame = snapshot.SampleRate / (double)outputRate;
        int outputFrames = Math.Max(1, (int)Math.Floor(snapshot.SampleCount / sourceFramesPerOutputFrame));
        int dataLength = checked(outputFrames * sizeof(short));
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: false);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataLength);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(outputRate);
        writer.Write(outputRate * sizeof(short));
        writer.Write((short)sizeof(short));
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataLength);

        for (int outputIndex = 0; outputIndex < outputFrames; outputIndex++)
        {
            if ((outputIndex & 4095) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            int sourceFrame = Math.Min(
                snapshot.SampleCount - 1,
                (int)Math.Floor(outputIndex * sourceFramesPerOutputFrame));
            double mono = 0;
            for (int channel = 0; channel < snapshot.ChannelCount; channel++)
            {
                float sample = snapshot.Interleaved[sourceFrame * snapshot.ChannelCount + channel];
                mono += float.IsFinite(sample) ? sample : 0;
            }
            mono = Math.Clamp(mono / snapshot.ChannelCount, -1, 1);
            writer.Write((short)Math.Round(mono * short.MaxValue));
        }
    }

    private FilePickerFileType CreateCaptionFileType()
        => new(Strings.AiSubtitle_CaptionFiles)
        {
            Patterns = _captionCodecs.Codecs
                .SelectMany(codec => codec.FileExtensions)
                .Select(extension => $"*{extension}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(pattern => pattern, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            MimeTypes = ["text/plain", "application/octet-stream"],
        };

    private static IStorageProvider? GetStorageProvider()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime
            { MainWindow: { } window })
        {
            return null;
        }
        return TopLevel.GetTopLevel(window)?.StorageProvider;
    }

    private sealed record TranslationPiece(
        int CueIndex,
        int PartIndex,
        string Id,
        string GroupId,
        TimeSpan Start,
        TimeSpan End,
        string Text);

    private sealed record TranslationBatch(IReadOnlyList<TranslationPiece> Pieces);

    private sealed class TranslationOperation(
        CaptionDocument sourceDocument,
        long expectedCaptionRevision,
        string? sourceLanguage,
        string? selectedSourceLanguage,
        string targetLanguage,
        long expectedDraftScopeRevision,
        IReadOnlyList<TranslationBatch> batches)
    {
        public CaptionDocument SourceDocument { get; } = sourceDocument;

        public long ExpectedCaptionRevision { get; set; } = expectedCaptionRevision;

        public string? SourceLanguage { get; } = sourceLanguage;

        public string? SelectedSourceLanguage { get; } = selectedSourceLanguage;

        public string TargetLanguage { get; } = targetLanguage;

        public long ExpectedDraftScopeRevision { get; } = expectedDraftScopeRevision;

        public IReadOnlyList<TranslationBatch> Batches { get; } = batches;

        public Dictionary<string, string> TranslatedPieces { get; } = new(StringComparer.Ordinal);

        public int CompletedBatchCount { get; set; }
    }

    private sealed class SceneTranscriptionOperation(
        AudioSourceItem? source,
        string startText,
        string endText,
        string? language,
        TimeSpan rangeStart,
        TimeSpan duration,
        TimeSpan chunkDuration,
        int chunkCount,
        long expectedCaptionRevision,
        long expectedDraftScopeRevision,
        Guid sceneId)
    {
        public AudioSourceItem? Source { get; set; } = source;

        public string StartText { get; } = startText;

        public string EndText { get; } = endText;

        public string? Language { get; } = language;

        public TimeSpan RangeStart { get; } = rangeStart;

        public TimeSpan Duration { get; } = duration;

        public TimeSpan ChunkDuration { get; } = chunkDuration;

        public int ChunkCount { get; } = chunkCount;

        public long ExpectedCaptionRevision { get; set; } = expectedCaptionRevision;

        public long ExpectedDraftScopeRevision { get; } = expectedDraftScopeRevision;

        public Guid SceneId { get; } = sceneId;

        public List<AiTranscriptionSegment> Segments { get; } = [];

        public string? DetectedLanguage { get; set; }

        public int CompletedChunkCount { get; set; }
    }

    private sealed record RecoverableCaptionResult(
        CaptionDocument Document,
        string? Language,
        AiTranscriptionSegment[]? Segments,
        PartialResultKind Kind,
        int CompletedSteps,
        int TotalSteps,
        long DraftScopeRevision);

    private enum PartialResultKind
    {
        Translation,
        Transcription,
    }
}

internal sealed class SubtitleInputException : Exception
{
    public SubtitleInputException()
    {
    }

    public SubtitleInputException(string message)
        : base(message)
    {
    }

    public SubtitleInputException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
