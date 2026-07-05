namespace Beutl.Media.Proxy;

public readonly record struct ProxyFingerprint
{
    private readonly string? _sourcePath;

    public ProxyFingerprint(string absolutePath, long fileSizeBytes, DateTime mtimeUtc)
    {
        if (string.IsNullOrWhiteSpace(absolutePath))
            throw new ArgumentException("Path must be non-empty.", nameof(absolutePath));

        string originalPath = Path.GetFullPath(absolutePath);
        if (!Path.IsPathFullyQualified(originalPath))
            throw new ArgumentException("Path must be absolute.", nameof(absolutePath));

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(fileSizeBytes, 0);

        if (mtimeUtc.Kind != DateTimeKind.Utc)
        {
            mtimeUtc = mtimeUtc.ToUniversalTime();
        }

        AbsolutePath = NormalizeAbsolutePath(originalPath);
        _sourcePath = originalPath;
        FileSizeBytes = fileSizeBytes;
        MtimeUtc = mtimeUtc;
    }

    // Case-folded on Windows/macOS. This is the identity/dedup key (equality, hashing, hash-directory
    // naming) — it is NOT safe for filesystem I/O on a case-sensitive volume; use SourcePath for that.
    public string AbsolutePath { get; init; }

    // The original, case-preserving absolute path. Use this — not AbsolutePath — whenever opening or
    // stat-ing the source media; on a case-sensitive volume the folded AbsolutePath will not resolve.
    // Falls back to AbsolutePath for entries persisted before this field existed.
    public string SourcePath
    {
        get => string.IsNullOrEmpty(_sourcePath) ? AbsolutePath : _sourcePath;
        init => _sourcePath = value;
    }

    public long FileSizeBytes { get; init; }

    public DateTime MtimeUtc { get; init; }

    // Equality and hashing intentionally exclude SourcePath: two fingerprints for the same file must
    // compare equal regardless of the casing recorded for I/O, and the folded AbsolutePath is the key.
    public bool Equals(ProxyFingerprint other)
        => string.Equals(AbsolutePath, other.AbsolutePath, StringComparison.Ordinal)
            && FileSizeBytes == other.FileSizeBytes
            && MtimeUtc == other.MtimeUtc;

    public override int GetHashCode()
        => HashCode.Combine(AbsolutePath, FileSizeBytes, MtimeUtc);

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

    // The key by which a path matches a store entry / change event. Entries key their source through
    // FromFile (which resolves the final symlink target before folding), so a path referenced via a
    // symlink must resolve the same way; falls back to plain normalization for offline media.
    internal static string ResolveComparableKey(string path)
    {
        return TryFromFile(path, out ProxyFingerprint fingerprint)
            ? fingerprint.AbsolutePath
            : NormalizeAbsolutePath(path);
    }

    internal static string NormalizeAbsolutePath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        // Windows and the default macOS volume are case-insensitive; Linux is not.
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
