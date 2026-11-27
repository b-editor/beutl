using System.Text;
using Beutl.Editor.Components.AiWorkspaceTab.Services;
using Beutl.NodeGraph.Generative;
using Beutl.Serialization;

namespace Beutl.Services.AI;

internal static class AgentGenerationRecovery
{
    public static bool MatchesOrSave(string recoveryStorePath, string requestSeed, string operation, string?[] parts)
        // Use the resolved inputs that name the service request. A changed resolution must never
        // send the same caller action under a different idempotency key.
        => MatchesOrSaveFile(GetPath(recoveryStorePath, requestSeed, ".fingerprint"),
            GenerativeFingerprint.Combine([operation, .. parts]));

    public static void SaveServiceKey(string recoveryStorePath, string requestSeed, string serviceKey)
    {
        if (!MatchesOrSaveFile(GetPath(recoveryStorePath, requestSeed, ".key"), serviceKey))
            throw new InvalidDataException("The service key for this generation changed.");
    }

    public static AiPendingAttempt? FindAttempt(AiRequestRecoveryContext context, string accountId, string operation, string requestSeed)
    {
        string path = GetPath(context.Store.StoragePath, requestSeed, ".key");
        string? serviceKey = File.Exists(path) ? File.ReadAllText(path) : null;
        AiPendingAttempt? pending = context.Store.PendingFor(accountId, operation).FirstOrDefault(attempt =>
            attempt.Operation == operation && (serviceKey is not null
                ? attempt.Key == serviceKey
                : attempt.Fingerprint.StartsWith(requestSeed + "-", StringComparison.Ordinal)));
        return pending ?? (serviceKey is null ? null : new AiPendingAttempt(accountId, operation, requestSeed, serviceKey));
    }

    private static string GetPath(string recoveryStorePath, string requestSeed, string extension)
        => Path.Combine(Path.GetDirectoryName(recoveryStorePath)!, "agent-generations",
            GenerativeFingerprint.Combine([requestSeed]) + extension);

    private static bool MatchesOrSaveFile(string path, string value)
    {
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temporary = path + $".{Guid.NewGuid():N}.tmp";
            try
            {
                using (FileStream stream = StorageWriteTransaction.CreateTemporaryFile(temporary, path, privateContents: true))
                {
                    stream.Write(Encoding.UTF8.GetBytes(value));
                    stream.Flush(flushToDisk: true);
                }

                StorageWriteTransaction.MoveIntoPlace(temporary, path, overwrite: false);
            }
            catch (IOException) when (File.Exists(path))
            {
                // Another app instance recorded this key first.
            }
            finally
            {
                File.Delete(temporary);
            }
        }

        return File.ReadAllText(path) == value;
    }
}
