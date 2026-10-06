using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
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
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

using static Beutl.Services.AI.CaptionTranslationBatcher;
using static Beutl.Services.AI.SpeechWaveEncoder;

namespace Beutl.ViewModels.Dialogs;

public sealed partial class AiSubtitleDialogViewModel
{
    // Long enough to keep the number of paid requests down, short enough that a
    // chunk stays inside what one upload may carry: 16 kHz mono 16-bit PCM runs
    // at 32 kB a second, so the endpoint's 25 MiB stops a little under fourteen
    // minutes.
    private static readonly TimeSpan s_sceneMixChunkDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan s_sceneMixComposeSlice = TimeSpan.FromSeconds(5);

    private TimeSpan _sceneMixChunkDuration = s_sceneMixChunkDuration;

    private readonly Guid _sceneAudioSessionId = Guid.NewGuid();

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

    private async Task TranscribeSelectedSourceAsync(
        AudioSourceItem source,
        AsyncOperationLifetime.Operation operationLifetime)
    {
        long captionRevision = Interlocked.Read(ref _captionDocumentRevision);
        long draftScopeRevision = Interlocked.Read(ref _captionDraftScopeRevision);
        string? language = SelectedSourceLanguage.Value.Code;
        if (source.IsSceneMix)
        {
            await TranscribeSceneMixAsync(source, language, draftScopeRevision, operationLifetime);
            return;
        }

        if (source.FilePath is not { } filePath)
            throw new SubtitleInputException(Strings.AiSubtitle_NoAudioInRange);

        await TranscribeSourceFileAsync(
            source,
            filePath,
            language,
            captionRevision,
            draftScopeRevision,
            operationLifetime);
    }

    private async Task TranscribeSourceFileAsync(
        AudioSourceItem source,
        string filePath,
        string? language,
        long captionRevision,
        long draftScopeRevision,
        AsyncOperationLifetime.Operation operationLifetime)
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

        SourceChunkPlan plan = PlanSourceChunks(reader, source);
        SourceTranscriptionOperation operation = BeginSourceTranscription(
            source,
            filePath,
            language,
            plan,
            fingerprint,
            captionRevision,
            draftScopeRevision);
        // Read once for the whole run, and taken from the run itself once it has
        // named anything. Every piece is named partly by the model, so a picker
        // that moved between naming a piece and sending it would put one model
        // in the name and another in the body — which the server refuses — and
        // one that moved between pieces, or fell back because the run's model
        // was withdrawn, would rename the rest of the run and buy it again.
        AiModelId? runModel = ModelOfRun(
            operation.CompletedChunkCount > 0
                || operation.RequestKey.HasOutstandingName.Value,
            operation.RequestKeyModel,
            TranscriptionModelPicker.SelectedModel);

        for (int chunkIndex = operation.CompletedChunkCount;
            chunkIndex < operation.ChunkCount;
            chunkIndex++)
        {
            RequestToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(SelectedAudioSource.Value, source))
                return;
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
                            checked((int)(operation.SourceStartSamples + chunkOffset)),
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
                // Anything that ends here ends before the request went out, so
                // the name reached nothing — a refusal, a run stopped while the
                // check was in the air, any of it.
                try
                {
                    // Not for a repeat: the server looks up the job this name
                    // already made before it looks at the balance, so refusing
                    // here would refuse to collect a piece already paid for.
                    if (!name.IsRepeat)
                    {
                        await EnsureAvailableAsync(
                            new AiOperationAvailabilityRequest.Transcription(
                                AiOperations.Transcription,
                                chunk.UploadedDuration.TotalSeconds,
                                runModel));
                    }

                    // Written before the chunk goes out, not after it comes
                    // back. The first chunk is the one most likely to be charged
                    // and lost, and without this the name that charged it would
                    // die with the session: the next run would name the same
                    // chunk differently and buy it again. A run that cannot be
                    // written down is not started at all — sending it would be
                    // paying for something no later session could ask for.
                    if (!CanRunContinue(PublishSourceTranscriptionPartial(operation)))
                        return;
                }
                catch
                {
                    WithdrawSourceTranscriptionName(operation, name);
                    throw;
                }

