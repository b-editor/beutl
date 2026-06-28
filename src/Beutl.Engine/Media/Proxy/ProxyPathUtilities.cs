using System.Security.Cryptography;

namespace Beutl.Media.Proxy;

internal static class ProxyPathUtilities
{
    public static string BuildRelativePath(ProxyFingerprint fingerprint, ProxyPreset preset)
    {
        string key = $"{fingerprint.AbsolutePath}|{fingerprint.FileSizeBytes}|{fingerprint.MtimeUtc:O}";
        byte[] hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key));
        string dir = Convert.ToHexString(hash).ToLowerInvariant();
        return $"{dir}/{preset.ToString().ToLowerInvariant()}.mp4";
    }

    public static string ResolveRelativePath(string storeRootPath, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeRootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        if (IsRootedProxyPath(relativePath))
            throw new ArgumentException("Proxy path must be relative.", nameof(relativePath));

        string root = Path.GetFullPath(storeRootPath);
        string candidate = Path.GetFullPath(Path.Combine(
            root,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));

        if (!IsUnderDirectory(candidate, root))
            throw new ArgumentException("Proxy path must stay inside the store root.", nameof(relativePath));

        return candidate;
    }

    public static bool TryResolveRelativePath(string storeRootPath, string relativePath, out string fullPath)
    {
        try
        {
            fullPath = ResolveRelativePath(storeRootPath, relativePath);
            return true;
        }
        catch
        {
            fullPath = string.Empty;
            return false;
        }
    }

    public static bool IsGeneratedProxyTempPath(string storeRootPath, string path)
    {
        string fullPath = Path.GetFullPath(path);
        string root = Path.GetFullPath(storeRootPath);
        if (!IsUnderDirectory(fullPath, root))
            return false;

        string? directory = Path.GetDirectoryName(fullPath);
        string? hashDirectory = directory == null ? null : Path.GetFileName(directory);
        if (hashDirectory is not { Length: 64 } || !hashDirectory.All(IsLowerHex))
            return false;

        string fileName = Path.GetFileName(fullPath);
        if (!fileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
            return false;

        string withoutExtension = Path.GetFileNameWithoutExtension(fileName);
        string[] parts = withoutExtension.Split('.');
        if (parts.Length != 3 || !parts[2].Equals("tmp", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!Guid.TryParseExact(parts[1], "N", out _))
            return false;

        return Enum.GetNames<ProxyPreset>()
            .Any(name => parts[0].Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsRootedProxyPath(string path)
    {
        return Path.IsPathRooted(path)
            || path.Contains('\\')
            || path.StartsWith('/')
            || path.StartsWith('\\')
            || (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':');
    }

    private static bool IsUnderDirectory(string candidate, string root)
    {
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string normalizedCandidate = Path.GetFullPath(candidate);
        string rootWithSeparator = normalizedRoot + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return normalizedCandidate.StartsWith(rootWithSeparator, comparison);
    }

    private static bool IsLowerHex(char c)
        => c is >= '0' and <= '9' or >= 'a' and <= 'f';
}
