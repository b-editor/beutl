using System.Text;
using Beutl.NodeGraph.Generative;
using Beutl.Serialization;

namespace Beutl.Services.AI;

internal static class AgentGenerationRecovery
{
    public static bool MatchesOrSave(string recoveryStorePath, string requestSeed, string operation, string?[] parts)
    {
        string directory = Path.Combine(Path.GetDirectoryName(recoveryStorePath)!, "agent-generations");
        string path = Path.Combine(directory, GenerativeFingerprint.Combine([requestSeed]) + ".fingerprint");
        // Use the resolved inputs that name the service request. A changed resolution must never
        // send the same caller action under a different idempotency key.
        string fingerprint = GenerativeFingerprint.Combine([operation, .. parts]);
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(directory);
            string temporary = path + $".{Guid.NewGuid():N}.tmp";
            try
            {
                using (FileStream stream = StorageWriteTransaction.CreateTemporaryFile(temporary, path, privateContents: true))
                {
                    stream.Write(Encoding.UTF8.GetBytes(fingerprint));
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

        return File.ReadAllText(path) == fingerprint;
    }
}