                AiTranscriptionResponse response;
                try
                {
                    response = await _aiService.TranscribeAsync(
                        new AiTranscriptionRequest(
                            AiUploadSource.FromFile(
                                path,
                                FormatChunkFileName("source", chunkIndex)),
                            language,
                            runModel,
                            name.Key),
                        RequestToken);
                    response = NormalizeTranscriptionResponse(response, chunk.UploadedDuration.TotalSeconds, operation.ExpectedDraftScopeRevision);
                }
                catch (AiProviderErrorException)
                {
                    // Failed or invalid settled responses need a fresh key on retry.
                    // Its key would keep answering with that failure, so the
                    // rest of the run takes new ones — and the resume state is
                    // rewritten with them, or a resumed run would ask under the
                    // spent key again.
                    if (operation.RequestKey.Retire())
                    {
                        UpdateOutstandingCaptionRequest();
                        PublishSourceTranscriptionPartial(operation);
                    }
                    throw;
                }
                catch (Exception ex) when (AiRequestOutcome.CanWithdraw(name, ex))
                {
                    WithdrawSourceTranscriptionName(operation, name);
                    throw;
                }
                // Settle the durable key before publishing any local progress.
                // A failed CAS means another owner replaced the recovery row;
                // leave this operation untouched so it can still be resumed.
                if (!operation.RequestKey.Retire(name))
                    return;
                operation.DetectedLanguage ??= response.Language;
                double offsetSeconds = (plan.StartSamples + chunkOffset)
                    / (double)operation.SampleRate;
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
                // This chunk is settled; the rest of the run keeps its own
                // names, and so does anything else still outstanding.
                UpdateOutstandingCaptionRequest();
                // If this cannot be written down, the response may already have
                // been charged. Stop before the final ClearPartialResult can delete
                // the previous durable seed; the next attempt can persist this
                // in-memory progress and resume by idempotency key.
                if (!CanRunContinue(PublishSourceTranscriptionPartial(operation)))
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
        if (!IsSourceResultCurrent(operation, source))
            return;

