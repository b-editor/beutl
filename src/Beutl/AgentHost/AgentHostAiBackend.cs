using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Media.Decoding;
using Beutl.NodeGraph.Generative;
using Beutl.ProjectSystem;
using Beutl.Services.AI;

namespace Beutl.AgentHost;

/// <summary>The signed-in application's AI services, for the agent host's AI tools.</summary>
internal sealed class AgentHostAiBackend(
    Func<AuthenticatedUser?> user,
    IAiEntitlementService entitlements,
    IAiOperationAvailabilityService availability,
    IAiTranscriptionService transcription,
    Func<IGenerativeModelCatalog> catalog,
    Func<Scene, IGenerativeNodeExecutor> executor) : IAgentAiBackend
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

    public IGenerativeNodeExecutor CreateExecutor(Scene scene) => executor(scene);

    public async Task<AgentTranscript> TranscribeAsync(
        string path,
        string? language,
        string? modelId,
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        AiModelId? model = string.IsNullOrWhiteSpace(modelId) ? null : new AiModelId(modelId.Trim());
        using MediaReader reader = await Task.Run(
            () => MediaReader.Open(path, new MediaOptions(MediaMode.Audio) { PreferProxy = false }),
            cancellationToken).ConfigureAwait(false);
        if (!reader.HasAudio)
            throw new AgentAiException(Beutl.AgentToolkit.Common.ErrorCode.MediaUnsupported, "The file has no audio to transcribe.");

        int sampleRate = reader.AudioInfo.SampleRate;
        long totalSamples = (long)Math.Floor(reader.AudioInfo.Duration.ToDouble() * sampleRate);
        int chunkSamples = checked((int)(s_chunkDuration.TotalSeconds * sampleRate));
        int chunkCount = Math.Max(1, (int)Math.Ceiling(totalSamples / (double)chunkSamples));
        var segments = new List<AgentTranscriptSegment>();
        var words = new List<AgentTranscriptWord>();
        bool hasWords = false;
        string? detected = null;
        for (int index = 0; index < chunkCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long start = (long)index * chunkSamples;
            int length = checked((int)Math.Min(chunkSamples, Math.Max(1, totalSamples - start)));
            progress.Report(chunkCount == 1 ? "Transcribing" : $"Transcribing part {index + 1} of {chunkCount}");
            (string wave, FileStream stream) = AiTemporaryFileStore.Create("audio", "agent", ".wav");
            try
            {
                SpeechWaveChunkResult chunk;
                using (stream)
                {
                    chunk = await Task.Run(
                        () => SpeechWaveEncoder.WriteSpeechWave(reader, checked((int)start), length, stream, cancellationToken),
                        cancellationToken).ConfigureAwait(false);
                }

                if (chunk.SourceSampleCount <= 0)
                    break;

                if (!await availability.CheckAsync(
                        new AiOperationAvailabilityRequest.Transcription(
                            AiOperations.Transcription,
                            chunk.UploadedDuration.TotalSeconds,
                            model),
                        cancellationToken).ConfigureAwait(false))
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
                    throw new AgentAiException(Beutl.AgentToolkit.Common.ErrorCode.AiGenerationFailed, failure.Message);
                }

                double offset = start / (double)sampleRate;
                detected ??= response.Language;
                foreach (AiTranscriptionSegment segment in response.Segments)
                    segments.Add(new AgentTranscriptSegment(segment.Start + offset, segment.End + offset, segment.Text));
                if (response.Words is { } chunkWords)
                {
                    hasWords = true;
                    foreach (AiTranscriptionWord word in chunkWords)
                        words.Add(new AgentTranscriptWord(word.Start + offset, word.End + offset, word.Word));
                }

                if (chunk.SourceSampleCount < length)
                    break;
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
