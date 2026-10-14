using Beutl.Api.Services;
using Beutl.Editor.Services.Captions;
using Beutl.Services;
using Beutl.Services.AI;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

using static Beutl.Services.AI.CaptionTranslationBatcher;

namespace Beutl.ViewModels.Dialogs;

public sealed partial class AiSubtitleDialogViewModel
{
    private async Task TranslateCore()
    {
        using AsyncOperationLifetime.Operation? operationLifetime = _operations.TryEnter();
        if (operationLifetime is null)
            return;
        long captionRevision = Interlocked.Read(ref _captionDocumentRevision);
        long draftScopeRevision = Interlocked.Read(ref _captionDraftScopeRevision);
        if (!TryBuildCaptionDocumentCore(out CaptionDocument? document, out _) || document is null)
            return;
        if (captionRevision != Interlocked.Read(ref _captionDocumentRevision))
            return;

        Error.Value = null;
        IsTranslating.Value = true;
        TranslationPreview.Value = null;
        using CancellationTokenSource requestCts =
            CancellationTokenSource.CreateLinkedTokenSource(
                operationLifetime.CancellationToken,
                _lifetimeCts.Token);
        _requestCts = requestCts;
        try
        {
            string targetLanguage = SelectedTargetLanguage.Value.Code!;
            string? selectedSourceLanguage = SelectedSourceLanguage.Value.Code;
            string? sourceLanguage = selectedSourceLanguage ?? _lastCaptionLanguage;
            TranslationOperation operation = BeginTranslation(
                document,
                targetLanguage,
                selectedSourceLanguage,
                sourceLanguage,
                captionRevision,
                draftScopeRevision);
            // Read once for the whole run, and taken from the run itself once
            // it has named anything — see ModelOfRun.
            AiModelId? runModel = ModelOfRun(
                operation.CompletedBatchCount > 0
                    || operation.RequestKey.HasOutstandingName.Value,
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
                AiCaptionTranslationLimits requestLimits = operation.Limits;
                UpdateOutstandingCaptionRequest();
                if (!await RecordRunPieceAsync(
                        operation,
                        name,
                        () => CreateTranslationAvailabilityRequest(
                            batch,
                            runModel,
                            requestLimits),
                        PublishTranslationPartial,
                        WithdrawTranslationName))
                {
                    return;
                }

                (bool settled, (AiCaptionTranslationResponse response, Dictionary<string, string> translatedBatch)) =
                    await SendRunPieceAsync(
                        operation,
                        name,
                        async () =>
                        {
                            AiCaptionTranslationResponse translation = await _translation.TranslateAsync(
                                new AiCaptionTranslationRequest(
                                    batch.Pieces.Select(piece => new AiCaptionTranslationSegment
                                    {
                                        Id = piece.Id,
                                        Text = piece.Text,
                                        Context = new AiCaptionTranslationSegmentContext(
                                            piece.GroupId,
                                            piece.ContextPartIndex,
                                            piece.Start,
                                            piece.End),
                                    }).ToArray(),
                                    operation.TargetLanguage,
                                    operation.SourceLanguage,
                                    model: runModel,
                                    idempotencyKey: name.Key,
                                    limits: requestLimits),
                                new Progress<AiCaptionTranslationSegment>(segment =>
                                    operationLifetime.TryPublish(() => ShowTranslatedLine(segment))),
                                RequestToken);
                            return (translation, ValidateTranslatedBatch(batch, translation));
                        },
                        PublishTranslationPartial,
                        WithdrawTranslationName);
                if (!settled)
                    return;
                AddTranslatedBatch(operation, translatedBatch);
                RecordCaptionDraftJob(response.JobId, operation.ExpectedDraftScopeRevision);
                operation.CompletedBatchCount++;
                UpdateOutstandingCaptionRequest();
                // Stop if this cannot be written down, as in TranscribeSourceFileAsync.
                if (!CanRunContinue(PublishTranslationPartial(operation)))
                    return;
                RefreshTranslationEstimate();
            }

            if (!IsTranslationResultCurrent(operation, targetLanguage, selectedSourceLanguage))
                return;

            operationLifetime.TryPublish(() =>
            {
                _lastCaptionLanguage = targetLanguage;
                DetectedLanguageText.Value = CreateDetectedLanguageText(targetLanguage);
                ReplaceCues(BuildTranslationDocument(operation, includeUntranslatedParts: false));
                ClearPartialResult();
            });
        }
        catch (Exception ex) when (KnownRunFailureMessage(ex) is { } message)
        {
            operationLifetime.TryPublish(() => SetCaptionErrorIfCurrent(draftScopeRevision, message));
        }
        // This run's name belongs to another request, or the job it made is gone.
        catch (Exception ex) when (ex is AiRequestChangedException or AiRequestWasDeletedException)
        {
            RetireTranslationRunName();
            operationLifetime.TryPublish(() => SetCaptionErrorIfCurrent(
                draftScopeRevision,
                ex is AiRequestWasDeletedException ? Strings.AiRequestWasDeleted : Strings.AiRequestChanged));
        }
        catch (SubtitleInputException ex)
        {
            operationLifetime.TryPublish(() => SetCaptionErrorIfCurrent(draftScopeRevision, ex.Message));
        }
        catch (OperationCanceledException) when (IsRequestCanceled)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to translate subtitles.");
            operationLifetime.TryPublish(() => SetCaptionErrorIfCurrent(draftScopeRevision, Strings.AiUnexpectedError));
        }
        finally
        {
            _requestCts = null;
            operationLifetime.TryPublish(() =>
            {
                if (IsCurrentCaptionDraftScope(draftScopeRevision))
                {
                    IsTranslating.Value = false;
                    TranslationPreview.Value = null;
                }
            });
        }
    }

    // Takes up the pending translation when it still matches, else a retained one that does, else
    // a new one, and makes it the pending translation.
    private TranslationOperation BeginTranslation(
        CaptionDocument document,
        string targetLanguage,
        string? selectedSourceLanguage,
        string? sourceLanguage,
        long captionRevision,
        long draftScopeRevision)
    {
        TranslationOperation operation;
        if (_pendingTranslation is { } current
            && CanUseTranslationOperation(
                current,
                document,
                targetLanguage,
                selectedSourceLanguage,
                draftScopeRevision))
        {
            operation = current;
            operation.ExpectedCaptionRevision = captionRevision;
        }
        else
        {
            CaptionDraftEntry? retained = _retainedCaptionRecoveries.FirstOrDefault(entry =>
                CanUseTranslationDraft(
                    entry.Draft,
                    document,
                    targetLanguage,
                    selectedSourceLanguage));
            if (!TryParkCurrentCaptionRecovery())
            {
                throw new SubtitleInputException(Strings.AiSubtitle_RunCannotBeRecorded);
            }

            if (retained is not null)
            {
                _retainedCaptionRecoveries.Remove(retained);
                operation = RestoreTranslationOperation(
                    retained.Draft.TranslationResume!,
                    captionRevision,
                    draftScopeRevision);
                _captionDraftJobId = retained.JobId;
            }
            else
            {
                ChangeCaptionDraftJob(null, deleteCurrent: false);
                operation = CreateTranslationOperation(
                    document,
                    captionRevision,
                    sourceLanguage,
                    selectedSourceLanguage,
                    targetLanguage,
                    draftScopeRevision);
            }
        }
        _pendingTranslation = operation;
        UpdateOutstandingCaptionRequest();
        return operation;
    }

    // Whether a finished translation still answers what the dialog shows.
    private bool IsTranslationResultCurrent(
        TranslationOperation operation,
        string targetLanguage,
        string? selectedSourceLanguage)
        => !_disposed
            && operation.ExpectedCaptionRevision == Interlocked.Read(ref _captionDocumentRevision)
            && IsCurrentCaptionDraftScope(operation.ExpectedDraftScopeRevision)
            && string.Equals(
                SelectedTargetLanguage.Value.Code,
                targetLanguage,
                StringComparison.Ordinal)
            && string.Equals(
                SelectedSourceLanguage.Value.Code,
                selectedSourceLanguage,
                StringComparison.Ordinal);

    // A run is worth picking up as soon as it has named a piece; see CanResumeSceneTranscription.
    private bool CanUseTranslationOperation(
        TranslationOperation operation,
        CaptionDocument document,
        string targetLanguage,
        string? selectedSourceLanguage,
        long draftScopeRevision)
        => (operation.CompletedBatchCount > 0
                || operation.RequestKey.HasOutstandingName.Value)
            && operation.CompletedBatchCount <= operation.Batches.Count
            && operation.ExpectedDraftScopeRevision == draftScopeRevision
            && (CaptionDocumentsEqual(operation.SourceDocument, document)
                || CaptionDocumentsEqual(
                    BuildTranslationDocument(operation, includeUntranslatedParts: true),
                    document))
            && string.Equals(operation.TargetLanguage, targetLanguage, StringComparison.Ordinal)
            && string.Equals(
                operation.SelectedSourceLanguage,
                selectedSourceLanguage,
                StringComparison.Ordinal);

    private static bool CanUseTranslationDraft(
        CaptionDraft draft,
        CaptionDocument document,
        string targetLanguage,
        string? selectedSourceLanguage)
        => draft.TranslationResume is { } resume
            && HoldsPaidWork(draft)
            && string.Equals(resume.TargetLanguage, targetLanguage, StringComparison.Ordinal)
            && string.Equals(
                resume.SelectedSourceLanguage,
                selectedSourceLanguage,
                StringComparison.Ordinal)
            && (StoredCuesEqual(resume.SourceCues, document.Cues)
                || StoredCuesEqual(draft.Cues, document.Cues));

    private TranslationOperation CreateTranslationOperation(
        CaptionDocument document,
        long captionRevision,
        string? sourceLanguage,
        string? selectedSourceLanguage,
        string targetLanguage,
        long draftScopeRevision)
    {
        var sourceDocument = new CaptionDocument(document.Cues.Select(cue => cue with { }));
        AiCaptionTranslationLimits limits = TranslationModelPicker.CaptionTranslationLimits;
        AiModelId? model = TranslationModelPicker.SelectedModel;
        return new TranslationOperation(
            sourceDocument,
            captionRevision,
            sourceLanguage,
            selectedSourceLanguage,
            targetLanguage,
            draftScopeRevision,
            limits,
            CaptionTranslationBatcher.CreateBatches(
                sourceDocument,
                sourceLanguage,
                targetLanguage,
                model,
                limits));
    }

    private static TranslationOperation RestoreTranslationOperation(
        CaptionTranslationResume resume,
        long captionRevision,
        long draftScopeRevision)
    {
        var sourceDocument = new CaptionDocument(RestoreCues(resume.SourceCues));
        AiCaptionTranslationLimits limits = TranslationLimitsOf(resume);
        AiModelId? model = ModelIdOrNull(resume.RequestKeyModel);
        List<TranslationBatch> batches = CaptionTranslationBatcher.CreateBatches(
            sourceDocument,
            resume.SourceLanguage,
            resume.TargetLanguage,
            model,
            limits);
        if (resume.CompletedBatchCount < 0 || resume.CompletedBatchCount > batches.Count)
            throw new InvalidDataException("The retained translation progress is invalid.");

        var operation = new TranslationOperation(
            sourceDocument,
            captionRevision,
            resume.SourceLanguage,
            resume.SelectedSourceLanguage,
            resume.TargetLanguage,
            draftScopeRevision,
            limits,
            batches,
            resume.RequestKeySeed,
            resume.RequestKeyNamePending)
        {
            CompletedBatchCount = resume.CompletedBatchCount,
            RequestKeyModel = resume.RequestKeyModel,
        };
        HashSet<string> completedIds = batches
            .Take(operation.CompletedBatchCount)
            .SelectMany(batch => batch.Pieces)
            .Select(piece => piece.Id)
            .ToHashSet(StringComparer.Ordinal);
        if (resume.TranslatedPieces.Count != completedIds.Count
            || resume.TranslatedPieces.Keys.Any(id => !completedIds.Contains(id)))
        {
            throw new InvalidDataException("The retained translated segments are invalid.");
        }
        AddTranslatedBatch(operation, resume.TranslatedPieces);
        return operation;
    }

    private static AiCaptionTranslationLimits TranslationLimitsOf(
        CaptionTranslationResume resume)
        => resume.MaxSegments > 0
            && resume.MaxCharacters > 0
            && resume.MaxRequestBytes > 0
                ? new AiCaptionTranslationLimits(
                    resume.MaxSegments,
                    resume.MaxCharacters,
                    resume.MaxRequestBytes)
                : AiCaptionTranslationLimits.Default;

    private static bool CaptionDocumentsEqual(CaptionDocument left, CaptionDocument right)
        => StoredCuesEqual(StoreCues(left.Cues), right.Cues);

    private static bool StoredCuesEqual(
        IReadOnlyList<StoredCaptionCue> stored,
        IReadOnlyList<CaptionCue> current)
    {
        if (stored.Count != current.Count)
            return false;

        for (int index = 0; index < stored.Count; index++)
        {
            StoredCaptionCue expected = stored[index];
            CaptionCue actual = current[index];
            if (expected.StartTicks != actual.Start.Ticks
                || expected.EndTicks != actual.End.Ticks
                || expected.Text != actual.Text
                || expected.Speaker != actual.Speaker
                || expected.Language != actual.Language
                || expected.Metadata.Count != actual.Metadata.Count
                || expected.Metadata.Any(pair =>
                    !actual.Metadata.TryGetValue(pair.Key, out string? value)
                    || value != pair.Value))
            {
                return false;
            }
        }
        return true;
    }

    private static void AddTranslatedBatch(
        TranslationOperation operation,
        IReadOnlyDictionary<string, string> translatedBatch)
    {
        foreach ((string id, string text) in translatedBatch)
        {
            operation.TranslatedPieces.Add(id, text);
        }
    }

    private static Dictionary<string, string> ValidateTranslatedBatch(
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

        return responseById;
    }

    private CaptionDocument BuildTranslationDocument(
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

            // Translation providers are not required to honor the editor's
            // display limits. Normalize each completed cue here so importing
            // a translation does not immediately surface a line warning.
            string translatedText = string.Concat(cuePieces.Select(piece =>
                operation.TranslatedPieces.TryGetValue(piece.Id, out string? translated)
                    ? translated
                    : piece.Text));
            if (!fullyTranslated)
            {
                translatedCues.Add(cue with { Text = translatedText });
                continue;
            }

            CaptionTextConstraints constraints = CreateTextConstraints();
            string wrappedText = CaptionTextWrapper.Wrap(translatedText, constraints);
            string[] lines = wrappedText.Split('\n');
            if (lines.Length > constraints.MaximumLineCount
                && (translatedText.Contains('\n') || translatedText.Contains('\r')))
            {
                // A provider may emit presentation line breaks of its own.
                // Collapse those only when they would exceed the editor's
                // line-count limit; the wrapper then lays the text out using
                // the user's configured limits.
                string flattened = translatedText
                    .Replace("\r\n", "\n", StringComparison.Ordinal)
                    .Replace('\r', '\n')
                    .Replace('\n', ' ');
                wrappedText = CaptionTextWrapper.Wrap(flattened, constraints);
                lines = wrappedText.Split('\n');
            }
            int cueCount = (lines.Length + constraints.MaximumLineCount - 1)
                / constraints.MaximumLineCount;
            long durationTicks = (cue.End - cue.Start).Ticks;
            // A valid cue normally has enough duration for every split. Keep
            // the original cue as a last resort when the interval is shorter
            // than the number of split cues rather than creating invalid
            // timing or losing text.
            if (cueCount <= 1 || durationTicks < cueCount)
            {
                translatedCues.Add(cue with
                {
                    Text = wrappedText,
                    Language = operation.TargetLanguage,
                });
                continue;
            }

            long baseDuration = durationTicks / cueCount;
            long remainder = durationTicks % cueCount;
            long elapsed = 0;
            for (int splitIndex = 0; splitIndex < cueCount; splitIndex++)
            {
                int firstLine = splitIndex * constraints.MaximumLineCount;
                int lineCount = Math.Min(constraints.MaximumLineCount, lines.Length - firstLine);
                long splitDuration = baseDuration + (splitIndex < remainder ? 1 : 0);
                TimeSpan splitStart = cue.Start + TimeSpan.FromTicks(elapsed);
                elapsed += splitDuration;
                TimeSpan splitEnd = cue.Start + TimeSpan.FromTicks(elapsed);
                translatedCues.Add(cue with
                {
                    Start = splitStart,
                    End = splitEnd,
                    Text = string.Join('\n', lines, firstLine, lineCount),
                    Language = operation.TargetLanguage,
                });
            }
        }

        return new CaptionDocument(translatedCues);
    }

    private CaptionDraftOutcome PublishTranslationPartial(TranslationOperation operation)
    {
        if (_disposed || !IsCurrentCaptionDraftScope(operation.ExpectedDraftScopeRevision))
            return CaptionDraftOutcome.Superseded;

        _pendingTranslation = operation;
        if (SetPartialResult(new RecoverableCaptionResult(
            BuildTranslationDocument(operation, includeUntranslatedParts: true),
            operation.TargetLanguage,
            null,
            PartialResultKind.Translation,
            operation.CompletedBatchCount,
            operation.Batches.Count,
            operation.ExpectedDraftScopeRevision)) is var outcome
            and not CaptionDraftOutcome.Recorded)
        {
            return outcome;
        }
        ShowRunProgress(
            operation.CompletedBatchCount,
            operation.Batches.Count,
            Strings.AiSubtitle_PartialTranslationAvailable);
        RefreshTranslationEstimate();
        return CaptionDraftOutcome.Recorded;
    }

    // Names the remainder of the run anew; the partial that has been paid for is still good.
    private void RetireTranslationRunName()
    {
        if (_pendingTranslation?.RequestKey.Retire() == true)
        {
            UpdateOutstandingCaptionRequest();
            PublishTranslationPartial(_pendingTranslation);
        }
    }

    private void WithdrawTranslationName(
        TranslationOperation operation,
        AiRequestName name)
    {
        if (!operation.RequestKey.WithdrawAfterNoReservation(name))
            return;
        UpdateOutstandingCaptionRequest();
        PublishTranslationPartial(operation);
    }

    private void RefreshTranslationEstimate()
    {
        if (_disposed)
            return;

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
            AiModelId? runModel = ModelOfRun(
                operation.CompletedBatchCount > 0
                    || operation.RequestKey.HasOutstandingName.Value,
                operation.RequestKeyModel,
                TranslationModelPicker.SelectedModel);
            request = CreateTranslationAvailabilityRequest(
                operation.Batches[operation.CompletedBatchCount],
                runModel,
                limits: operation.Limits);
        }
        else if (TryBuildCaptionDocumentCore(out CaptionDocument? document, out _)
                 && document is { Count: > 0 }
                 && SelectedTargetLanguage.Value.Code is { } targetLanguage)
        {
            AiCaptionTranslationLimits limits =
                TranslationModelPicker.CaptionTranslationLimits;
            request = CaptionTranslationBatcher.CreateBatches(
                    document,
                    SelectedSourceLanguage.Value.Code ?? _lastCaptionLanguage,
                    targetLanguage,
                    TranslationModelPicker.SelectedModel,
                    limits)
                .FirstOrDefault() is { } batch
                ? CreateTranslationAvailabilityRequest(batch, TranslationModelPicker.SelectedModel, limits)
                : null;
        }

        _translationAvailability.Check(request);
    }

    // The line the model has just translated, shown while the rest is still
    // being worked on. A preview only: the cues take the batch the server returns, after validation.
    private void ShowTranslatedLine(AiCaptionTranslationSegment segment)
    {
        if (_disposed || string.IsNullOrEmpty(segment.Text))
            return;

        TranslationPreview.Value = segment.Text;
    }

    private static AiOperationAvailabilityRequest.Translation CreateTranslationAvailabilityRequest(
        TranslationBatch batch,
        AiModelId? model,
        AiCaptionTranslationLimits limits)
        // What the batch will actually be sent to. A run in progress is priced
        // against the model its names were built from, not against whichever the
        // picker is showing now.
        => new(
            AiOperations.CaptionTranslation,
            batch.Pieces.Sum(piece => piece.Text.Length),
            model,
            limits);

    private sealed class TranslationOperation(
        CaptionDocument sourceDocument,
        long expectedCaptionRevision,
        string? sourceLanguage,
        string? selectedSourceLanguage,
        string targetLanguage,
        long expectedDraftScopeRevision,
        AiCaptionTranslationLimits limits,
        IReadOnlyList<TranslationBatch> batches,
        string? requestKeySeed = null,
        bool requestKeyNamePending = false)
        : CaptionRunOperation(expectedCaptionRevision, expectedDraftScopeRevision, requestKeySeed, requestKeyNamePending)
    {
        public CaptionDocument SourceDocument { get; } = sourceDocument;

        public string? SourceLanguage { get; } = sourceLanguage;

        public string? SelectedSourceLanguage { get; } = selectedSourceLanguage;

        public string TargetLanguage { get; } = targetLanguage;

        public AiCaptionTranslationLimits Limits { get; } = limits;

        public IReadOnlyList<TranslationBatch> Batches { get; } = batches;

        // A batch is named for the languages as well.
        protected override AiRequestName NameFor(int index, string? model)
            => RequestKey.NameFor(index, model, TargetLanguage, SourceLanguage);

        public Dictionary<string, string> TranslatedPieces { get; } = new(StringComparer.Ordinal);

        public int CompletedBatchCount { get; set; }
    }
}