        operationLifetime.TryPublish(() =>
        {
            _lastCaptionLanguage = resultLanguage;
            DetectedLanguageText.Value = CreateDetectedLanguageText(_lastCaptionLanguage);
            ResultSegments.Value = mappedSegments;
            _pendingSourceTranscription = null;
            ClearPartialResult();
        });
    }

    // How the selected stretch of a source file divides into the pieces sent one by one.
    private readonly record struct SourceChunkPlan(
        int SampleRate,
        long StartSamples,
        long TotalSamples,
        int ChunkSamples,
        int ChunkCount);

    private SourceChunkPlan PlanSourceChunks(MediaReader reader, AudioSourceItem source)
    {
        int sampleRate = reader.AudioInfo.SampleRate;
        if (sampleRate <= 0)
            throw new SubtitleInputException(Strings.AiSubtitle_NoAudioInRange);

        long sourceStartSamples = checked((long)Math.Max(
            0,
            Math.Floor(source.SourceOffset.TotalSeconds * sampleRate)));
        double selectedDurationSeconds = source.GetSourceElapsedSeconds();
        if (!double.IsFinite(selectedDurationSeconds) || selectedDurationSeconds <= 0)
            throw new SubtitleInputException(Strings.AiSubtitle_NoAudioInRange);
        long selectedSamples = GetSelectedSourceSampleCount(
            reader.AudioInfo,
            TimeSpan.FromSeconds(selectedDurationSeconds));
        double sourceSampleCount = reader.AudioInfo.NumSamples.ToDouble();
        long availableSamples = double.IsFinite(sourceSampleCount) && sourceSampleCount > 0
            ? checked((long)Math.Floor(sourceSampleCount)) - sourceStartSamples
            : selectedSamples;
        if (availableSamples <= 0)
            throw new SubtitleInputException(Strings.AiSubtitle_NoAudioInRange);
        long totalSamples = Math.Min(selectedSamples, availableSamples);
        int chunkSamples = checked((int)Math.Ceiling(
            SceneMixChunkDuration.TotalSeconds * sampleRate));
        if (totalSamples <= 0 || chunkSamples <= 0)
            throw new SubtitleInputException(Strings.AiSubtitle_NoAudioInRange);

        int chunkCount = checked((int)Math.Ceiling(totalSamples / (double)chunkSamples));
        return new SourceChunkPlan(sampleRate, sourceStartSamples, totalSamples, chunkSamples, chunkCount);
    }

    // Takes up the pending run when it still matches, else a retained one that does, else a new
    // one, and makes it the pending source run.
    private SourceTranscriptionOperation BeginSourceTranscription(
        AudioSourceItem source,
        string filePath,
        string? language,
        SourceChunkPlan plan,
        SourceAudioFingerprint fingerprint,
        long captionRevision,
        long draftScopeRevision)
    {
        bool canResume = CanResumeSourceTranscription(
            source,
            filePath,
            language,
            plan,
            fingerprint,
            draftScopeRevision);
        SourceTranscriptionOperation operation;
        if (canResume)
        {
            operation = _pendingSourceTranscription!;
            operation.ExpectedCaptionRevision = captionRevision;
        }
        else
        {
            CaptionDraftEntry? retained = _retainedCaptionRecoveries.FirstOrDefault(entry =>
                CanUseSourceTranscriptionDraft(
                    entry.Draft,
                    source,
                    filePath,
                    language,
                    plan,
                    fingerprint));
            if (!TryParkCurrentCaptionRecovery())
                throw new SubtitleInputException(Strings.AiSubtitle_RunCannotBeRecorded);

            if (retained is not null)
            {
                _retainedCaptionRecoveries.Remove(retained);
                operation = RestoreSourceTranscriptionOperation(
                    retained.Draft,
                    source,
                    captionRevision,
                    draftScopeRevision);
                _captionDraftJobId = retained.JobId;
            }
            else
            {
                ChangeCaptionDraftJob(null, deleteCurrent: false);
                operation = new SourceTranscriptionOperation(
                    source,
                    filePath,
                    source.ElementId,
                    fingerprint.FileLength,
                    fingerprint.LastWriteTimeUtcTicks,
                    language,
                    plan.SampleRate,
                    plan.StartSamples,
                    plan.TotalSamples,
                    plan.ChunkSamples,
                    plan.ChunkCount,
                    captionRevision,
                    draftScopeRevision);
            }
        }
        operation.Source = source;
        _pendingSourceTranscription = operation;
        _pendingSceneTranscription = null;
        UpdateOutstandingCaptionRequest();
        return operation;
    }

    // Whether a finished source run still answers what the dialog shows.
    private bool IsSourceResultCurrent(SourceTranscriptionOperation operation, AudioSourceItem source)
        => !_disposed
            && ReferenceEquals(SelectedAudioSource.Value, source)
            && operation.ExpectedCaptionRevision == Interlocked.Read(ref _captionDocumentRevision)
            && IsCurrentCaptionDraftScope(operation.ExpectedDraftScopeRevision)
            && string.Equals(
                SelectedSourceLanguage.Value.Code,
                operation.Language,
                StringComparison.Ordinal);

    private async Task TranscribeSceneMixAsync(
        AudioSourceItem source,
        string? language,
        long draftScopeRevision,
        AsyncOperationLifetime.Operation operationLifetime)
    {
        if (_disposed
            || _editViewModel is null
            || !TryGetSceneRange(out TimeSpan rangeStart, out TimeSpan duration))
        {
            throw new SubtitleInputException(Strings.AiSubtitle_InvalidRange);
        }

        int chunkCount = (int)Math.Ceiling(duration.TotalSeconds / SceneMixChunkDuration.TotalSeconds);
        SceneTranscriptionOperation operation = BeginSceneTranscription(
            _editViewModel,
            source,
            language,
            rangeStart,
            duration,
            chunkCount,
            draftScopeRevision);
        // Read once for the whole run, and taken from the run itself once it has
        // named anything. Every piece is named partly by the model, so a picker
        // that moved between naming a piece and sending it would put one model
        // in the name and another in the body — which the server refuses — and
        // one that moved between pieces, or fell back because the run's model
        // was withdrawn, would rename the rest of the run and buy it again.
        AiModelId? runModel = ModelOfRun(
            operation.CompletedChunkCount > 0
                || operation.RequestKey.HasOutstandingName.Value,
            operation.RequestKeyModel,
            TranscriptionModelPicker.SelectedModel);

        for (int index = operation.CompletedChunkCount; index < chunkCount; index++)
        {
            RequestToken.ThrowIfCancellationRequested();
            if (operation.ExpectedSceneAudioRevision != Interlocked.Read(ref _sceneAudioRevision)
                || !ReferenceEquals(SelectedAudioSource.Value, source)
                || !TryGetSceneRange(out TimeSpan liveStart, out TimeSpan liveDuration)
                || liveStart != operation.RangeStart || liveDuration != operation.Duration)
                return;
            TimeSpan chunkOffset = TimeSpan.FromTicks(
                Math.Min(duration.Ticks, index * operation.ChunkDuration.Ticks));
            TimeSpan chunkDuration = TimeSpan.FromTicks(
                Math.Min(operation.ChunkDuration.Ticks, duration.Ticks - chunkOffset.Ticks));
            AiRequestName name = operation.RequestNameFor(index, runModel);
            UpdateOutstandingCaptionRequest();
            // Before the scene is composed: mixing a chunk of audio the account
            // cannot pay for is work thrown away. Anything that ends here ends
            // before the request went out, so the name reached nothing — a
            // refusal, a run stopped while the check was in the air, any of it.
            try
            {
                // Not for a repeat: the server looks up the job this name
                // already made before it looks at the balance, so refusing here
                // would refuse to collect a piece already paid for.
                if (!name.IsRepeat)
                {
                    await EnsureAvailableAsync(
                        new AiOperationAvailabilityRequest.Transcription(
                            AiOperations.Transcription,
                            chunkDuration.TotalSeconds,
                            runModel));
                }

                // Written before the chunk goes out. Scene audio is composed
                // rather than read from a file, so nothing written down proves
                // it is still the same audio — the server's own fingerprint
                // does, and answers a chunk asked for again only when it
                // matches.
                if (!CanRunContinue(PublishSceneTranscriptionPartial(operation)))
                    return;
            }
            catch
            {
                WithdrawSceneTranscriptionName(operation, name);
                throw;
            }

            (string path, FileStream stream) = AiTemporaryFileStore.Create(
                "audio",
                "scene-mix",
                ".wav");
            TimeSpan uploadedDuration;
            try
            {
                try
                {
                    using (stream)
                    {
                        uploadedDuration = await WriteSceneMixWaveAsync(
                            stream,
                            rangeStart + chunkOffset,
                            chunkDuration,
                            RequestToken);
                    }
                }
                catch
                {
                    WithdrawSceneTranscriptionName(operation, name);
                    throw;
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
                    response = NormalizeTranscriptionResponse(response, uploadedDuration.TotalSeconds, operation.ExpectedDraftScopeRevision);
                }
                catch (AiProviderErrorException)
                {
                    // Failed or invalid settled responses need a fresh key on retry.
                    // Its key would keep answering with that failure, so the
                    // rest of the run takes new ones — written down as well, or
                    // a run picked up later asks under the spent key again and
                    // gets the same failure every time.
                    if (operation.RequestKey.Retire())
                    {
                        UpdateOutstandingCaptionRequest();
                        PublishSceneTranscriptionPartial(operation);
                    }
                    throw;
                }
                catch (Exception ex) when (AiRequestOutcome.CanWithdraw(name, ex))
                {
                    WithdrawSceneTranscriptionName(operation, name);
                    throw;
                }
                if (!operation.RequestKey.Retire(name))
                    return;
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
                UpdateOutstandingCaptionRequest();
                // If this cannot be written down, the response may already have
                // been charged. Stop before the final ClearPartialResult can delete
                // the previous durable seed; the next attempt can persist this
                // in-memory progress and resume by idempotency key.
                if (!CanRunContinue(PublishSceneTranscriptionPartial(operation)))
                    return;
            }
            finally
            {
                DeleteTemporaryAudio(path);
            }
        }

        if (!IsSceneResultCurrent(operation, source))
            return;

        operationLifetime.TryPublish(() =>
        {
            _lastCaptionLanguage = operation.DetectedLanguage ?? language;
            DetectedLanguageText.Value = CreateDetectedLanguageText(_lastCaptionLanguage);
            ResultSegments.Value = CloneSegments(operation.Segments);
            ClearPartialResult();
        });
    }

    // Takes up the pending scene run when it still matches, else a retained one that does, else a
    // new one, and makes it the pending scene run.
    private SceneTranscriptionOperation BeginSceneTranscription(
        EditViewModel editor,
        AudioSourceItem source,
        string? language,
        TimeSpan rangeStart,
        TimeSpan duration,
        int chunkCount,
        long draftScopeRevision)
    {
        bool canResume = CanResumeSceneTranscription(
            source,
            language,
            rangeStart,
            duration,
            chunkCount);
        SceneTranscriptionOperation operation;
        if (canResume)
        {
            operation = _pendingSceneTranscription!;
            operation.ExpectedCaptionRevision = Interlocked.Read(ref _captionDocumentRevision);
            if (operation.CompletedChunkCount == operation.ChunkCount)
            {
                operation.ExpectedSceneAudioRevision = Interlocked.Read(ref _sceneAudioRevision);
            }
        }
        else
        {
            CaptionDraftEntry? retained = _retainedCaptionRecoveries.FirstOrDefault(entry =>
                CanUseSceneTranscriptionDraft(
                    entry.Draft,
                    editor.Scene.Id,
                    language,
                    rangeStart,
                    duration,
                    SceneMixChunkDuration,
                    chunkCount));
            if (!TryParkCurrentCaptionRecovery())
                throw new SubtitleInputException(Strings.AiSubtitle_RunCannotBeRecorded);

            if (retained is not null)
            {
                _retainedCaptionRecoveries.Remove(retained);
                operation = RestoreSceneTranscriptionOperation(
                    retained.Draft,
                    source,
                    Interlocked.Read(ref _captionDocumentRevision),
                    draftScopeRevision,
                    Interlocked.Read(ref _sceneAudioRevision));
                _captionDraftJobId = retained.JobId;
            }
            else
            {
                ChangeCaptionDraftJob(null, deleteCurrent: false);
                operation = new SceneTranscriptionOperation(
                    source,
                    language,
                    rangeStart,
                    duration,
                    SceneMixChunkDuration,
                    chunkCount,
                    Interlocked.Read(ref _captionDocumentRevision),
                    draftScopeRevision,
                    Interlocked.Read(ref _sceneAudioRevision),
                    editor.Scene.Id,
                    _sceneAudioSessionId);
            }
        }
        operation.Source = source;
        _pendingSceneTranscription = operation;
        _pendingSourceTranscription = null;
        UpdateOutstandingCaptionRequest();
        return operation;
    }

    // Whether a finished scene run still answers what the dialog shows, over the same scene audio.
    private bool IsSceneResultCurrent(SceneTranscriptionOperation operation, AudioSourceItem source)
        => ReferenceEquals(SelectedAudioSource.Value, source)
            && TryGetSceneRange(out TimeSpan currentRangeStart, out TimeSpan currentDuration)
            && currentRangeStart == operation.RangeStart
            && currentDuration == operation.Duration
            && string.Equals(
                SelectedSourceLanguage.Value.Code,
                operation.Language,
                StringComparison.Ordinal)
            && operation.ExpectedCaptionRevision == Interlocked.Read(ref _captionDocumentRevision)
            && operation.ExpectedSceneAudioRevision == Interlocked.Read(ref _sceneAudioRevision)
            && IsCurrentCaptionDraftScope(operation.ExpectedDraftScopeRevision);

    private async Task<TimeSpan> WriteSceneMixWaveAsync(
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
        return writer.UploadedDuration;
    }

    private sealed class RejectedTranscriptionResultException(InvalidDataException inner)
        : Exception("The transcription result was rejected by the client.", inner);

    private AiTranscriptionResponse NormalizeTranscriptionResponse(AiTranscriptionResponse response, double duration, long draftScopeRevision)
    {
        try
        {
            AiTranscriptionSegment[] segments = ValidateTranscriptionSegments(response.Segments, duration);
            if (!_disposed && IsCurrentCaptionDraftScope(draftScopeRevision))
                HasRejectedTranscriptionResult.Value = false;
            return response with { Segments = segments };
        }
        catch (InvalidDataException ex)
        {
            if (!_disposed && IsCurrentCaptionDraftScope(draftScopeRevision))
                HasRejectedTranscriptionResult.Value = true;
            throw new RejectedTranscriptionResultException(ex);
        }
    }

    internal static AiTranscriptionSegment[] ValidateTranscriptionSegments(
        IReadOnlyList<AiTranscriptionSegment> segments,
        double maximumEndSeconds)
    {
        const double TimestampToleranceSeconds = 0.05;
        if (!double.IsFinite(maximumEndSeconds) || maximumEndSeconds <= 0)
            throw new InvalidDataException("The transcription chunk duration is invalid.");
        if (segments.Count == 0)
            return [];
        double previousStart = -1;
        double previousEnd = 0;
        var normalized = new AiTranscriptionSegment[segments.Count];
        for (int i = 0; i < segments.Count; i++)
        {
            AiTranscriptionSegment segment = segments[i];
            if (segment is null
                || !double.IsFinite(segment.Start)
                || !double.IsFinite(segment.End)
                || segment.Start < 0
                || segment.Start < previousStart
                || segment.End < previousEnd
                || segment.Start < previousEnd - TimestampToleranceSeconds
                || segment.End <= segment.Start
                || segment.End > maximumEndSeconds + TimestampToleranceSeconds
                || Math.Min(segment.End, maximumEndSeconds) <= segment.Start
                || !IsTimeSpanRepresentable(segment.Start)
                || !IsTimeSpanRepresentable(segment.End)
                || string.IsNullOrWhiteSpace(segment.Text))
            {
                // This is a client-side rejection of a potentially paid success, not a
                // settled provider failure. Keep its request key available for recovery.
                throw new InvalidDataException("The transcription provider returned an invalid segment set.");
            }

            normalized[i] = new AiTranscriptionSegment
            {
                Start = segment.Start,
                End = Math.Min(segment.End, maximumEndSeconds),
                Text = segment.Text,
            };
            previousStart = segment.Start;
            previousEnd = normalized[i].End;
        }
        return normalized;
    }

    private static bool IsTimeSpanRepresentable(double seconds)
    {
        try
        {
            _ = TimeSpan.FromSeconds(seconds);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
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
            && operation.CompletedChunkCount <= operation.ChunkCount
            && source.IsSceneMix
            && (operation.Source is null || operation.Source.IsSceneMix)
            && operation.Language == language
            && operation.RangeStart == rangeStart
            && operation.Duration == duration
            && operation.ChunkDuration == SceneMixChunkDuration
            && operation.ChunkCount == chunkCount
            && IsCurrentCaptionDraftScope(operation.ExpectedDraftScopeRevision)
            && (operation.CompletedChunkCount == operation.ChunkCount
                || operation.ExpectedSceneAudioRevision == Interlocked.Read(ref _sceneAudioRevision)
                && (operation.CompletedChunkCount == 0
                    || operation.AudioSessionId == _sceneAudioSessionId))
            && operation.SceneId == _editViewModel?.Scene.Id;

    private bool CanUseSceneTranscriptionDraft(
        CaptionDraft draft,
        Guid sceneId,
        string? language,
        TimeSpan rangeStart,
        TimeSpan duration,
        TimeSpan chunkDuration,
        int chunkCount)
        => draft.SceneTranscriptionResume is { } resume
            && HoldsPaidWork(draft)
            && resume.SceneId == sceneId
            && resume.Language == language
            && resume.RangeStart == rangeStart
            && resume.Duration == duration
            && resume.ChunkDuration == chunkDuration
            && resume.ChunkCount == chunkCount
            && (resume.CompletedChunkCount == 0
                || resume.CompletedChunkCount == resume.ChunkCount
                || resume.AudioSessionId == _sceneAudioSessionId);

    private static SceneTranscriptionOperation RestoreSceneTranscriptionOperation(
        CaptionDraft draft,
        AudioSourceItem? source,
        long captionRevision,
        long draftScopeRevision,
        long sceneAudioRevision)
    {
        CaptionSceneTranscriptionResume resume = draft.SceneTranscriptionResume
            ?? throw new InvalidDataException("The retained scene transcription has no resume state.");
        var operation = new SceneTranscriptionOperation(
            source,
            resume.Language,
            resume.RangeStart,
            resume.Duration,
            resume.ChunkDuration,
            resume.ChunkCount,
            captionRevision,
            draftScopeRevision,
            sceneAudioRevision,
            resume.SceneId,
            resume.AudioSessionId,
            SeedOrNull(resume.RequestKeySeed),
            resume.RequestKeyNamePending)
        {
            CompletedChunkCount = resume.CompletedChunkCount,
            DetectedLanguage = resume.DetectedLanguage,
            RequestKeyModel = resume.RequestKeyModel,
        };
        operation.Segments.AddRange(CloneSegments(resume.Segments));
        return operation;
    }

    private CaptionDraftOutcome PublishSceneTranscriptionPartial(SceneTranscriptionOperation operation)
    {
        if (_disposed || !IsCurrentCaptionDraftScope(operation.ExpectedDraftScopeRevision))
            return CaptionDraftOutcome.Superseded;

        _pendingSceneTranscription = operation;
        string? detectedLanguage = operation.DetectedLanguage ?? operation.Language;
        AiTranscriptionSegment[] segments = CloneSegments(operation.Segments);
        if (SetPartialResult(new RecoverableCaptionResult(
            CreateCaptionDocument(segments, detectedLanguage),
            detectedLanguage,
            segments,
            PartialResultKind.Transcription,
            operation.CompletedChunkCount,
            operation.ChunkCount,
            operation.ExpectedDraftScopeRevision)) is var outcome
            and not CaptionDraftOutcome.Recorded)
        {
            return outcome;
        }
        ShowRunProgress(
            operation.CompletedChunkCount,
            operation.ChunkCount,
            Strings.AiSubtitle_PartialTranscriptionAvailable);
        _transcriptionEstimateRevision.Value++;
        return CaptionDraftOutcome.Recorded;
    }

    private bool CanResumeSourceTranscription(
        AudioSourceItem source,
        string filePath,
        string? language,
        SourceChunkPlan plan,
        SourceAudioFingerprint fingerprint,
        long draftScopeRevision)
        => _pendingSourceTranscription is { } operation
            && (operation.CompletedChunkCount > 0
                || operation.RequestKey.HasOutstandingName.Value)
            && operation.CompletedChunkCount <= operation.ChunkCount
            && (operation.Source is null || AudioSourceItem.CanResume(source, operation.Source))
            && AudioSourceItem.FilePathsEqual(operation.FilePath, filePath)
            && operation.ElementId == source.ElementId
            && operation.Language == language
            && operation.SampleRate == plan.SampleRate
            && operation.SourceStartSamples == plan.StartSamples
            && operation.TotalSamples == plan.TotalSamples
            && operation.ChunkSamples == plan.ChunkSamples
            && operation.ChunkCount == plan.ChunkCount
            && operation.FileLength == fingerprint.FileLength
            && operation.LastWriteTimeUtcTicks == fingerprint.LastWriteTimeUtcTicks
            && operation.ExpectedDraftScopeRevision == draftScopeRevision
            && IsCurrentCaptionDraftScope(draftScopeRevision);

    private static bool CanUseSourceTranscriptionDraft(
        CaptionDraft draft,
        AudioSourceItem source,
        string filePath,
        string? language,
        SourceChunkPlan plan,
        SourceAudioFingerprint fingerprint)
        => draft.SourceTranscriptionResume is { } resume
            && HoldsPaidWork(draft)
            && AudioSourceItem.FilePathsEqual(resume.FilePath, filePath)
            && resume.ElementId == source.ElementId
            && resume.Language == language
            && resume.SampleRate == plan.SampleRate
            && resume.SourceStartSamples == plan.StartSamples
            && resume.TotalSamples == plan.TotalSamples
            && resume.ChunkSamples == plan.ChunkSamples
            && resume.ChunkCount == plan.ChunkCount
            && resume.FileLength == fingerprint.FileLength
            && resume.LastWriteTimeUtcTicks == fingerprint.LastWriteTimeUtcTicks;

    private static SourceTranscriptionOperation RestoreSourceTranscriptionOperation(
        CaptionDraft draft,
        AudioSourceItem source,
        long captionRevision,
        long draftScopeRevision)
    {
        CaptionSourceTranscriptionResume resume = draft.SourceTranscriptionResume
            ?? throw new InvalidDataException("The retained source transcription has no resume state.");
        var operation = new SourceTranscriptionOperation(
            source,
            resume.FilePath,
            resume.ElementId,
            resume.FileLength,
            resume.LastWriteTimeUtcTicks,
            resume.Language,
            resume.SampleRate,
            resume.SourceStartSamples ?? -1,
            resume.TotalSamples,
            resume.ChunkSamples,
            resume.ChunkCount,
            captionRevision,
            draftScopeRevision,
            SeedOrNull(resume.RequestKeySeed),
            resume.RequestKeyNamePending)
        {
            CompletedChunkCount = resume.CompletedChunkCount,
            DetectedLanguage = resume.DetectedLanguage,
            RequestKeyModel = resume.RequestKeyModel,
        };
        operation.SourceSegments.AddRange(CloneSegments(resume.Segments));
        return operation;
    }

    private CaptionDraftOutcome PublishSourceTranscriptionPartial(SourceTranscriptionOperation operation)
    {
        if (_disposed || !IsCurrentCaptionDraftScope(operation.ExpectedDraftScopeRevision))
            return CaptionDraftOutcome.Superseded;

        _pendingSourceTranscription = operation;
        if (operation.Source is not { } source)
            return CaptionDraftOutcome.Superseded;

        string? detectedLanguage = operation.DetectedLanguage ?? operation.Language;
        AiTranscriptionSegment[] mappedSegments = source.MapSegmentsToScene(
            operation.SourceSegments);
        if (SetPartialResult(new RecoverableCaptionResult(
                CreateCaptionDocument(mappedSegments, detectedLanguage),
                detectedLanguage,
                mappedSegments,
                PartialResultKind.Transcription,
                operation.CompletedChunkCount,
                operation.ChunkCount,
                operation.ExpectedDraftScopeRevision)) is var outcome
            and not CaptionDraftOutcome.Recorded)
        {
            return outcome;
        }
        ShowRunProgress(
            operation.CompletedChunkCount,
            operation.ChunkCount,
            Strings.AiSubtitle_PartialTranscriptionAvailable);
        _transcriptionEstimateRevision.Value++;
        return CaptionDraftOutcome.Recorded;
    }

    internal static long GetSelectedSourceSampleCount(
        AudioStreamInfo audioInfo,
        TimeSpan selectedDuration)
    {
        if (audioInfo.SampleRate <= 0)
            throw new SubtitleInputException(Strings.AiSubtitle_NoAudioInRange);

        double samples = selectedDuration.TotalSeconds * audioInfo.SampleRate;
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

    // When this run's name can no longer be used because its job vanished or belongs to another
    // request, retain paid chunks and purchase only the remainder under a new name. Persist that
    // name so another restoration does not retry the unusable one.
    private void RetireTranscriptionRunNames()
    {
        if (_pendingSourceTranscription is { } source)
        {
            if (source.RequestKey.Retire())
                PublishSourceTranscriptionPartial(source);
        }

        if (_pendingSceneTranscription is { } scene)
        {
            if (scene.RequestKey.Retire())
                PublishSceneTranscriptionPartial(scene);
        }

        UpdateOutstandingCaptionRequest();
    }

    // Withdraw a name that ended before dispatch and persist that fact. Leaving it marked as held
    // would make the next session treat unpaid work as recovery and bypass its balance check.
    private void WithdrawSourceTranscriptionName(
        SourceTranscriptionOperation operation,
        AiRequestName name)
    {
        if (!operation.RequestKey.WithdrawAfterNoReservation(name))
            return;
        UpdateOutstandingCaptionRequest();
        PublishSourceTranscriptionPartial(operation);
    }

    private void WithdrawSceneTranscriptionName(
        SceneTranscriptionOperation operation,
        AiRequestName name)
    {
        if (!operation.RequestKey.WithdrawAfterNoReservation(name))
            return;
        UpdateOutstandingCaptionRequest();
        PublishSceneTranscriptionPartial(operation);
    }

    // The name the chunk is uploaded under. Part of what the server fingerprints
    // the request by, so it has to be the same on every attempt at one chunk;
    // the file it is read from is named for uniqueness on disk instead.
    private static string FormatChunkFileName(string prefix, int chunkIndex)
        => $"{prefix}-chunk-{chunkIndex:D4}.wav";

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
                AiOperations.Transcription,
                duration.TotalSeconds,
                TranscriptionModelPicker.SelectedModel)
            : null;
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
        Guid audioSessionId,
        string? requestKeySeed = null,
        bool requestKeyNamePending = false)
    {
        public AudioSourceItem? Source { get; set; } = source;

        /// <summary>
        /// Names this run's requests, one key per chunk. Held with the rest of
        /// the resume state so a retried chunk asks for the transcription it
        /// already paid for instead of buying a second one.
        /// </summary>
        public AiRequestKey RequestKey { get; } = new(requestKeySeed, requestKeyNamePending);

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

        public long ExpectedSceneAudioRevision { get; set; } = expectedSceneAudioRevision;

        public Guid SceneId { get; } = sceneId;

        public Guid AudioSessionId { get; } = audioSessionId;

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
        long sourceStartSamples,
        long totalSamples,
        int chunkSamples,
        int chunkCount,
        long expectedCaptionRevision,
        long expectedDraftScopeRevision,
        string? requestKeySeed = null,
        bool requestKeyNamePending = false)
    {
        public AudioSourceItem? Source { get; set; } = source;

        /// <summary>
        /// Names this run's requests, one key per chunk. Restored with the rest
        /// of the resume state so a chunk resumed after a restart asks for the
        /// transcription it already paid for instead of buying a second one.
        /// </summary>
        public AiRequestKey RequestKey { get; } = new(requestKeySeed, requestKeyNamePending);

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

        public long SourceStartSamples { get; } = sourceStartSamples;

        public long TotalSamples { get; set; } = totalSamples;

        public int ChunkSamples { get; } = chunkSamples;

        public int ChunkCount { get; set; } = chunkCount;

        public long ExpectedCaptionRevision { get; set; } = expectedCaptionRevision;

        public long ExpectedDraftScopeRevision { get; } = expectedDraftScopeRevision;

        public List<AiTranscriptionSegment> SourceSegments { get; } = [];

        public string? DetectedLanguage { get; set; }

        public int CompletedChunkCount { get; set; }
    }
}

internal readonly record struct SourceAudioFingerprint(
    long FileLength,
    long LastWriteTimeUtcTicks);
