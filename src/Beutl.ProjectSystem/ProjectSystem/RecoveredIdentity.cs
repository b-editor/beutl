using System.Security.Cryptography;
using System.Text;

namespace Beutl.ProjectSystem;

// Deterministic Ids for recovered elements and their descendants (UUIDv5 over scene-relative paths), and
// the formats of the keys those Ids are persisted under in the scene's recovery metadata.
internal static class RecoveredIdentity
{
    private const int MaxRecoveredIdCollisionAttempts = 1024;
    private static readonly Guid s_recoveredElementNamespace = new("dfad2f76-1d04-5593-ae3b-f371fb1f42ee");

    public static Guid DeriveElementId(string relativePath)
    {
        return CreateVersion5Guid(s_recoveredElementNamespace, relativePath);
    }

    public static Guid ClaimRecoveredElementId(string relativePath, ISet<Guid> claimedIds)
    {
        return ClaimDeterministicId(relativePath, claimedIds, "element", relativePath);
    }

    public static Guid ClaimRecoveredDescendantId(
        string relativePath,
        string remapKey,
        ISet<Guid> claimedIds)
    {
        return ClaimDeterministicId(remapKey, claimedIds, "descendant", relativePath);
    }

    private static Guid ClaimDeterministicId(
        string baseName,
        ISet<Guid> claimedIds,
        string kind,
        string relativePath)
    {
        for (int attempt = 0; attempt < MaxRecoveredIdCollisionAttempts; attempt++)
        {
            string candidateName = attempt == 0
                ? baseName
                : $"{baseName}#{attempt}";
            Guid candidate = CreateVersion5Guid(s_recoveredElementNamespace, candidateName);
            if (claimedIds.Add(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            $"Could not assign a unique recovered {kind} Id for '{relativePath}'.");
    }

    public static bool TryGetRecoveredDescendantPositionalIdentity(
        IReadOnlyDictionary<string, Guid> identities,
        string relativePath,
        string positionalPath,
        out Guid identityId,
        out bool ambiguous)
    {
        string keyPrefix = $"{relativePath}!path:";
        string normalizedPath = SerializedGraphPaths.NormalizeSerializedGraphPositionalPath(positionalPath);
        var candidates = new HashSet<Guid>();
        foreach ((string key, Guid id) in identities)
        {
            if (key.StartsWith(keyPrefix, StringComparison.Ordinal)
                && SerializedGraphPaths.NormalizeSerializedGraphPositionalPath(key[keyPrefix.Length..]) == normalizedPath)
            {
                candidates.Add(id);
            }
        }

        ambiguous = candidates.Count > 1;
        if (candidates.Count == 1)
        {
            identityId = candidates.Single();
            return true;
        }

        identityId = Guid.Empty;
        return false;
    }

    public static string CreateRecoveredDescendantKey(string relativePath, Guid originalId, int occurrence)
    {
        return $"{relativePath}!{originalId:D}#{occurrence}";
    }

    public static string CreateRecoveredDescendantIdentityKey(string relativePath, string graphPath)
    {
        return $"{relativePath}!path:{graphPath}";
    }

    public static string CreateLegacyRecoveredDescendantIdentityKey(string relativePath, int index)
    {
        return $"{relativePath}!@{index}";
    }

    public static string NormalizeRelativePath(string path)
    {
        return path.Replace('\\', '/');
    }

    private static Guid CreateVersion5Guid(Guid namespaceId, string name)
    {
        byte[] namespaceBytes = namespaceId.ToByteArray();
        SwapGuidByteOrder(namespaceBytes);
        byte[] nameBytes = Encoding.UTF8.GetBytes(name);
        byte[] source = new byte[namespaceBytes.Length + nameBytes.Length];
        namespaceBytes.CopyTo(source, 0);
        nameBytes.CopyTo(source, namespaceBytes.Length);

        byte[] hash = SHA1.HashData(source);
        hash[6] = (byte)((hash[6] & 0x0f) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
        Array.Resize(ref hash, 16);
        SwapGuidByteOrder(hash);
        return new Guid(hash);
    }

    private static void SwapGuidByteOrder(Span<byte> bytes)
    {
        (bytes[0], bytes[3]) = (bytes[3], bytes[0]);
        (bytes[1], bytes[2]) = (bytes[2], bytes[1]);
        (bytes[4], bytes[5]) = (bytes[5], bytes[4]);
        (bytes[6], bytes[7]) = (bytes[7], bytes[6]);
    }
}
