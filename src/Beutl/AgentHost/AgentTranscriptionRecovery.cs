using System.Text.Json;
using Beutl.AgentToolkit.Common;
using Beutl.Editor.Components.AiWorkspaceTab.Services;
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
    AgentTranscript? Transcript);

/// <summary>Stores a keyed transcription under the account that paid for it.</summary>
internal sealed class AgentTranscriptionRecovery : IDisposable
{
    private const string CompletedExtension = ".completed";
    private readonly string _path;
    private readonly string _completedPath;
    private readonly FileStream _lease;

    public AgentTranscriptionRecovery(string recoveryStorePath, string accountId, string requestKey)
    {
        _path = CheckpointPath(recoveryStorePath, accountId, requestKey);
        _completedPath = Path.ChangeExtension(_path, CompletedExtension);
        string directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        PruneExpiredResults(directory);

        // Keep the lease until the job exits so two app instances cannot resume one checkpoint at once.
        try
        {
            _lease = OpenLease(_path);
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
        AgentTranscriptionCheckpoint? checkpoint = await ReadAsync<AgentTranscriptionCheckpoint>(_path, cancellationToken)
            .ConfigureAwait(false);
        if (checkpoint is { IsComplete: true, Transcript: null }
            && File.GetLastWriteTimeUtc(_completedPath) > DateTime.UtcNow - AiTemporaryFileStore.StaleAge)
        {
            checkpoint = checkpoint with
            {
                Transcript = await ReadAsync<AgentTranscript>(_completedPath, cancellationToken).ConfigureAwait(false),
            };
        }

        return checkpoint;
    }

    private static async Task<T?> ReadAsync<T>(string path, CancellationToken cancellationToken) where T : class
    {
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false) ?? throw new InvalidDataException("The saved transcription is empty.");
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    public async Task SaveAsync(AgentTranscriptionCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        if (checkpoint is { IsComplete: true, Transcript: { } result })
        {
            // Save the result before marking the key complete. The smaller checkpoint survives
            // expiry so replaying a spent key cannot submit the audio again.
            await WriteAsync(_completedPath, result, cancellationToken).ConfigureAwait(false);
            checkpoint = checkpoint with { Transcript = null };
        }

        await WriteAsync(_path, checkpoint, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        string temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (FileStream stream = StorageWriteTransaction.CreateTemporaryFile(temporary, path, privateContents: true))
            {
                await JsonSerializer.SerializeAsync(stream, value, cancellationToken: cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            StorageWriteTransaction.MoveIntoPlace(temporary, path, overwrite: true);
        }
        finally
        {
            AgentHostAiBackend.TryDelete(temporary);
        }
    }

    private static FileStream OpenLease(string path)
        => File.Open(Path.ChangeExtension(path, ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    private static void PruneExpiredResults(string directory)
    {
        DateTime cutoff = DateTime.UtcNow - AiTemporaryFileStore.StaleAge;
        foreach (string path in Directory.EnumerateFiles(directory, "*" + CompletedExtension))
        {
            if (File.GetLastWriteTimeUtc(path) > cutoff)
                continue;

            try
            {
                using FileStream lease = OpenLease(path);
                // The previous writer may have replaced the file before we acquired its lease.
                if (File.GetLastWriteTimeUtc(path) <= cutoff)
                    AgentHostAiBackend.TryDelete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A result in another app's use can be pruned on a later call.
            }
        }
    }

    public void Dispose() => _lease.Dispose();
}
