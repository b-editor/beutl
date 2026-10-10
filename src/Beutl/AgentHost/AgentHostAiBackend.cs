using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Media.Decoding;
using Beutl.NodeGraph.Generative;
using Beutl.ProjectSystem;
using Beutl.Services.AI;
using Beutl.ViewModels.Dialogs;

namespace Beutl.AgentHost;

/// <summary>The signed-in application's AI services, for the agent host's AI tools.</summary>
internal sealed class AgentHostAiBackend(
    Func<AuthenticatedUser?> user,
    IAiEntitlementService entitlements,
    IAiOperationAvailabilityService availability,
    IAiTranscriptionService transcription,
    Func<IGenerativeModelCatalog> catalog,
    Func<Scene, IGenerativeNodeExecutor> executor,
    IAiModelCatalogService modelCatalog) : IAgentAiBackend
{
    // The longest piece of audio sent at once: ten minutes of 16 kHz mono stays under the upload limit.
    private static readonly TimeSpan s_chunkDuration = TimeSpan.FromMinutes(10);

    public async Task<string?> GetUnavailableReasonAsync(CancellationToken cancellationToken)
    {
        if (user() is null)
            return "Sign in to Beutl in the running app to use AI generation.";

        AiEntitlements? current = entitlements.Entitlements.Value;
        if (current is null)
        {
            try
            {
                current = await entitlements.RefreshAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Offline or refused: the service gives its own reason when the request is sent.
                return null;
            }
        }

        return current is { CanUseAi: false }
            ? "AI generation requires the Beutl Pro plan. Choose a plan from the AI tab in the app."
            : null;
    }

    public Task<IReadOnlyList<GenerativeModelInfo>> GetModelsAsync(string operationId, CancellationToken cancellationToken)
        => catalog().GetModelsAsync(operationId, cancellationToken);

    // The catalog refreshes the entitlements when it loads; still null means the plan could not be read.
    public bool KnowsModelAvailability => entitlements.Entitlements.Value is not null;

    public IGenerativeNodeExecutor CreateExecutor(Scene scene) => executor(scene);

    public async Task<long> GetImageReferenceBudgetAsync(CancellationToken cancellationToken)
    {
        try
        {
            AiModelCatalog loaded = await modelCatalog.GetAsync(cancellationToken).ConfigureAwait(false);
            return loaded.GetImageReferenceLimits(AiOperations.ImageGeneration).MaxTotalBytes;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Unreachable: the executor checks again against whatever catalog it loads.
            return AiRequestLimits.MaxImageReferencesTotalBytes;
        }
    }

    public async Task<AgentTranscript> TranscribeAsync(
        string path,
        string? language,
        string? modelId,
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        AiModelId? model = string.IsNullOrWhiteSpace(modelId) ? null : new AiModelId(modelId.Trim());
        MediaReader opened;
        try
        {
            opened = await Task.Run(
                () => MediaReader.Open(path, new MediaOptions(MediaMode.Audio) { PreferProxy = false }),
                cancellationToken).ConfigureAwait(false);
        }
        catch (UnsupportedMediaException)
        {
            throw new AgentAiException(Beutl.AgentToolkit.Common.ErrorCode.MediaUnsupported, "The file could not be decoded as audio or video.");
        }

        using MediaReader reader = opened;
        if (!reader.HasAudio)
            throw new AgentAiException(Beutl.AgentToolkit.Common.ErrorCode.MediaUnsupported, "The file has no audio to transcribe.");

        int sampleRate = reader.AudioInfo.SampleRate;
        long totalSamples = (long)Math.Floor(reader.AudioInfo.Duration.ToDouble() * sampleRate);
        // Parts are read by an int sample position; refused here, before the first part is paid for.
        if (totalSamples > int.MaxValue)
        {
            throw new AgentAiException(
                Beutl.AgentToolkit.Common.ErrorCode.MediaUnsupported,
                $"The recording is too long to transcribe; at {sampleRate} Hz the limit is {int.MaxValue / (double)sampleRate / 3600:0.#} hours.");
        }

        int chunkSamples = checked((int)(s_chunkDuration.TotalSeconds * sampleRate));
        int estimatedParts = Math.Max(1, (int)Math.Ceiling(totalSamples / (double)chunkSamples));
        var segments = new List<AgentTranscriptSegment>();
        var words = new List<AgentTranscriptWord>();
        bool hasWords = false;
        string? detected = null;
        // Each part starts where the last one stopped reading. The encoder ends a part at a short
        // read, which a decoder may return mid-file, so a short part is not the end of the recording.
        long start = 0;
        int part = 0;
        while (start < totalSamples)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long partStart = start;
            int length = checked((int)Math.Min(chunkSamples, totalSamples - partStart));
            part++;
            progress.Report(estimatedParts == 1 && part == 1
                ? "Transcribing"
                : $"Transcribing part {part} of {Math.Max(part, estimatedParts)}");
            (string wave, FileStream stream) = AiTemporaryFileStore.Create("audio", "agent", ".wav");
            try
            {
                SpeechWaveChunkResult chunk;
                try
                {
                    using (stream)
                    {
                        chunk = await Task.Run(
                            () => SpeechWaveEncoder.WriteSpeechWave(reader, checked((int)partStart), length, stream, cancellationToken),
                            cancellationToken).ConfigureAwait(false);
                    }
                }
                // The file opened but its audio could not be read: the input's fault, as for a file
                // that does not open, not a service failure worth retrying. An IOException is left
                // out: writing the temporary WAV (a full disk) raises it too, and it is not the
                // recording's fault, so it takes the logged path for unexpected failures.
                catch (Exception ex) when (ex is SubtitleInputException or InvalidDataException)
                {
                    // Nothing more to read after earlier parts: some files report a length past their
                    // last sample, and the parts already paid for are the whole transcript.
                    if (partStart > 0 && ex is SubtitleInputException)
                        break;
                    throw new AgentAiException(
                        Beutl.AgentToolkit.Common.ErrorCode.MediaUnsupported,
                        $"The audio could not be decoded from {TimeSpan.FromSeconds(partStart / (double)sampleRate):hh\\:mm\\:ss} on.");
                }

                if (chunk.SourceSampleCount <= 0)
                    break;

                bool covered;
                try
                {
                    covered = await availability.CheckAsync(
                        new AiOperationAvailabilityRequest.Transcription(
                            AiOperations.Transcription,
                            chunk.UploadedDuration.TotalSeconds,
                            model),
                        cancellationToken).ConfigureAwait(false);
                }
                // The check itself can refuse, when the session or credits changed since the call.
                catch (Exception ex) when (AiRequestFailure.Classify(ex) is { } failure)
                {
                    throw new AgentAiException(AgentAiException.CodeFor(ex), failure.Message);
                }

                if (!covered)
                {
                    throw new AgentAiException(
                        Beutl.AgentToolkit.Common.ErrorCode.AiUnavailable,
                        "The account's AI usage does not cover this transcription.");
                }

                AiTranscriptionResponse response;
                try
                {
                    response = await transcription.TranscribeAsync(
                        new AiTranscriptionRequest(
                            AiUploadSource.FromFile(wave, "audio.wav"),
                            language,
                            model,
                            Guid.NewGuid().ToString("N")),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (AiRequestFailure.Classify(ex) is { } failure)
                {
                    throw new AgentAiException(AgentAiException.CodeFor(ex), failure.Message);
                }

                // Checked as the subtitle flow checks a part before accepting it: times inside the part,
                // in order, so offsetting them cannot overlap the next part.
                double partSeconds = chunk.UploadedDuration.TotalSeconds;
                AiTranscriptionSegment[] partSegments;
                try
                {
                    partSegments = AiSubtitleDialogViewModel.ValidateTranscriptionSegments(response.Segments, partSeconds);
                }
                catch (InvalidDataException)
                {
                    throw new AgentAiException(
                        Beutl.AgentToolkit.Common.ErrorCode.AiGenerationFailed,
                        "The transcription service returned timings outside the audio it was sent.");
                }

                double offset = partStart / (double)sampleRate;
                detected ??= response.Language;
                foreach (AiTranscriptionSegment segment in partSegments)
                    segments.Add(new AgentTranscriptSegment(segment.Start + offset, segment.End + offset, segment.Text));
                if (response.Words is { } chunkWords)
                {
                    hasWords = true;
                    // The segments' tolerance: providers round word times to a few hundredths.
                    const double ToleranceSeconds = 0.05;
                    double previousEnd = 0;
                    foreach (AiTranscriptionWord word in chunkWords)
                    {
                        // Words only refine the segments, so one with unusable times, out of order
                        // or overlapping the word before it, is left out rather than the whole part.
                        if (word is null
                            || string.IsNullOrWhiteSpace(word.Word)
                            || !double.IsFinite(word.Start)
                            || !double.IsFinite(word.End)
                            || word.Start < 0
                            || word.End < word.Start
                            || word.Start > partSeconds
                            || word.Start < previousEnd - ToleranceSeconds)
                        {
                            continue;
                        }

                        double end = Math.Min(word.End, partSeconds);
                        words.Add(new AgentTranscriptWord(word.Start + offset, end + offset, word.Word));
                        previousEnd = end;
                    }
                }

                start = partStart + chunk.SourceSampleCount;
            }
            finally
            {
                TryDelete(wave);
            }
        }

        return new AgentTranscript(detected ?? language, segments, hasWords ? words : null);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
