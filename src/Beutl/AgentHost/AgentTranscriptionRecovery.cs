using System.Text.Json;
using Beutl.AgentToolkit.Common;
using Beutl.NodeGraph.Generative;
using Beutl.Serialization;

namespace Beutl.AgentHost;

internal sealed record AgentTranscriptionCheckpoint(
    string SourceFingerprint,
    string? ModelId,
    int CompletedParts,
    long NextSample,
    string? PendingPartKey,
    bool IsComplete,
    AgentTranscript Transcript);

/// <summary>Stores a keyed transcription under the account that paid for it.</summary>
internal sealed class AgentTranscriptionRecovery : IDisposable
{
    private readonly string _path;
    private readonly FileStream _lease;

    public AgentTranscriptionRecovery(string recoveryStorePath, string accountId, string requestKey)
    {
        _path = CheckpointPath(recoveryStorePath, accountId, requestKey);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

        // Keep the lease until the job exits so two app instances cannot resume one checkpoint at once.
        try
        {
            _lease = File.Open(Path.ChangeExtension(_path, ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            throw new AgentAiException(ErrorCode.AiGenerationFailed,
                "This transcription's saved state is in use or unavailable. Retry with the same requestKey.");
        }
    }

    public static bool Exists(string recoveryStorePath, string accountId, string requestKey)
        => File.Exists(CheckpointPath(recoveryStorePath, accountId, requestKey));

    private static string CheckpointPath(string recoveryStorePath, string accountId, string requestKey)
        => Path.Combine(Path.GetDirectoryName(recoveryStorePath)!, "agent-transcriptions",
            GenerativeFingerprint.Combine([accountId, requestKey]) + ".json");

    public async Task<AgentTranscriptionCheckpoint?> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(_path);
            return await JsonSerializer.DeserializeAsync<AgentTranscriptionCheckpoint>(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false) ?? throw new InvalidDataException("The transcription checkpoint is empty.");
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    public async Task SaveAsync(AgentTranscriptionCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        string temporary = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (FileStream stream = StorageWriteTransaction.CreateTemporaryFile(temporary, _path, privateContents: true))
            {
                await JsonSerializer.SerializeAsync(stream, checkpoint, cancellationToken: cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            StorageWriteTransaction.MoveIntoPlace(temporary, _path, overwrite: true);
        }
        finally
        {
            AgentHostAiBackend.TryDelete(temporary);
        }
    }

    public void Dispose() => _lease.Dispose();
}
