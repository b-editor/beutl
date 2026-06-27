namespace Beutl.Media.Proxy;

public readonly record struct ProxyFingerprint
{
    public ProxyFingerprint(string absolutePath, long fileSizeBytes, DateTime mtimeUtc)
    {
        if (string.IsNullOrWhiteSpace(absolutePath))
            throw new ArgumentException("Path must be non-empty.", nameof(absolutePath));

        string fullPath = NormalizeAbsolutePath(absolutePath);
        if (!Path.IsPathFullyQualified(fullPath))
            throw new ArgumentException("Path must be absolute.", nameof(absolutePath));

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(fileSizeBytes, 0);

        if (mtimeUtc.Kind != DateTimeKind.Utc)
        {
            mtimeUtc = mtimeUtc.ToUniversalTime();
        }

        AbsolutePath = fullPath;
        FileSizeBytes = fileSizeBytes;
        MtimeUtc = mtimeUtc;
    }

    public string AbsolutePath { get; init; }

    public long FileSizeBytes { get; init; }

    public DateTime MtimeUtc { get; init; }

    public static ProxyFingerprint FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string resolved = ResolveFinalPath(path);
        var info = new FileInfo(resolved);
        if (!info.Exists)
            throw new FileNotFoundException(null, path);

        return new ProxyFingerprint(info.FullName, info.Length, info.LastWriteTimeUtc);
    }

    public static bool TryFromFile(string path, out ProxyFingerprint fingerprint)
    {
        try
        {
            fingerprint = FromFile(path);
            return true;
        }
        catch
        {
            fingerprint = default;
            return false;
        }
    }

    internal static string NormalizeAbsolutePath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            fullPath = fullPath.ToUpperInvariant();
        }

        return fullPath;
    }

    private static string ResolveFinalPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        FileSystemInfo? target = info.ResolveLinkTarget(returnFinalTarget: true);
        return target?.FullName ?? info.FullName;
    }
}
