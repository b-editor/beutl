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

    /// <summary>
    /// A fingerprint carrying only the normalized source key (<see cref="ResolveComparableKey"/>) for an
    /// offline original that cannot be stat-ed. It is NOT a content fingerprint — <see cref="FileSizeBytes"/>
    /// and <see cref="MtimeUtc"/> are unset — so use it ONLY for <see cref="AbsolutePath"/>-based tracking
    /// or lookup (e.g. matching a store change event by path). Never register it or compare it for
    /// staleness; a real entry keyed on the same path has a different size/mtime and will not equal it.
    /// </summary>
    public static ProxyFingerprint ForPathKey(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new ProxyFingerprint { AbsolutePath = ResolveComparableKey(path) };
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

    /// <summary>
    /// The key by which a path matches a store entry / change event (<see cref="AbsolutePath"/>).
    /// Entries key their source through <see cref="FromFile"/> (which resolves the final symlink target
    /// before folding), so a path referenced via a symlink resolves the same way; falls back to plain
    /// normalization for offline media whose original can no longer be stat-ed.
    /// </summary>
    public static string ResolveComparableKey(string path)
    {
        if (TryFromFile(path, out ProxyFingerprint fingerprint))
            return fingerprint.AbsolutePath;

        // Offline media: FromFile can't stat it, but a symlink whose target moved is still
        // readable via returnFinalTarget:false, so resolve it to the target the entry was keyed on
        // (rather than the raw symlink path, which would never match).
        return NormalizeAbsolutePath(ResolveLinkTargetOrSelf(path));
    }

    private static string ResolveLinkTargetOrSelf(string path)
    {
        try
        {
            string fullPath = Path.GetFullPath(path);
            return new FileInfo(fullPath).ResolveLinkTarget(returnFinalTarget: false)?.FullName ?? fullPath;
        }
        catch
        {
            return path;
        }
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
