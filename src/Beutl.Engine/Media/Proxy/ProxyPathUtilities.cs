namespace Beutl.Media.Proxy;

internal static class ProxyPathUtilities
{
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
}
