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
    private RecoverableCaptionResult? _partialResult;

    private AiCaptionHistoryResult? _pendingHistoryResult;

    // Another tab owns this scene's draft, so this tab cannot write it. A paid request created
    // without durable storage would lose its name when the session ends.
    private bool _captionDraftScopeIsHeldElsewhere;
    // This scene's draft was unreadable, which is not the same as absent, so do not overwrite it.
    private bool _captionDraftIsUnreadable;
    private CaptionDraftScope? _captionDraftBaseScope;

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

    // Whether replacement requires confirmation. Visible state is insufficient: a run whose first
    // chunk received no response looks empty but may still hold a paid request name that silent
    // replacement would erase.
    private bool HasUnsavedCaptionWork()
        => HasPartialResult.Value
            || _editableCues.Count > 0
            || HasOutstandingTranscriptionRequest.Value
            || HasOutstandingTranslationRequest.Value
            || _retainedCaptionRecoveries.Any(entry => HoldsPaidWork(entry.Draft));

    private void ApplyHistoryResult(AiCaptionHistoryResult result)
    {
        ChangeCaptionDraftJob(result.JobId.Value, deleteCurrent: true);
        _pendingTranslation = null;
        _pendingSceneTranscription = null;
        _pendingSourceTranscription = null;
        // Held names are gone now. Notify dependents so model loading resumes and balance checks
        // stop treating the request as recovery work.
        UpdateOutstandingCaptionRequest();
        _partialResult = null;
        HasPartialResult.Value = false;
        PartialResultMessage.Value = null;
        _lastCaptionLanguage = result.Language;
        DetectedLanguageText.Value = CreateDetectedLanguageText(result.Language);
        ResultSegments.Value = CloneSegments(result.Segments);
        Error.Value = null;
    }

    /// <summary>What became of an attempt to write a run down.</summary>
    private enum CaptionDraftOutcome
    {
        /// <summary>
        /// Written down, or there was nothing to write it to — no signed-in
        /// user, no project, no scene. Either way the run may go ahead.
        /// </summary>
        Recorded,

        /// <summary>
        /// Nowhere, though it should have been. Another tab holds this scene's
        /// draft, or the write failed. A request sent now would take its name
        /// with it when the session ends.
        /// </summary>
        NotRecorded,

        /// <summary>The run this belongs to is no longer the one on screen.</summary>
        Superseded,
    }

    // Whether a run may go on after writing itself down. A run that is no longer the one on
    // screen stops quietly; one that should have been written down and was not throws, since a
    // request sent now would take its name with it when the session ends.
    private static bool CanRunContinue(CaptionDraftOutcome outcome)
        => outcome switch
        {
            CaptionDraftOutcome.Superseded => false,
            CaptionDraftOutcome.NotRecorded => throw new SubtitleInputException(
                Strings.AiSubtitle_RunCannotBeRecorded),
            _ => true,
        };

    // How far a run has come, once a piece of it has come back.
    private void ShowRunProgress(int completed, int total, string partialFormat)
        => PartialResultMessage.Value = completed <= 0
            ? null
            : string.Format(
                completed == total
                    ? Strings.AiSubtitle_CompletedResultAvailable
                    : partialFormat,
                completed,
                total);

    private CaptionDraftOutcome SetPartialResult(RecoverableCaptionResult result)
    {
        if (!IsCurrentCaptionDraftScope(result.DraftScopeRevision))
            return CaptionDraftOutcome.Superseded;

        // Another tab previously owned this scene's draft. Retry now in case it released the
        // draft; giving up after one failure would permanently block paid operations in this tab.
        if (_captionDraftSession is null && _captionDraftScopeIsHeldElsewhere)
            TakeOverReleasedCaptionDraft();

        _partialResult = result;
        // A run that has only named its first piece has nothing to apply yet.
        // It is still written down — the name is what makes that piece
        // collectable — but the button that imports a partial stays closed.
        HasPartialResult.Value = result.CompletedSteps > 0;
        if (_captionDraftIsUnreadable && !CanWriteOverUnreadableDraft())
            return CaptionDraftOutcome.NotRecorded;

        if (_captionDraftSession is null)
        {
            return _captionDraftScopeIsHeldElsewhere
                ? CaptionDraftOutcome.NotRecorded
                : CaptionDraftOutcome.Recorded;
        }

        CaptionDraft draft = CreateCaptionDraft(result);
        if (!HoldsPaidWork(draft) && _retainedCaptionRecoveries.Count > 0)
        {
            ResetCurrentCaptionRecovery();
            PersistRetainedCaptionRecoveries();
            return CaptionDraftOutcome.Recorded;
        }

        try
        {
            _captionDraftSession.Save(new CaptionDraftEntry(
                _captionDraftJobId,
                draft,
                _retainedCaptionRecoveries.ToArray()));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist a recoverable paid caption result.");
            // The failed write leaves the previous draft on disk. Do not delete it: it may be the
            // only record of paid chunks and their name. Replaying that older name is safe because
            // it identifies paid work, settled work, or nothing; deleting it can force a repurchase.
            return CaptionDraftOutcome.NotRecorded;
        }
        return CaptionDraftOutcome.Recorded;
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
                translation.RequestKeyModel,
                translation.RequestKey.HasOutstandingName.Value,
                translation.Limits.MaxSegments,
                translation.Limits.MaxCharacters,
                translation.Limits.MaxRequestBytes);
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
                scene.CompletedChunkCount,
                scene.RequestKeySeed,
                scene.RequestKeyModel,
                scene.RequestKey.HasOutstandingName.Value,
                scene.AudioSessionId);
        }

        CaptionSourceTranscriptionResume? sourceResume = null;
        if (result.Kind == PartialResultKind.Transcription
            && result.Segments is { } sourceResultSegments
            && _pendingSourceTranscription is { } sourceTranscription
            && sourceTranscription.CompletedChunkCount == result.CompletedSteps
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
                sourceTranscription.RequestKeyModel,
                sourceTranscription.RequestKey.HasOutstandingName.Value,
                sourceTranscription.SourceStartSamples);
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

    private bool TryParkCurrentCaptionRecovery()
    {
        if (_partialResult is { } result)
        {
            var entry = new CaptionDraftEntry(_captionDraftJobId, CreateCaptionDraft(result));
            if (HoldsPaidWork(entry.Draft))
            {
                string identity = GetRecoveryIdentity(entry);
                int duplicate = _retainedCaptionRecoveries.FindIndex(candidate =>
                    string.Equals(GetRecoveryIdentity(candidate), identity, StringComparison.Ordinal));
                if (duplicate >= 0)
                {
                    _retainedCaptionRecoveries[duplicate] = entry;
                }
                else
                {
                    if (_retainedCaptionRecoveries.Count
                        >= FileCaptionDraftStore.MaximumRetainedRecoveries)
                        return false;
                    _retainedCaptionRecoveries.Add(entry);
                }
            }
        }

        ResetCurrentCaptionRecovery();
        return true;
    }

    private void ResetCurrentCaptionRecovery()
    {
        HasRejectedTranscriptionResult.Value = false;
        _partialResult = null;
        _pendingTranslation = null;
        _pendingSceneTranscription = null;
        _pendingSourceTranscription = null;
        _captionDraftJobId = null;
        HasPartialResult.Value = false;
        PartialResultMessage.Value = null;
        ClearRestoredModelPreference();
        UpdateOutstandingCaptionRequest();
    }

    private static string GetRecoveryIdentity(CaptionDraftEntry entry)
    {
        CaptionDraft draft = entry.Draft;
        string seed = draft.TranslationResume?.RequestKeySeed
            ?? draft.SourceTranscriptionResume?.RequestKeySeed
            ?? draft.SceneTranscriptionResume?.RequestKeySeed
            ?? string.Empty;
        return $"{draft.Kind}:{seed}:{entry.JobId}";
    }

    private void PersistRetainedCaptionRecoveries()
    {
        if (_captionDraftSession is null || _retainedCaptionRecoveries.Count == 0)
            return;

        CaptionDraftEntry root = _retainedCaptionRecoveries[^1];
        CaptionDraftEntry[] additional = _retainedCaptionRecoveries
            .Take(_retainedCaptionRecoveries.Count - 1)
            .ToArray();
        try
        {
            _captionDraftSession.Save(new CaptionDraftEntry(
                root.JobId,
                root.Draft,
                additional));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist retained paid caption recoveries.");
        }
    }

    private void RestoreCaptionDraft()
    {
        if (_captionDraftSession is null)
            return;

        CaptionDraftReadResult read;
        try
        {
            read = _captionDraftSession.Read();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to restore the recoverable paid caption result.");
            read = CaptionDraftReadResult.Unreadable;
        }

        // Unreadable is not absent. The draft may contain an already-paid request name, so do not
        // write this scene or begin paid work until it becomes readable.
        _captionDraftIsUnreadable = read.Outcome == CaptionDraftReadOutcome.Unreadable;
        CaptionDraftEntry? entry = read.Entry;
        if (entry is null)
            return;

        try
        {
            _retainedCaptionRecoveries.Clear();
            _retainedCaptionRecoveries.AddRange(entry.Recoveries);
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
                RestoreTranslationResume(draft, translation, draftScopeRevision);

            if (draft.SceneTranscriptionResume is { } scene
                && scene.SceneId == _editViewModel?.Scene.Id)
            {
                RestoreSceneResume(draft, scene, draftScopeRevision);
            }

            if (draft.SourceTranscriptionResume is { } source)
                RestoreSourceResume(source, draftScopeRevision);

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
            // Expose the name held by a restored run to the UI. Otherwise a run that looks empty
            // because only its first chunk was sent could be overwritten without confirmation.
            UpdateOutstandingCaptionRequest();
            _transcriptionEstimateRevision.Value++;
            RefreshTranslationEstimate();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ignored an invalid recoverable caption draft.");
            ClearPartialResult();
        }
    }

    private void RestoreTranslationResume(
        CaptionDraft draft,
        CaptionTranslationResume translation,
        long draftScopeRevision)
    {
        try
        {
            TranslationOperation operation = RestoreTranslationOperation(
                draft,
                Interlocked.Read(ref _captionDocumentRevision),
                draftScopeRevision);
            // The unfinished batches are named partly by the model the
            // run used. Landing on another one would name them
            // differently and buy them again.
            _restoredTranslationModel = ModelIdOrNull(translation.RequestKeyModel);
            PreferRestoredModel(
                TranslationModelPicker,
                _restoredTranslationModel);
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
            if (draft.CompletedSteps == 0
                && translation.RequestKeyNamePending
                && _editableCues.Count == 0)
            {
                ReplaceCues(new CaptionDocument(
                    operation.SourceDocument.Cues.Select(cue => cue with { })));
                operation.ExpectedCaptionRevision = Interlocked.Read(
                    ref _captionDocumentRevision);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Restored a paid caption draft without resumable translation state.");
        }
    }

    private void RestoreSceneResume(
        CaptionDraft draft,
        CaptionSceneTranscriptionResume scene,
        long draftScopeRevision)
    {
        // Scene audio is composed rather than read from a file. A pending first chunk is
        // safe to retry because the server validates that chunk's fingerprint, and a
        // complete result needs no new audio. A partially completed run carries its old
        // session identifier so it cannot combine those unverifiable chunks with audio
        // composed by this dialog lifetime.
        _pendingSceneTranscription = RestoreSceneTranscriptionOperation(
            draft,
            null,
            Interlocked.Read(ref _captionDocumentRevision),
            draftScopeRevision,
            Interlocked.Read(ref _sceneAudioRevision));
        _restoredTranscriptionModel = ModelIdOrNull(scene.RequestKeyModel);
        PreferRestoredModel(
            TranscriptionModelPicker,
            _restoredTranscriptionModel);
        SelectSourceLanguage(scene.Language);
    }

    // Built here rather than by RestoreSourceTranscriptionOperation so the segments still
    // arrive after the model is chosen, as they always have.
    private void RestoreSourceResume(
        CaptionSourceTranscriptionResume source,
        long draftScopeRevision)
    {
        _pendingSourceTranscription = new SourceTranscriptionOperation(
            null,
            source.FilePath,
            source.ElementId,
            source.FileLength,
            source.LastWriteTimeUtcTicks,
            source.Language,
            source.SampleRate,
            source.SourceStartSamples ?? -1,
            source.TotalSamples,
            source.ChunkSamples,
            source.ChunkCount,
            Interlocked.Read(ref _captionDocumentRevision),
            draftScopeRevision,
            SeedOrNull(source.RequestKeySeed),
            source.RequestKeyNamePending)
        {
            CompletedChunkCount = source.CompletedChunkCount,
            DetectedLanguage = source.DetectedLanguage,
            RequestKeyModel = source.RequestKeyModel,
        };
        _restoredTranscriptionModel = ModelIdOrNull(source.RequestKeyModel);
        PreferRestoredModel(
            TranscriptionModelPicker,
            _restoredTranscriptionModel);
        _pendingSourceTranscription.SourceSegments.AddRange(CloneSegments(source.Segments));
        SelectSourceLanguage(source.Language);
    }

    private void SelectSourceLanguage(string? code)
    {
        CaptionLanguageOption? sourceOption = SourceLanguages.FirstOrDefault(option =>
            string.Equals(option.Code, code, StringComparison.Ordinal));
        if (sourceOption is not null)
            SelectedSourceLanguage.Value = sourceOption;
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
        ResetCurrentCaptionRecovery();
        if (_retainedCaptionRecoveries.Count == 0)
        {
            try
            {
                _captionDraftSession?.Delete();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to remove the recoverable caption draft.");
            }
        }
        else
        {
            PersistRetainedCaptionRecoveries();
        }
        _transcriptionEstimateRevision.Value++;
        RefreshTranslationEstimate();
    }

    private void InvalidatePartialResultResume()
    {
        _transcriptionEstimateRevision.Value++;
        RefreshTranslationEstimate();
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
        if (deleteCurrent)
            _retainedCaptionRecoveries.Clear();
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

    private void UpdateOutstandingCaptionRequest()
    {
        if (_disposed)
            return;

        HasOutstandingTranscriptionRequest.Value =
            _pendingSourceTranscription?.RequestKey.HasOutstandingName.Value == true
            || _pendingSceneTranscription?.RequestKey.HasOutstandingName.Value == true
            || _pendingSourceTranscription is { CompletedChunkCount: > 0 } completedSource
            && completedSource.CompletedChunkCount == completedSource.ChunkCount
            || _pendingSceneTranscription is { CompletedChunkCount: > 0 } completedScene
            && completedScene.CompletedChunkCount == completedScene.ChunkCount;
        HasOutstandingTranslationRequest.Value =
            _pendingTranslation?.RequestKey.HasOutstandingName.Value == true
            || _pendingTranslation is { CompletedBatchCount: > 0 } completedTranslation
            && completedTranslation.CompletedBatchCount == completedTranslation.Batches.Count
            || _retainedCaptionRecoveries.Any(entry =>
                entry.Draft.Kind == CaptionDraftKind.Translation
                && (entry.Draft.TranslationResume?.RequestKeyNamePending == true
                    || entry.Draft.CompletedSteps > 0
                    && entry.Draft.CompletedSteps == entry.Draft.TotalSteps));
        HasOutstandingTranscriptionRequest.Value |= _retainedCaptionRecoveries.Any(entry =>
            entry.Draft.Kind == CaptionDraftKind.Transcription
            && (entry.Draft.SourceTranscriptionResume?.RequestKeyNamePending == true
                || entry.Draft.SceneTranscriptionResume?.RequestKeyNamePending == true
                || entry.Draft.CompletedSteps > 0
                && entry.Draft.CompletedSteps == entry.Draft.TotalSteps));
    }

    private bool HasPendingTranslationName()
        => _pendingTranslation?.RequestKey.HasOutstandingName.Value == true
            || _retainedCaptionRecoveries.Any(entry =>
                entry.Draft.TranslationResume?.RequestKeyNamePending == true);

    private bool HasPendingTranscriptionName()
        => _pendingSourceTranscription?.RequestKey.HasOutstandingName.Value == true
            || _pendingSceneTranscription?.RequestKey.HasOutstandingName.Value == true
            || _retainedCaptionRecoveries.Any(entry =>
                entry.Draft.SourceTranscriptionResume?.RequestKeyNamePending == true
                || entry.Draft.SceneTranscriptionResume?.RequestKeyNamePending == true);

    private IReadOnlyList<AiModelId> OutstandingTranslationModels()
        => EnumerateOutstandingModels(
                _pendingTranslation is { } current
                && (current.RequestKey.HasOutstandingName.Value
                    || current.CompletedBatchCount > 0
                    && current.CompletedBatchCount < current.Batches.Count)
                        ? current.RequestKeyModel
                        : null,
                _retainedCaptionRecoveries
                    .Where(entry => entry.Draft.TranslationResume is { } resume
                        && (resume.RequestKeyNamePending
                            || entry.Draft.CompletedSteps > 0
                            && entry.Draft.CompletedSteps < entry.Draft.TotalSteps))
                    .Select(entry => entry.Draft.TranslationResume!.RequestKeyModel))
            .ToArray();

    private IReadOnlyList<AiModelId> OutstandingTranscriptionModels()
        => EnumerateOutstandingModels(
                CurrentTranscriptionModel(),
                _retainedCaptionRecoveries
                    .Where(entry => entry.Draft.Kind == CaptionDraftKind.Transcription
                        && (entry.Draft.CompletedSteps > 0
                            && entry.Draft.CompletedSteps < entry.Draft.TotalSteps
                            || entry.Draft.SourceTranscriptionResume?.RequestKeyNamePending == true
                            || entry.Draft.SceneTranscriptionResume?.RequestKeyNamePending == true))
                    .Select(entry => entry.Draft.SourceTranscriptionResume?.RequestKeyModel
                        ?? entry.Draft.SceneTranscriptionResume?.RequestKeyModel
                        ?? string.Empty))
            .ToArray();

    private string? CurrentTranscriptionModel()
    {
        if (_pendingSourceTranscription is { } source
            && (source.RequestKey.HasOutstandingName.Value
                || source.CompletedChunkCount > 0
                && source.CompletedChunkCount < source.ChunkCount))
        {
            return source.RequestKeyModel;
        }
        if (_pendingSceneTranscription is { } scene
            && (scene.RequestKey.HasOutstandingName.Value
                || scene.CompletedChunkCount > 0
                && scene.CompletedChunkCount < scene.ChunkCount))
        {
            return scene.RequestKeyModel;
        }
        return null;
    }

    private static IEnumerable<AiModelId> EnumerateOutstandingModels(
        string? current,
        IEnumerable<string> retained)
        => retained.Prepend(current ?? string.Empty)
            .Where(model => !string.IsNullOrWhiteSpace(model))
            .Distinct(StringComparer.Ordinal)
            .Select(model => new AiModelId(model));

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
        if (!_disposed && IsCurrentCaptionDraftScope(revision))
        {
            Error.Value = error;
        }
    }

    private void RecordCaptionDraftJob(AiJobId? jobId, long revision)
    {
        if (!_disposed
            && jobId is { } value
            && value.Value.Length > 0
            && IsCurrentCaptionDraftScope(revision))
        {
            _captionDraftJobId = value.Value;
        }
    }

    private void ResetCaptionStateForAccountChange()
    {
        HasRejectedTranscriptionResult.Value = false;
        _partialResult = null;
        _pendingTranslation = null;
        _pendingSceneTranscription = null;
        _pendingSourceTranscription = null;
        _retainedCaptionRecoveries.Clear();
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
        SelectedSubtitlePageIndex.Value = TranscribePageIndex;
        ResultSegments.Value = null;
        ReplaceCues(new CaptionDocument());
        SelectedSourceLanguage.Value = SourceLanguages[0];
        SelectedTargetLanguage.Value = GetDefaultTargetLanguage();
        _transcriptionEstimateRevision.Value++;
        RefreshTranslationEstimate();
    }

    // Recheck whether an unreadable draft is now readable. If it contains paid work, writing this
    // run would overwrite its name and chunks. Claim the scene only after proving the draft empty.
    private bool CanWriteOverUnreadableDraft()
    {
        if (_captionDraftSession is null)
            return false;

        CaptionDraftReadResult read;
        try
        {
            read = _captionDraftSession.Read();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to re-read a caption draft that could not be read.");
            return false;
        }

        if (read.Outcome == CaptionDraftReadOutcome.Unreadable)
            return false;
        if (read.Entry is { } entry && HoldsPaidWork(entry))
            return false;

        _captionDraftIsUnreadable = false;
        return true;
    }

    // Take over a released draft only if it contains no paid work. Overwriting another run's name
    // and chunks mid-execution would make them unreachable and force a repurchase; opening that
    // scene separately can restore them safely instead.
    private void TakeOverReleasedCaptionDraft()
    {
        OpenCaptionDraftSession(_captionDraftBaseScope);
        if (_captionDraftSession is null)
            return;

        CaptionDraftReadResult left;
        try
        {
            left = _captionDraftSession.Read();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read the caption draft left by another tab.");
            left = CaptionDraftReadResult.Unreadable;
        }

        // Take over only after proving the draft empty; unreadable is not empty.
        if (left.Outcome == CaptionDraftReadOutcome.Absent)
            return;
        if (left.Entry is { } entry && !HoldsPaidWork(entry))
            return;

        _captionDraftSession.Dispose();
        _captionDraftSession = null;
        _captionDraftScopeIsHeldElsewhere = true;
    }

    // A draft is protected from overwrite only when it ended with a held name or contains paid
    // chunks. An unreserved, withdrawn name may leave a seed, but no job, so it need not block takeover.
    private static bool HoldsPaidWork(CaptionDraft draft)
        => draft.CompletedSteps > 0
            || draft.TranslationResume?.RequestKeyNamePending == true
            || draft.SourceTranscriptionResume?.RequestKeyNamePending == true
            || draft.SceneTranscriptionResume?.RequestKeyNamePending == true;

    private static bool HoldsPaidWork(CaptionDraftEntry entry)
        => HoldsPaidWork(entry.Draft)
            || entry.Recoveries.Any(recovery => HoldsPaidWork(recovery.Draft));

    private void OpenCaptionDraftSession(CaptionDraftScope? scope)
    {
        _captionDraftSession?.Dispose();
        _captionDraftSession = null;
        // Nothing to write to is not the same as failing to write. Without a
        // signed-in user, a project and a scene there is no draft at all, and
        // the screen works the way it did before drafts existed. A scope that
        // exists and cannot be opened is another tab holding this scene: that
        // run has somewhere it should be written down and cannot be.
        _captionDraftScopeIsHeldElsewhere = false;
        if (scope is null)
            return;

        if (_captionDraftStore.TryOpen(scope, out ICaptionDraftSession? session))
        {
            _captionDraftSession = session;
        }
        else
        {
            _captionDraftScopeIsHeldElsewhere = true;
        }
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
