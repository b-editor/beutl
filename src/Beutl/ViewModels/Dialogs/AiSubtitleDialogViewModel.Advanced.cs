using System.Collections.ObjectModel;
using System.Collections.Specialized;
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
using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Source;
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
    // Long enough to keep the number of paid requests down, short enough that a
    // chunk stays inside what one upload may carry: 16 kHz mono 16-bit PCM runs
    // at 32 kB a second, so the endpoint's 25 MiB stops a little under fourteen
    // minutes.
    private static readonly TimeSpan s_sceneMixChunkDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan s_sceneMixComposeSlice = TimeSpan.FromSeconds(5);
    private readonly CompositeDisposable _captionDisposables = [];
    private readonly ObservableCollection<EditableCaptionCueViewModel> _editableCues = [];
    private readonly ReactivePropertySlim<long> _transcriptionEstimateRevision = new();
    private readonly ReactivePropertySlim<long> _translationEstimateRevision = new();
    private readonly ReactivePropertySlim<bool> _canDeleteCue = new();
    private readonly ReactivePropertySlim<bool> _canSplitCue = new();
    private readonly ReactivePropertySlim<bool> _canMergeCue = new();
    private string? _lastCaptionLanguage;
    private TimeSpan _sceneMixChunkDuration = s_sceneMixChunkDuration;
    private long _captionDocumentRevision;
    private long _sceneAudioRevision;
    private RecoverableCaptionResult? _partialResult;
    private TranslationOperation? _pendingTranslation;
    private SceneTranscriptionOperation? _pendingSceneTranscription;
    private SourceTranscriptionOperation? _pendingSourceTranscription;
    // Which model a restored run was named for, until the pickers have loaded
    // and can be put back on it.
    private AiModelId? _restoredTranscriptionModel;
    private AiModelId? _restoredTranslationModel;
    private AiCaptionHistoryResult? _pendingHistoryResult;
    private ICaptionDraftSession? _captionDraftSession;
    private CaptionDraftScope? _captionDraftBaseScope;
    private string? _captionDraftJobId;
    private long _captionDraftScopeRevision;
    private bool _captionDraftScopeInitialized;
    private AiOperationAvailabilityTracker _transcriptionAvailability = null!;
    private AiOperationAvailabilityTracker _translationAvailability = null!;

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
        _pendingSourceTranscription = null;
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

    /// <summary>
    /// How much of the scene one composition call asks for. An upload chunk is
    /// minutes long, and composing that in one call materializes every sample in
    /// it as float — hundreds of megabytes — while the compose thread, and the
    /// editor waiting on it, can do nothing else.
    /// </summary>
    internal static TimeSpan SceneMixComposeSlice => s_sceneMixComposeSlice;

    internal Func<TimeSpan, TimeSpan, CancellationToken, Task<AudioFrameSnapshot?>>?
        SceneMixAudioComposer
    { get; set; }

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
    /// Whether a caption run has named a piece the server has not been seen to
    /// settle. Not the same as having a partial result: the very first piece of
    /// a run may have been charged and lost before anything came back.
    /// </summary>
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
        DetectedLanguageText = new ReactivePropertySlim<string?>().DisposeWith(_captionDisposables);
        HasCues = new ReactivePropertySlim<bool>(false).DisposeWith(_captionDisposables);
        _editableCues.CollectionChanged += OnEditableCuesChanged;
        HasValidCues = new ReactivePropertySlim<bool>(false).DisposeWith(_captionDisposables);
        HasTimingValidCues = new ReactivePropertySlim<bool>(false).DisposeWith(_captionDisposables);
        IsTranslating = new ReactivePropertySlim<bool>(false).DisposeWith(_captionDisposables);
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

        CanTranslate = HasTimingValidCues
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
            .CombineLatest(
                TranslationModelPicker.OffersNothingUsable,
                HasOutstandingTranslationRequest,
                // Every model the operation registered was ruled out, so a new
                // request would be refused however it is shaped — but a run
                // already holding a name is answered from the job it made.
                (can, nothingUsable, outstanding) =>
                    can && (!nothingUsable || outstanding))
            // Until the list has been asked for, a request would name no model
            // and run on the server's default, which may cost more than what
            // this screen was about to offer.
            .CombineLatest(
                TranslationModelPicker.IsLoaded,
                (can, loaded) => can && loaded)
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

        ResultSegments.Subscribe(ApplyTranscriptionSegments).DisposeWith(_captionDisposables);
        MaximumLineLength.Subscribe(_ => RefreshCaptionState()).DisposeWith(_captionDisposables);
        MaximumLineCount.Subscribe(_ => RefreshCaptionState()).DisposeWith(_captionDisposables);
        SelectedCaptionTemplate.Subscribe(_ => RefreshTemplatePreview()).DisposeWith(_captionDisposables);
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
            editViewModel.Scene.Edited += OnCaptionSceneEdited;
            Disposable.Create(() => editViewModel.Scene.Edited -= OnCaptionSceneEdited)
                .DisposeWith(_captionDisposables);
        }
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
    }

    private async Task TranscribeSelectedSourceAsync(AudioSourceItem source)
    {
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

        await TranscribeSourceFileAsync(
            source,
            filePath,
            language,
            captionRevision,
            draftScopeRevision);
    }

    private async Task TranscribeSourceFileAsync(
        AudioSourceItem source,
        string filePath,
        string? language,
        long captionRevision,
        long draftScopeRevision)
    {
        SourceAudioFingerprint fingerprint = GetSourceAudioFingerprint(filePath);
        // Opening and decoding are the editor's own work, not the server's, and
        // both run for as long as the audio is: off the UI thread, or the window
        // stops answering for the length of the file.
        using MediaReader reader = await Task.Run(
            () => MediaReader.Open(
                filePath,
                new MediaOptions(MediaMode.Audio) { PreferProxy = false }),
            RequestToken);
        if (!reader.HasAudio)
            throw new SubtitleInputException(Strings.AiSubtitle_NoAudioInRange);

        int sampleRate = reader.AudioInfo.SampleRate;
        if (sampleRate <= 0)
            throw new SubtitleInputException(Strings.AiSubtitle_NoAudioInRange);

        long totalSamples = GetSourceSampleCount(reader.AudioInfo, source.Duration);
        int chunkSamples = checked((int)Math.Ceiling(
            SceneMixChunkDuration.TotalSeconds * sampleRate));
        if (totalSamples <= 0 || chunkSamples <= 0)
            throw new SubtitleInputException(Strings.AiSubtitle_NoAudioInRange);

        int chunkCount = checked((int)Math.Ceiling(totalSamples / (double)chunkSamples));
        bool canResume = CanResumeSourceTranscription(
            source,
            filePath,
            language,
            sampleRate,
            totalSamples,
            chunkSamples,
            chunkCount,
            fingerprint,
            draftScopeRevision);
        if (!canResume)
            ChangeCaptionDraftJob(null, deleteCurrent: false);

        SourceTranscriptionOperation operation = canResume
            ? _pendingSourceTranscription!
            : new SourceTranscriptionOperation(
                source,
                filePath,
                source.ElementId,
                fingerprint.FileLength,
                fingerprint.LastWriteTimeUtcTicks,
                language,
                sampleRate,
                totalSamples,
                chunkSamples,
                chunkCount,
                captionRevision,
                draftScopeRevision);
        operation.Source = source;
        _pendingSourceTranscription = operation;
        _pendingSceneTranscription = null;
        UpdateOutstandingCaptionRequest();
        // Read once for the whole run, and taken from the run itself once it has
        // named anything. Every piece is named partly by the model, so a picker
        // that moved between naming a piece and sending it would put one model
        // in the name and another in the body — which the server refuses — and
        // one that moved between pieces, or fell back because the run's model
        // was withdrawn, would rename the rest of the run and buy it again.
        AiModelId? runModel = ModelOfRun(
            operation.RequestKey.HasOutstandingName.Value,
            operation.RequestKeyModel,
            TranscriptionModelPicker.SelectedModel);

        for (int chunkIndex = operation.CompletedChunkCount;
            chunkIndex < operation.ChunkCount;
            chunkIndex++)
        {
            RequestToken.ThrowIfCancellationRequested();
            long chunkOffset = checked((long)chunkIndex * operation.ChunkSamples);
            int requestedSamples = checked((int)Math.Min(
                operation.ChunkSamples,
                operation.TotalSamples - chunkOffset));
            (string path, FileStream stream) = AiTemporaryFileStore.Create(
                "audio",
                "source",
                ".wav");
            try
            {
                SpeechWaveChunkResult chunk;
                using (stream)
                {
                    chunk = await Task.Run(
                        () => WriteSpeechWave(
                            reader,
                            checked((int)chunkOffset),
                            requestedSamples,
                            stream,
                            RequestToken),
                        RequestToken);
                }
                if (chunk.SourceSampleCount <= 0)
                    throw new SubtitleInputException(Strings.AiSubtitle_NoAudioInRange);

                if (chunk.SourceSampleCount < requestedSamples)
                {
                    operation.TotalSamples = chunkOffset + chunk.SourceSampleCount;
                    operation.ChunkCount = chunkIndex + 1;
                }

                AiRequestName name = operation.RequestNameFor(chunkIndex, runModel);
                UpdateOutstandingCaptionRequest();
                AiTranscriptionResponse response;
                try
                {
                    // Not for a repeat: the server looks up the job this name
                    // already made before it looks at the balance, so refusing
                    // here would refuse to collect a piece already paid for.
                    if (!name.IsRepeat)
                    {
                        await EnsureAvailableAsync(
                            new AiOperationAvailabilityRequest.Transcription(
                                chunk.Duration.TotalSeconds,
                                runModel));
                    }

                    // Written before the chunk goes out, not after it comes
                    // back. The first chunk is the one most likely to be charged
                    // and lost, and without this the name that charged it would
                    // die with the session: the next run would name the same
                    // chunk differently and buy it again.
                    if (!PublishSourceTranscriptionPartial(operation))
                        return;

                    response = await _aiService.TranscribeAsync(
                        new AiTranscriptionRequest(
                            AiUploadSource.FromFile(
                                path,
                                FormatChunkFileName("source", chunkIndex)),
                            language,
                            runModel,
                            name.Key),
                        RequestToken);
                }
                catch (AiProviderErrorException)
                {
                    // The server settled this chunk as failed and refunded it.
                    // Its key would keep answering with that failure, so the
                    // rest of the run takes new ones — and the resume state is
                    // rewritten with them, or a resumed run would ask under the
                    // spent key again.
                    operation.RequestKey.Retire();
                    UpdateOutstandingCaptionRequest();
                    PublishSourceTranscriptionPartial(operation);
                    throw;
                }
                catch (Exception ex) when (ReservedNothing(ex))
                {
                    operation.RequestKey.Withdraw(name);
                    UpdateOutstandingCaptionRequest();
                    throw;
                }
                operation.DetectedLanguage ??= response.Language;
                double offsetSeconds = chunkOffset / (double)operation.SampleRate;
                foreach (AiTranscriptionSegment segment in response.Segments)
                {
                    operation.SourceSegments.Add(new AiTranscriptionSegment
                    {
                        Start = offsetSeconds + segment.Start,
                        End = offsetSeconds + segment.End,
                        Text = segment.Text,
                    });
                }
                RecordCaptionDraftJob(response.JobId, operation.ExpectedDraftScopeRevision);
                operation.CompletedChunkCount++;
                if (!PublishSourceTranscriptionPartial(operation))
                    return;
            }
            finally
            {
                DeleteTemporaryAudio(path);
            }
        }

        string? resultLanguage = operation.DetectedLanguage ?? language;
        AiTranscriptionSegment[] mappedSegments = source.MapSegmentsToScene(
            operation.SourceSegments);
        if (!ReferenceEquals(SelectedAudioSource.Value, source)
            || operation.ExpectedCaptionRevision != Interlocked.Read(ref _captionDocumentRevision)
            || !IsCurrentCaptionDraftScope(operation.ExpectedDraftScopeRevision)
            || !string.Equals(
                SelectedSourceLanguage.Value.Code,
                operation.Language,
                StringComparison.Ordinal))
        {
            return;
        }

        _lastCaptionLanguage = resultLanguage;
        DetectedLanguageText.Value = CreateDetectedLanguageText(_lastCaptionLanguage);
        ResultSegments.Value = mappedSegments;
        _pendingSourceTranscription = null;
        ClearPartialResult();
    }

    private async Task TranscribeSceneMixAsync(
        AudioSourceItem source,
        string? language,
        long draftScopeRevision)
    {
        if (_editViewModel is null
            || !TryGetSceneRange(out TimeSpan rangeStart, out TimeSpan duration))
        {
            throw new SubtitleInputException(Strings.AiSubtitle_InvalidRange);
        }

        int chunkCount = (int)Math.Ceiling(duration.TotalSeconds / SceneMixChunkDuration.TotalSeconds);
        bool canResume = CanResumeSceneTranscription(
            source,
            language,
            rangeStart,
            duration,
            chunkCount);
        if (!canResume)
            ChangeCaptionDraftJob(null, deleteCurrent: false);

        SceneTranscriptionOperation operation = canResume
            ? _pendingSceneTranscription!
            : new SceneTranscriptionOperation(
                source,
                language,
                rangeStart,
                duration,
                SceneMixChunkDuration,
                chunkCount,
                Interlocked.Read(ref _captionDocumentRevision),
                draftScopeRevision,
                Interlocked.Read(ref _sceneAudioRevision),
                _editViewModel.Scene.Id);
        operation.Source = source;
        _pendingSceneTranscription = operation;
        _pendingSourceTranscription = null;
        UpdateOutstandingCaptionRequest();
        // Read once for the whole run, and taken from the run itself once it has
        // named anything. Every piece is named partly by the model, so a picker
        // that moved between naming a piece and sending it would put one model
        // in the name and another in the body — which the server refuses — and
        // one that moved between pieces, or fell back because the run's model
        // was withdrawn, would rename the rest of the run and buy it again.
        AiModelId? runModel = ModelOfRun(
            operation.RequestKey.HasOutstandingName.Value,
            operation.RequestKeyModel,
            TranscriptionModelPicker.SelectedModel);

        for (int index = operation.CompletedChunkCount; index < chunkCount; index++)
        {
            RequestToken.ThrowIfCancellationRequested();
            TimeSpan chunkOffset = TimeSpan.FromTicks(
                Math.Min(duration.Ticks, index * operation.ChunkDuration.Ticks));
            TimeSpan chunkDuration = TimeSpan.FromTicks(
                Math.Min(operation.ChunkDuration.Ticks, duration.Ticks - chunkOffset.Ticks));
            AiRequestName name = operation.RequestNameFor(index, runModel);
            UpdateOutstandingCaptionRequest();
            // Before the scene is composed: mixing a chunk of audio the account
            // cannot pay for is work thrown away.
            try
            {
                // Not for a repeat: the server looks up the job this name
                // already made before it looks at the balance, so refusing here
                // would refuse to collect a piece already paid for.
                if (!name.IsRepeat)
                {
                    await EnsureAvailableAsync(
                        new AiOperationAvailabilityRequest.Transcription(
                            chunkDuration.TotalSeconds,
                            runModel));
                }
            }
            catch (Exception ex) when (ReservedNothing(ex))
            {
                operation.RequestKey.Withdraw(name);
                UpdateOutstandingCaptionRequest();
                throw;
            }

            (string path, FileStream stream) = AiTemporaryFileStore.Create(
                "audio",
                "scene-mix",
                ".wav");
            try
            {
                using (stream)
                {
                    await WriteSceneMixWaveAsync(
                        stream,
                        rangeStart + chunkOffset,
                        chunkDuration,
                        RequestToken);
                }
                AiTranscriptionResponse response;
                try
                {
                    response = await _aiService.TranscribeAsync(
                        new AiTranscriptionRequest(
                            AiUploadSource.FromFile(
                                path,
                                FormatChunkFileName("scene-mix", index)),
                            language,
                            runModel,
                            name.Key),
                        RequestToken);
                }
                catch (AiProviderErrorException)
                {
                    operation.RequestKey.Retire();
                    UpdateOutstandingCaptionRequest();
                    throw;
                }
                catch (Exception ex) when (ReservedNothing(ex))
                {
                    operation.RequestKey.Withdraw(name);
                    UpdateOutstandingCaptionRequest();
                    throw;
                }
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
                DeleteTemporaryAudio(path);
            }
        }

        if (!ReferenceEquals(SelectedAudioSource.Value, source)
            || !TryGetSceneRange(out TimeSpan currentRangeStart, out TimeSpan currentDuration)
            || currentRangeStart != operation.RangeStart
            || currentDuration != operation.Duration
            || !string.Equals(
                SelectedSourceLanguage.Value.Code,
                operation.Language,
                StringComparison.Ordinal)
            || operation.ExpectedCaptionRevision != Interlocked.Read(ref _captionDocumentRevision)
            || operation.ExpectedSceneAudioRevision != Interlocked.Read(ref _sceneAudioRevision)
            || !IsCurrentCaptionDraftScope(operation.ExpectedDraftScopeRevision))
        {
            return;
        }

        _lastCaptionLanguage = operation.DetectedLanguage ?? language;
        DetectedLanguageText.Value = CreateDetectedLanguageText(_lastCaptionLanguage);
        ResultSegments.Value = CloneSegments(operation.Segments);
        ClearPartialResult();
    }

    private async Task WriteSceneMixWaveAsync(
        Stream stream,
        TimeSpan start,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var writer = new SpeechWaveWriter(stream);
        for (TimeSpan offset = TimeSpan.Zero; offset < duration; offset += SceneMixComposeSlice)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TimeSpan sliceDuration = TimeSpan.FromTicks(
                Math.Min(SceneMixComposeSlice.Ticks, (duration - offset).Ticks));
            AudioFrameSnapshot? snapshot = SceneMixAudioComposer is { } composer
                ? await composer(start + offset, sliceDuration, cancellationToken)
                : await ((IPreviewPlayer)_editViewModel!.Player)
                    .ComposeAudioAsync(start + offset, sliceDuration, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (snapshot is null || snapshot.SampleCount == 0)
                throw new SubtitleInputException(Strings.AiSubtitle_NoAudioInRange);

            writer.Append(snapshot, cancellationToken);
        }

        writer.Complete();
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
        TranslationPreview.Value = null;
        TranslatedLineCount.Value = 0;
        using CancellationTokenSource requestCts =
            CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        _requestCts = requestCts;
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
            UpdateOutstandingCaptionRequest();
            // Read once for the whole run, and taken from the run itself once
            // it has named anything — see ModelOfRun.
            AiModelId? runModel = ModelOfRun(
                operation.RequestKey.HasOutstandingName.Value,
                operation.RequestKeyModel,
                TranslationModelPicker.SelectedModel);
            if (operation.Batches.Count == 0)
            {
                _pendingTranslation = null;
                return;
            }

            for (int index = operation.CompletedBatchCount; index < operation.Batches.Count; index++)
            {
                TranslationBatch batch = operation.Batches[index];
                AiRequestName name = operation.RequestNameFor(index, runModel);
                UpdateOutstandingCaptionRequest();
                AiCaptionTranslationResponse response;
                try
                {
                    if (!name.IsRepeat)
                    {
                        await EnsureAvailableAsync(
                            CreateTranslationAvailabilityRequest(batch, runModel));
                    }

                    // Written before the batch goes out, not after it comes
                    // back. The first batch is the one most likely to be charged
                    // and lost, and without this the name that charged it would
                    // die with the session: the next run would name the same
                    // batch differently and buy it again.
                    if (!PublishTranslationPartial(operation))
                        return;

                    response = await _aiService.TranslateAsync(
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
                            operation.SourceLanguage,
                            model: runModel,
                            idempotencyKey: name.Key),
                        new Progress<AiCaptionTranslationSegment>(ShowTranslatedLine),
                        RequestToken);
                }
                catch (AiProviderErrorException)
                {
                    // The server settled this batch as failed and refunded it.
                    // Its key would keep answering with that failure, so what is
                    // left of the run goes out under new ones — and the resume
                    // state is rewritten with them, or a run resumed after a
                    // restart would ask under the spent key again.
                    operation.RequestKey.Retire();
                    UpdateOutstandingCaptionRequest();
                    PublishTranslationPartial(operation);
                    throw;
                }
                catch (Exception ex) when (ReservedNothing(ex))
                {
                    operation.RequestKey.Withdraw(name);
                    UpdateOutstandingCaptionRequest();
                    throw;
                }
                AddTranslatedBatch(operation, batch, response);
                RecordCaptionDraftJob(response.JobId, operation.ExpectedDraftScopeRevision);
                operation.CompletedBatchCount++;
                if (!PublishTranslationPartial(operation))
                    return;
                RefreshTranslationEstimate();
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
        // Reachable because a batch keeps its name across attempts. None of
        // these is a settlement, so the run keeps its names and can be resumed:
        // saying "an unexpected error" instead sent the user back to a button
        // that looked like it would start over.
        catch (AiResultUnavailableException)
        {
            SetCaptionErrorIfCurrent(draftScopeRevision, Strings.AiResultUnavailable);
        }
        catch (AiRequestInProgressException)
        {
            SetCaptionErrorIfCurrent(draftScopeRevision, Strings.AiRequestInProgress);
        }
        catch (AiRequestWasDeletedException)
        {
            // The job those names made is gone, so the rest of the run needs
            // new ones; the partial that has been paid for is still good.
            _pendingTranslation?.RequestKey.Retire();
            UpdateOutstandingCaptionRequest();
            if (_pendingTranslation is { } deleted)
                PublishTranslationPartial(deleted);
            SetCaptionErrorIfCurrent(draftScopeRevision, Strings.AiRequestWasDeleted);
        }
        catch (AiModelDoesNotSupportRequestException)
        {
            SetCaptionErrorIfCurrent(
                draftScopeRevision,
                Strings.AiModelDoesNotSupportRequest);
        }
        catch (AiModelUnavailableException)
        {
            SetCaptionErrorIfCurrent(draftScopeRevision, Strings.AiModelUnavailable);
        }
        catch (OperationCanceledException) when (IsRequestCanceled)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to translate subtitles.");
            SetCaptionErrorIfCurrent(draftScopeRevision, Strings.AiUnexpectedError);
        }
        finally
        {
            _requestCts = null;
            if (!_disposed && IsCurrentCaptionDraftScope(draftScopeRevision))
            {
                IsTranslating.Value = false;
                TranslationPreview.Value = null;
            }
        }
    }

    // A run is worth picking up as soon as it has named a piece, not only once a
    // piece has come back. The first piece is the one most likely to have been
    // charged and lost: starting a new run instead would name it differently and
    // buy it again.
    private bool CanResumeTranslation(
        long captionRevision,
        string targetLanguage,
        string? selectedSourceLanguage)
        => _pendingTranslation is { } operation
            && (operation.CompletedBatchCount > 0
                || operation.RequestKey.HasOutstandingName.Value)
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
        PartialResultMessage.Value = operation.CompletedBatchCount <= 0
            ? null
            : string.Format(
                operation.CompletedBatchCount == operation.Batches.Count
                    ? Strings.AiSubtitle_CompletedResultAvailable
                    : Strings.AiSubtitle_PartialTranslationAvailable,
                operation.CompletedBatchCount,
                operation.Batches.Count);
        RefreshTranslationEstimate();
        return true;
    }

    // A run is worth picking up as soon as it has named a piece, not only once a
    // piece has come back. The first piece is the one most likely to have been
    // charged and lost: starting a new run instead would name it differently and
    // buy it again.
    private bool CanResumeSceneTranscription(
        AudioSourceItem source,
        string? language,
        TimeSpan rangeStart,
        TimeSpan duration,
        int chunkCount)
        => _pendingSceneTranscription is { } operation
            && (operation.CompletedChunkCount > 0
                || operation.RequestKey.HasOutstandingName.Value)
            && operation.CompletedChunkCount < operation.ChunkCount
            && (operation.Source is null && source.IsSceneMix
                || ReferenceEquals(operation.Source, source))
            && operation.Language == language
            && operation.RangeStart == rangeStart
            && operation.Duration == duration
            && operation.ChunkDuration == SceneMixChunkDuration
            && operation.ChunkCount == chunkCount
            && IsCurrentCaptionDraftScope(operation.ExpectedDraftScopeRevision)
            && operation.ExpectedSceneAudioRevision == Interlocked.Read(ref _sceneAudioRevision)
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
        PartialResultMessage.Value = operation.CompletedChunkCount <= 0
            ? null
            : string.Format(
                operation.CompletedChunkCount == operation.ChunkCount
                    ? Strings.AiSubtitle_CompletedResultAvailable
                    : Strings.AiSubtitle_PartialTranscriptionAvailable,
                operation.CompletedChunkCount,
                operation.ChunkCount);
        _transcriptionEstimateRevision.Value++;
        return true;
    }

    private bool CanResumeSourceTranscription(
        AudioSourceItem source,
        string filePath,
        string? language,
        int sampleRate,
        long totalSamples,
        int chunkSamples,
        int chunkCount,
        SourceAudioFingerprint fingerprint,
        long draftScopeRevision)
        => _pendingSourceTranscription is { } operation
            && (operation.CompletedChunkCount > 0
                || operation.RequestKey.HasOutstandingName.Value)
            && operation.CompletedChunkCount < operation.ChunkCount
            && (operation.Source is null || AudioSourceItem.CanResume(source, operation.Source))
            && AudioSourceItem.FilePathsEqual(operation.FilePath, filePath)
            && operation.ElementId == source.ElementId
            && operation.Language == language
            && operation.SampleRate == sampleRate
            && operation.TotalSamples == totalSamples
            && operation.ChunkSamples == chunkSamples
            && operation.ChunkCount == chunkCount
            && operation.FileLength == fingerprint.FileLength
            && operation.LastWriteTimeUtcTicks == fingerprint.LastWriteTimeUtcTicks
            && operation.ExpectedDraftScopeRevision == draftScopeRevision
            && IsCurrentCaptionDraftScope(draftScopeRevision);

    private bool PublishSourceTranscriptionPartial(SourceTranscriptionOperation operation)
    {
        if (!IsCurrentCaptionDraftScope(operation.ExpectedDraftScopeRevision))
            return false;

        _pendingSourceTranscription = operation;
        if (operation.Source is not { } source)
            return false;

        string? detectedLanguage = operation.DetectedLanguage ?? operation.Language;
        AiTranscriptionSegment[] mappedSegments = source.MapSegmentsToScene(
            operation.SourceSegments);
        if (!SetPartialResult(new RecoverableCaptionResult(
                CreateCaptionDocument(mappedSegments, detectedLanguage),
                detectedLanguage,
                mappedSegments,
                PartialResultKind.Transcription,
                operation.CompletedChunkCount,
                operation.ChunkCount,
                operation.ExpectedDraftScopeRevision)))
        {
            return false;
        }
        PartialResultMessage.Value = operation.CompletedChunkCount <= 0
            ? null
            : string.Format(
                operation.CompletedChunkCount == operation.ChunkCount
                    ? Strings.AiSubtitle_CompletedResultAvailable
                    : Strings.AiSubtitle_PartialTranscriptionAvailable,
                operation.CompletedChunkCount,
                operation.ChunkCount);
        _transcriptionEstimateRevision.Value++;
        return true;
    }

    private static long GetSourceSampleCount(AudioStreamInfo audioInfo, TimeSpan fallbackDuration)
    {
        if (audioInfo.SampleRate <= 0)
            throw new SubtitleInputException(Strings.AiSubtitle_NoAudioInRange);

        double samples = audioInfo.NumSamples.ToDouble();
        if (!double.IsFinite(samples) || samples <= 0)
            samples = fallbackDuration.TotalSeconds * audioInfo.SampleRate;
        if (!double.IsFinite(samples) || samples <= 0 || samples > int.MaxValue)
            throw new SubtitleInputException("The source audio duration is unsupported.");
        return Math.Max(1, checked((long)Math.Ceiling(samples)));
    }

    private static SourceAudioFingerprint GetSourceAudioFingerprint(string filePath)
    {
        var info = new FileInfo(filePath);
        if (!info.Exists || info.Length <= 0)
            throw new SubtitleInputException(Strings.AiSubtitle_NoAudioInRange);

        return new SourceAudioFingerprint(info.Length, info.LastWriteTimeUtc.Ticks);
    }

    internal static bool HasMatchingSourceAudioFingerprint(
        string filePath,
        SourceAudioFingerprint expected)
        => GetSourceAudioFingerprint(filePath) == expected;

    private void DeleteTemporaryAudio(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to remove temporary audio {Path}.", path);
        }
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
        // A run that has only named its first piece has nothing to apply yet.
        // It is still written down — the name is what makes that piece
        // collectable — but the button that imports a partial stays closed.
        HasPartialResult.Value = result.CompletedSteps > 0;
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
                translation.CompletedBatchCount,
                translation.RequestKeySeed,
                translation.RequestKeyModel);
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
                scene.Language,
                scene.RangeStart,
                scene.Duration,
                scene.ChunkDuration,
                scene.ChunkCount,
                CloneSegments(scene.Segments),
                scene.DetectedLanguage,
                scene.CompletedChunkCount);
        }

        CaptionSourceTranscriptionResume? sourceResume = null;
        if (result.Kind == PartialResultKind.Transcription
            && result.Segments is { } sourceResultSegments
            && _pendingSourceTranscription is { } sourceTranscription
            && sourceTranscription.CompletedChunkCount == result.CompletedSteps
            && sourceTranscription.CompletedChunkCount < sourceTranscription.ChunkCount
            && sourceTranscription.ChunkCount == result.TotalSteps
            && (sourceTranscription.Source is null
                || SegmentsEqual(
                    sourceTranscription.Source.MapSegmentsToScene(
                        sourceTranscription.SourceSegments),
                    sourceResultSegments)))
        {
            sourceResume = new CaptionSourceTranscriptionResume(
                sourceTranscription.FilePath,
                sourceTranscription.ElementId,
                sourceTranscription.FileLength,
                sourceTranscription.LastWriteTimeUtcTicks,
                sourceTranscription.Language,
                sourceTranscription.SampleRate,
                sourceTranscription.TotalSamples,
                sourceTranscription.ChunkSamples,
                sourceTranscription.ChunkCount,
                CloneSegments(sourceTranscription.SourceSegments),
                sourceTranscription.DetectedLanguage,
                sourceTranscription.CompletedChunkCount,
                sourceTranscription.RequestKeySeed,
                sourceTranscription.RequestKeyModel);
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
            sceneResume,
            sourceResume);
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
                    if (translation.CompletedBatchCount < 0
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
                        batches,
                        string.IsNullOrEmpty(translation.RequestKeySeed)
                            ? null
                            : translation.RequestKeySeed)
                    {
                        CompletedBatchCount = translation.CompletedBatchCount,
                        RequestKeyModel = translation.RequestKeyModel,
                    };
                    // The unfinished batches are named partly by the model the
                    // run used. Landing on another one would name them
                    // differently and buy them again.
                    _restoredTranslationModel =
                        string.IsNullOrEmpty(translation.RequestKeyModel)
                            ? null
                            : new AiModelId(translation.RequestKeyModel);
                    PreferRestoredModel(
                        TranslationModelPicker,
                        _restoredTranslationModel);
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
                // The paid partial remains applicable, but scene audio has no durable
                // content fingerprint. Never append newly composed chunks after a restart.
                CaptionLanguageOption? sourceOption = SourceLanguages.FirstOrDefault(option =>
                    string.Equals(option.Code, scene.Language, StringComparison.Ordinal));
                if (sourceOption is not null)
                    SelectedSourceLanguage.Value = sourceOption;
            }

            if (draft.SourceTranscriptionResume is { } source)
            {
                _pendingSourceTranscription = new SourceTranscriptionOperation(
                    null,
                    source.FilePath,
                    source.ElementId,
                    source.FileLength,
                    source.LastWriteTimeUtcTicks,
                    source.Language,
                    source.SampleRate,
                    source.TotalSamples,
                    source.ChunkSamples,
                    source.ChunkCount,
                    Interlocked.Read(ref _captionDocumentRevision),
                    draftScopeRevision,
                    string.IsNullOrEmpty(source.RequestKeySeed) ? null : source.RequestKeySeed)
                {
                    CompletedChunkCount = source.CompletedChunkCount,
                    DetectedLanguage = source.DetectedLanguage,
                    RequestKeyModel = source.RequestKeyModel,
                };
                _restoredTranscriptionModel =
                    string.IsNullOrEmpty(source.RequestKeyModel)
                        ? null
                        : new AiModelId(source.RequestKeyModel);
                PreferRestoredModel(
                    TranscriptionModelPicker,
                    _restoredTranscriptionModel);
                _pendingSourceTranscription.SourceSegments.AddRange(CloneSegments(source.Segments));
                CaptionLanguageOption? sourceOption = SourceLanguages.FirstOrDefault(option =>
                    string.Equals(option.Code, source.Language, StringComparison.Ordinal));
                if (sourceOption is not null)
                    SelectedSourceLanguage.Value = sourceOption;
            }

            // A draft written the moment its first piece was named holds the
            // way back to that piece and nothing to apply yet.
            HasPartialResult.Value = draft.CompletedSteps > 0;
            PartialResultMessage.Value = draft.CompletedSteps <= 0
                ? null
                : draft.CompletedSteps == draft.TotalSteps
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
        else if (result.Kind == PartialResultKind.Transcription
            && _pendingSourceTranscription is { } sourceTranscription)
        {
            sourceTranscription.ExpectedCaptionRevision = Interlocked.Read(
                ref _captionDocumentRevision);
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
        _pendingSourceTranscription = null;
        // The preference belonged to the run that has just gone; leaving it
        // would move the picker back to that model on the next activation.
        ClearRestoredModelPreference();
        UpdateOutstandingCaptionRequest();
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

    private void RetireDeletedTranscriptionNames()
    {
        if (_pendingSourceTranscription is { } source)
        {
            source.RequestKey.Retire();
            PublishSourceTranscriptionPartial(source);
        }

        if (_pendingSceneTranscription is { } scene)
        {
            scene.RequestKey.Retire();
            PublishSceneTranscriptionPartial(scene);
        }

        UpdateOutstandingCaptionRequest();
    }

    // Whether a caption run has named a piece the server has not been seen to
    // settle. While it has, asking again may be answered by a job already paid
    // for, so the button stays live however little is left to spend.
    private void UpdateOutstandingCaptionRequest()
    {
        HasOutstandingTranscriptionRequest.Value =
            _pendingSourceTranscription?.RequestKey.HasOutstandingName.Value == true
            || _pendingSceneTranscription?.RequestKey.HasOutstandingName.Value == true;
        HasOutstandingTranslationRequest.Value =
            _pendingTranslation?.RequestKey.HasOutstandingName.Value == true;
    }

    // Refusals the server decides after looking the name up and finding nothing
    // under it — the sign-in, the plan, the balance, the size of the upload, and
    // whether the model can serve the request at all. No job was made, so the
    // name is not the way back to one, and leaving it outstanding would pin the
    // rest of the run to the model that named it.
    private static bool ReservedNothing(Exception exception)
        => exception is AuthenticationRequiredException
            or AiPlanRequiredException
            or AiUsageLimitExceededException
            or AiModelDoesNotSupportRequestException
            or AiFileTooLargeException;

    private void ClearRestoredModelPreference()
    {
        _restoredTranscriptionModel = null;
        _restoredTranslationModel = null;
    }

    // A draft can be restored before the pickers have loaded or after, so the
    // model a run was named for is both remembered for a load still to come and
    // applied at once to one that has already happened.
    private static void PreferRestoredModel(
        AiModelPickerViewModel picker,
        AiModelId? model)
    {
        if (model is not { } wanted)
            return;

        AiModelPickerOption? option =
            picker.Options.FirstOrDefault(candidate => candidate.Id == wanted);
        if (option is not null)
            picker.Selected.Value = option;
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
        _pendingSourceTranscription = null;
        _pendingHistoryResult = null;
        _lastCaptionLanguage = null;
        ClearRestoredModelPreference();
        UpdateOutstandingCaptionRequest();
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
        int textOffset = selected.CaretIndex;
        if (!TryGetCueSplitTime(cue, textOffset, out TimeSpan splitTime))
            return;

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
        if (e.PropertyName == nameof(EditableCaptionCueViewModel.CaretIndex))
        {
            RefreshCueCommandStates();
            return;
        }

        if (e.PropertyName != nameof(EditableCaptionCueViewModel.Number))
        {
            MarkCaptionDocumentChanged();
        }
        RefreshCueCommandStates();
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

    private void RefreshCueCommandStates()
    {
        EditableCaptionCueViewModel? selected = SelectedCue.Value;
        int index = selected is null ? -1 : _editableCues.IndexOf(selected);
        _canDeleteCue.Value = index >= 0;
        _canMergeCue.Value = index >= 0
            && index < _editableCues.Count - 1
            && TryBuildCaptionDocumentCore(out _, out _);
        _canSplitCue.Value = index >= 0
            && TryBuildCaptionDocumentCore(out CaptionDocument? document, out _)
            && document is not null
            && TryGetCueSplitTime(document[index], selected!.CaretIndex, out _);
    }

    private static bool TryGetCueSplitTime(
        CaptionCue cue,
        int textOffset,
        out TimeSpan splitTime)
    {
        splitTime = default;
        if (textOffset <= 0 || textOffset >= cue.Text.Length)
            return false;

        int[] boundaries = StringInfo.ParseCombiningCharacters(cue.Text);
        int boundaryIndex = Array.BinarySearch(boundaries, textOffset);
        long durationTicks = (cue.End - cue.Start).Ticks;
        if (boundaryIndex <= 0 || durationTicks <= 1)
            return false;

        long offsetTicks = (long)Math.Round(
            durationTicks * (boundaryIndex / (double)boundaries.Length),
            MidpointRounding.AwayFromZero);
        offsetTicks = Math.Clamp(offsetTicks, 1, durationTicks - 1);
        splitTime = cue.Start + TimeSpan.FromTicks(offsetTicks);
        return true;
    }

    private void RefreshTranslationEstimate()
    {
        _translationEstimateRevision.Value++;
    }

    private void RefreshTranslationAvailability()
    {
        AiOperationAvailabilityRequest? request = null;
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
            request = CreateTranslationAvailabilityRequest(
                operation.Batches[operation.CompletedBatchCount]);
        }
        else if (TryBuildCaptionDocumentCore(out CaptionDocument? document, out _)
                 && document is { Count: > 0 })
        {
            request = CreateTranslationBatches(document).FirstOrDefault() is { } batch
                ? CreateTranslationAvailabilityRequest(batch)
                : null;
        }

        _translationAvailability.Check(request);
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

    // The line the model has just translated, shown while the rest is still
    // being worked on. It is a preview: what ends up on the cues is the batch
    // the server returns, checked as it always was.
    private void ShowTranslatedLine(AiCaptionTranslationSegment segment)
    {
        if (_disposed || string.IsNullOrEmpty(segment.Text))
            return;

        TranslationPreview.Value = segment.Text;
        TranslatedLineCount.Value++;
    }

    // The name the chunk is uploaded under. Part of what the server fingerprints
    // the request by, so it has to be the same on every attempt at one chunk;
    // the file it is read from is named for uniqueness on disk instead.
    private static string FormatChunkFileName(string prefix, int chunkIndex)
        => $"{prefix}-chunk-{chunkIndex:D4}.wav";

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

    private AiOperationAvailabilityRequest? CreateTranscriptionAvailabilityRequest(
        AudioSourceItem? source)
    {
        if (source is null)
            return null;
        TimeSpan duration = source.Duration;
        TimeSpan rangeStart = TimeSpan.Zero;
        if (source.IsSceneMix
            && (!TryGetSceneRange(out rangeStart, out duration) || duration <= TimeSpan.Zero))
        {
            return null;
        }
        if (source.IsSceneMix)
        {
            int chunkCount = (int)Math.Ceiling(
                duration.TotalSeconds / SceneMixChunkDuration.TotalSeconds);
            int completed = CanResumeSceneTranscription(
                source,
                SelectedSourceLanguage.Value.Code,
                rangeStart,
                duration,
                chunkCount)
                ? _pendingSceneTranscription!.CompletedChunkCount
                : 0;
            TimeSpan offset = TimeSpan.FromTicks(Math.Min(
                duration.Ticks,
                completed * SceneMixChunkDuration.Ticks));
            duration = TimeSpan.FromTicks(Math.Min(
                SceneMixChunkDuration.Ticks,
                duration.Ticks - offset.Ticks));
        }
        else
        {
            duration = duration <= TimeSpan.Zero
                ? duration
                : TimeSpan.FromTicks(Math.Min(duration.Ticks, SceneMixChunkDuration.Ticks));
        }
        return duration > TimeSpan.Zero
            ? new AiOperationAvailabilityRequest.Transcription(
                duration.TotalSeconds,
                TranscriptionModelPicker.SelectedModel)
            : null;
    }

    private AiOperationAvailabilityRequest.Translation CreateTranslationAvailabilityRequest(
        TranslationBatch batch,
        AiModelId? model = null)
        // What the batch will actually be sent to. A run in progress is priced
        // against the model its names were built from, not against whichever the
        // picker is showing now.
        => new(
            batch.Pieces.Sum(piece => piece.Text.Length),
            model ?? TranslationModelPicker.SelectedModel);

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
            : string.IsNullOrEmpty(recorded)
                ? null
                : new AiModelId(recorded);

    private async Task EnsureAvailableAsync(AiOperationAvailabilityRequest request)
    {
        AiOperationAvailabilityTracker tracker = request is AiOperationAvailabilityRequest.Translation
            ? _translationAvailability
            : _transcriptionAvailability;
        if (!await tracker.CheckNowAsync(request, RequestToken))
            throw new AiUsageLimitExceededException();
    }

    // The transcribed range is the scene's own: the tab has nothing to add to
    // what the timeline already says about where the work starts and ends.
    private bool TryGetSceneRange(out TimeSpan start, out TimeSpan duration)
    {
        start = _editViewModel?.Scene.Start ?? TimeSpan.Zero;
        duration = _editViewModel?.Scene.Duration ?? TimeSpan.Zero;
        return _editViewModel is not null && start >= TimeSpan.Zero && duration > TimeSpan.Zero;
    }

    private IObservable<TimeSpan> CreateSceneRangeObservable()
        => _editViewModel is { } editor
            ? editor.Scene.GetObservable(Scene.StartProperty)
                .CombineLatest(
                    editor.Scene.GetObservable(Scene.DurationProperty),
                    (start, duration) => start < TimeSpan.Zero ? TimeSpan.Zero : duration)
            : Observable.Return(TimeSpan.Zero);

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

    internal static SpeechWaveChunkResult WriteSpeechWave(
        MediaReader reader,
        int startSample,
        int requestedSamples,
        Stream stream,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(stream);
        if (startSample < 0)
            throw new ArgumentOutOfRangeException(nameof(startSample));
        if (requestedSamples <= 0)
            throw new ArgumentOutOfRangeException(nameof(requestedSamples));
        cancellationToken.ThrowIfCancellationRequested();
        if (!stream.CanWrite || !stream.CanSeek)
        {
            throw new ArgumentException(
                "The destination stream must be writable and seekable.",
                nameof(stream));
        }

        int sourceRate = reader.AudioInfo.SampleRate;
        if (sourceRate <= 0)
            throw new SubtitleInputException(Strings.AiSubtitle_NoAudioInRange);

        const int outputRate = 16_000;
        const int waveHeaderLength = 44;
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(0);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(outputRate);
        writer.Write(outputRate * sizeof(short));
        writer.Write((short)sizeof(short));
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(0);

        int sourceSamplesWritten = 0;
        int outputSamplesWritten = 0;
        int decodeBlockSize = Math.Max(1, Math.Min(sourceRate, requestedSamples));
        double nextOutputSourcePosition = 0;
        while (sourceSamplesWritten < requestedSamples)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int length = Math.Min(decodeBlockSize, requestedSamples - sourceSamplesWritten);
            if (!reader.ReadAudio(
                    checked(startSample + sourceSamplesWritten),
                    length,
                    out Ref<IPcm>? audioRef))
            {
                throw new SubtitleInputException(Strings.AiSubtitle_NoAudioInRange);
            }

            using (audioRef)
            {
                if (audioRef.Value is not Pcm<Stereo32BitFloat> pcm)
                    throw new SubtitleInputException(Strings.AiSubtitle_NoAudioInRange);

                int decodedSamples = Math.Min(pcm.NumSamples, length);
                if (decodedSamples == 0)
                    break;

                Span<Stereo32BitFloat> source = pcm.DataSpan[..decodedSamples];
                double blockEnd = sourceSamplesWritten + decodedSamples;
                while (nextOutputSourcePosition < blockEnd)
                {
                    int sourceIndex = Math.Clamp(
                        (int)Math.Floor(nextOutputSourcePosition - sourceSamplesWritten),
                        0,
                        decodedSamples - 1);
                    Stereo32BitFloat sample = source[sourceIndex];
                    double mono = (sample.Left + sample.Right) / 2d;
                    mono = double.IsFinite(mono) ? Math.Clamp(mono, -1, 1) : 0;
                    writer.Write((short)Math.Round(mono * short.MaxValue));
                    outputSamplesWritten++;
                    nextOutputSourcePosition = outputSamplesWritten * (double)sourceRate / outputRate;
                }
                sourceSamplesWritten += decodedSamples;
                if (decodedSamples < length)
                    break;
            }
        }

        if (sourceSamplesWritten == 0 || outputSamplesWritten == 0)
            throw new SubtitleInputException(Strings.AiSubtitle_NoAudioInRange);

        int dataLength = checked(outputSamplesWritten * sizeof(short));
        stream.Position = 4;
        writer.Write(checked(waveHeaderLength - 8 + dataLength));
        stream.Position = 40;
        writer.Write(dataLength);
        stream.Position = waveHeaderLength + dataLength;
        return new SpeechWaveChunkResult(
            sourceSamplesWritten,
            outputSamplesWritten,
            TimeSpan.FromSeconds(sourceSamplesWritten / (double)sourceRate));
    }

    /// <summary>
    /// Writes one 16-bit mono PCM wave from as many composed slices as it is
    /// handed. The slices are separate calls because a whole upload chunk cannot
    /// be composed at once (see <see cref="SceneMixComposeSlice"/>), and the
    /// resampling position carries across them so a slice boundary neither drops
    /// a sample nor shifts the ones after it.
    /// </summary>
    internal sealed class SpeechWaveWriter(Stream stream)
    {
        private const int WaveHeaderLength = 44;
        private const int MaximumOutputRate = 16_000;
        private readonly Stream _stream = stream;
        private int _sourceRate;
        private int _outputRate;
        private long _sourceFramesWritten;
        private double _nextOutputSourcePosition;
        private int _outputSampleCount;

        public void Append(AudioFrameSnapshot snapshot, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            cancellationToken.ThrowIfCancellationRequested();
            if (snapshot.SampleRate <= 0 || snapshot.ChannelCount <= 0 || snapshot.SampleCount <= 0)
                throw new SubtitleInputException(Strings.AiSubtitle_NoAudioInRange);

            if (_sourceRate == 0)
            {
                _sourceRate = snapshot.SampleRate;
                _outputRate = Math.Min(_sourceRate, MaximumOutputRate);
                WriteHeader();
            }
            else if (snapshot.SampleRate != _sourceRate)
            {
                // Resampling assumes one rate for the whole wave; a slice at another
                // rate would land its samples at the wrong time.
                throw new SubtitleInputException(Strings.AiSubtitle_NoAudioInRange);
            }

            using var writer = new BinaryWriter(_stream, Encoding.ASCII, leaveOpen: true);
            long sliceStart = _sourceFramesWritten;
            long sliceEnd = sliceStart + snapshot.SampleCount;
            while (_nextOutputSourcePosition < sliceEnd)
            {
                if ((_outputSampleCount & 4095) == 0)
                    cancellationToken.ThrowIfCancellationRequested();

                int frame = (int)Math.Clamp(
                    (long)Math.Floor(_nextOutputSourcePosition) - sliceStart,
                    0,
                    snapshot.SampleCount - 1);
                double mono = 0;
                for (int channel = 0; channel < snapshot.ChannelCount; channel++)
                {
                    float sample = snapshot.Interleaved[(frame * snapshot.ChannelCount) + channel];
                    mono += float.IsFinite(sample) ? sample : 0;
                }

                mono = Math.Clamp(mono / snapshot.ChannelCount, -1, 1);
                writer.Write((short)Math.Round(mono * short.MaxValue));
                _outputSampleCount++;
                _nextOutputSourcePosition = _outputSampleCount * (double)_sourceRate / _outputRate;
            }

            _sourceFramesWritten = sliceEnd;
        }

        public void Complete()
        {
            if (_outputSampleCount == 0)
                throw new SubtitleInputException(Strings.AiSubtitle_NoAudioInRange);

            int dataLength = checked(_outputSampleCount * sizeof(short));
            using var writer = new BinaryWriter(_stream, Encoding.ASCII, leaveOpen: true);
            _stream.Position = 4;
            writer.Write(checked(WaveHeaderLength - 8 + dataLength));
            _stream.Position = 40;
            writer.Write(dataLength);
            _stream.Position = WaveHeaderLength + dataLength;
        }

        private void WriteHeader()
        {
            using var writer = new BinaryWriter(_stream, Encoding.ASCII, leaveOpen: true);
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(0);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(_outputRate);
            writer.Write(_outputRate * sizeof(short));
            writer.Write((short)sizeof(short));
            writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(0);
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
        IReadOnlyList<TranslationBatch> batches,
        string? requestKeySeed = null)
    {
        public CaptionDocument SourceDocument { get; } = sourceDocument;

        public long ExpectedCaptionRevision { get; set; } = expectedCaptionRevision;

        public string? SourceLanguage { get; } = sourceLanguage;

        public string? SelectedSourceLanguage { get; } = selectedSourceLanguage;

        public string TargetLanguage { get; } = targetLanguage;

        public long ExpectedDraftScopeRevision { get; } = expectedDraftScopeRevision;

        public IReadOnlyList<TranslationBatch> Batches { get; } = batches;

        /// <summary>
        /// Names this run's requests, one key per batch. Restored with the rest
        /// of the resume state so a batch resumed after a restart asks for the
        /// translation it already paid for instead of buying a second one.
        /// </summary>
        public AiRequestKey RequestKey { get; } = new(requestKeySeed);

        public string RequestKeySeed => RequestKey.Seed;


        /// <summary>The model the names handed out so far were built from.</summary>
        public string RequestKeyModel { get; set; } = string.Empty;

        public AiRequestName RequestNameFor(int batchIndex, AiModelId? model)
        {
            RequestKeyModel = model?.Value ?? string.Empty;
            return RequestKey.NameFor(batchIndex, model?.Value, TargetLanguage, SourceLanguage);
        }

        public Dictionary<string, string> TranslatedPieces { get; } = new(StringComparer.Ordinal);

        public int CompletedBatchCount { get; set; }
    }

    private sealed class SceneTranscriptionOperation(
        AudioSourceItem? source,
        string? language,
        TimeSpan rangeStart,
        TimeSpan duration,
        TimeSpan chunkDuration,
        int chunkCount,
        long expectedCaptionRevision,
        long expectedDraftScopeRevision,
        long expectedSceneAudioRevision,
        Guid sceneId,
        string? requestKeySeed = null)
    {
        public AudioSourceItem? Source { get; set; } = source;

        /// <summary>
        /// Names this run's requests, one key per chunk. Held with the rest of
        /// the resume state so a retried chunk asks for the transcription it
        /// already paid for instead of buying a second one.
        /// </summary>
        public AiRequestKey RequestKey { get; } = new(requestKeySeed);

        public string RequestKeySeed => RequestKey.Seed;


        /// <summary>The model the names handed out so far were built from.</summary>
        public string RequestKeyModel { get; set; } = string.Empty;

        // The model belongs in the key because the server refuses a key that
        // comes back with a different request behind it, and it fingerprints
        // the model. A chunk sent to another model is another request and takes
        // another key, which is also what it costs.
        public AiRequestName RequestNameFor(int chunkIndex, AiModelId? model)
        {
            RequestKeyModel = model?.Value ?? string.Empty;
            return RequestKey.NameFor(chunkIndex, model?.Value);
        }

        public string? Language { get; } = language;

        public TimeSpan RangeStart { get; } = rangeStart;

        public TimeSpan Duration { get; } = duration;

        public TimeSpan ChunkDuration { get; } = chunkDuration;

        public int ChunkCount { get; } = chunkCount;

        public long ExpectedCaptionRevision { get; set; } = expectedCaptionRevision;

        public long ExpectedDraftScopeRevision { get; } = expectedDraftScopeRevision;

        public long ExpectedSceneAudioRevision { get; } = expectedSceneAudioRevision;

        public Guid SceneId { get; } = sceneId;

        public List<AiTranscriptionSegment> Segments { get; } = [];

        public string? DetectedLanguage { get; set; }

        public int CompletedChunkCount { get; set; }
    }

    private sealed class SourceTranscriptionOperation(
        AudioSourceItem? source,
        string filePath,
        Guid elementId,
        long fileLength,
        long lastWriteTimeUtcTicks,
        string? language,
        int sampleRate,
        long totalSamples,
        int chunkSamples,
        int chunkCount,
        long expectedCaptionRevision,
        long expectedDraftScopeRevision,
        string? requestKeySeed = null)
    {
        public AudioSourceItem? Source { get; set; } = source;

        /// <summary>
        /// Names this run's requests, one key per chunk. Restored with the rest
        /// of the resume state so a chunk resumed after a restart asks for the
        /// transcription it already paid for instead of buying a second one.
        /// </summary>
        public AiRequestKey RequestKey { get; } = new(requestKeySeed);

        public string RequestKeySeed => RequestKey.Seed;


        /// <summary>The model the names handed out so far were built from.</summary>
        public string RequestKeyModel { get; set; } = string.Empty;

        public AiRequestName RequestNameFor(int chunkIndex, AiModelId? model)
        {
            RequestKeyModel = model?.Value ?? string.Empty;
            return RequestKey.NameFor(chunkIndex, model?.Value);
        }

        public string FilePath { get; } = filePath;

        public Guid ElementId { get; } = elementId;

        public long FileLength { get; } = fileLength;

        public long LastWriteTimeUtcTicks { get; } = lastWriteTimeUtcTicks;

        public string? Language { get; } = language;

        public int SampleRate { get; } = sampleRate;

        public long TotalSamples { get; set; } = totalSamples;

        public int ChunkSamples { get; } = chunkSamples;

        public int ChunkCount { get; set; } = chunkCount;

        public long ExpectedCaptionRevision { get; set; } = expectedCaptionRevision;

        public long ExpectedDraftScopeRevision { get; } = expectedDraftScopeRevision;

        public List<AiTranscriptionSegment> SourceSegments { get; } = [];

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

internal readonly record struct SpeechWaveChunkResult(
    int SourceSampleCount,
    int OutputSampleCount,
    TimeSpan Duration);

internal readonly record struct SourceAudioFingerprint(
    long FileLength,
    long LastWriteTimeUtcTicks);

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
