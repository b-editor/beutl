using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Beutl.Editor.VersionControl;

internal sealed partial class GitCliRunner
{
    public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        if (HasActiveProcess)
        {
            return null;
        }

        try
        {
            foreach (string lockPath in GetRepositoryLockPaths(repository))
            {
                RepositoryLockFileSnapshot? snapshot = _readLockFileSnapshot(lockPath);
                if (snapshot is not { } lockSnapshot)
                {
                    continue;
                }

                if (_timeProvider.GetUtcNow() - lockSnapshot.LastWriteTimeUtc > StaleLockAge)
                {
                    var lockInfo = new RepositoryLockInfo(
                        lockPath,
                        lockSnapshot.LastWriteTimeUtc);
                    if (lockSnapshot.Identity is { } identity)
                    {
                        _lockFileIdentities.Add(
                            lockInfo,
                            new RepositoryLockFileIdentityBox(identity));
                    }

                    return lockInfo;
                }
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or ArgumentException
                                   or NotSupportedException)
        {
            return null;
        }
    }

    public bool RemoveRecoverableRepositoryLock(
        RepositoryInfo repository,
        RepositoryLockInfo lockInfo)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(lockInfo);
        if (!_supportsConditionalLockDeletion
            || !_lockFileIdentities.TryGetValue(
                lockInfo,
                out RepositoryLockFileIdentityBox? offered)
            || offered is null)
        {
            return false;
        }

        RepositoryLockInfo? current = GetRecoverableRepositoryLock(repository);
        if (current is null
            || !_lockFileIdentities.TryGetValue(
                current,
                out RepositoryLockFileIdentityBox? currentIdentity)
            || currentIdentity is null
            || !VersionControlPathComparison.AreSameCanonicalPath(
                current.LockPath,
                Path.GetFullPath(lockInfo.LockPath))
            || current.LastWriteTimeUtc != lockInfo.LastWriteTimeUtc
            || currentIdentity.Identity != offered.Identity)
        {
            return false;
        }

        try
        {
            var expected = new RepositoryLockFileSnapshot(
                current.LastWriteTimeUtc,
                currentIdentity.Identity);
            return _deleteLockFileConditionally(current.LockPath, expected);
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or ArgumentException
                                   or NotSupportedException)
        {
            return false;
        }
    }

    private IReadOnlyList<string> GetRepositoryLockPaths(RepositoryInfo repository)
    {
        string gitDirectory = GetGitDirectory(repository);
        List<string> lockPaths =
        [
            Path.Combine(gitDirectory, "index.lock"),
            Path.Combine(gitDirectory, "HEAD.lock"),
        ];
        try
        {
            string commonDirectory = GetCommonDirectory(gitDirectory);
            lockPaths.Add(Path.Combine(commonDirectory, "config.lock"));
            string? branchLockPath = GetCurrentBranchLockPath(gitDirectory);
            if (branchLockPath is not null)
            {
                lockPaths.Add(branchLockPath);
            }

            lockPaths.AddRange(GetRefLockPaths(gitDirectory));
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or ArgumentException
                                   or NotSupportedException)
        {
        }

        return lockPaths;
    }

    private IReadOnlyList<string> GetRefLockPaths(string gitDirectory)
    {
        string commonDirectory = GetCommonDirectory(gitDirectory);
        string refsDirectory = Path.GetFullPath(Path.Combine(commonDirectory, "refs"));
        if (!Directory.Exists(refsDirectory))
        {
            return [];
        }

        if (ContainsReparsePoint(commonDirectory, refsDirectory))
        {
            return [];
        }

        string canonicalRefsDirectory = Path.TrimEndingDirectorySeparator(
            VersionControlPathComparison.ResolveCanonicalPath(refsDirectory));
        var visitedDirectories = new HashSet<string>(StringComparer.Ordinal);
        var pendingDirectories = new Stack<string>();
        var lockPaths = new List<string>();
        pendingDirectories.Push(refsDirectory);

        while (pendingDirectories.TryPop(out string? directory))
        {
            try
            {
                if (!TryEnterRefDirectory(
                        directory,
                        commonDirectory,
                        canonicalRefsDirectory,
                        visitedDirectories,
                        out string fullDirectory))
                {
                    continue;
                }

                foreach (string candidate in _enumerateFileSystemEntries(fullDirectory))
                {
                    CollectRefLockEntry(
                        candidate,
                        commonDirectory,
                        canonicalRefsDirectory,
                        pendingDirectories,
                        lockPaths);
                }
            }
            catch (Exception ex) when (IsFileInspectionFailure(ex))
            {
            }
        }

        return lockPaths;
    }

    // A directory is walked once, and only while it is a real directory inside the refs directory.
    private static bool TryEnterRefDirectory(
        string directory,
        string commonDirectory,
        string canonicalRefsDirectory,
        HashSet<string> visitedDirectories,
        out string fullDirectory)
    {
        fullDirectory = Path.GetFullPath(directory);
        FileAttributes directoryAttributes = File.GetAttributes(fullDirectory);
        if ((directoryAttributes & FileAttributes.Directory) == 0
            || (directoryAttributes & FileAttributes.ReparsePoint) != 0
            || ContainsReparsePoint(commonDirectory, fullDirectory))
        {
            return false;
        }

        string canonicalDirectory = Path.TrimEndingDirectorySeparator(
            VersionControlPathComparison.ResolveCanonicalPath(fullDirectory));
        FileAttributes verifiedDirectoryAttributes = File.GetAttributes(fullDirectory);
        if (!IsCanonicalSameOrDescendant(
                canonicalRefsDirectory,
                canonicalDirectory)
            || (verifiedDirectoryAttributes & FileAttributes.Directory) == 0
            || (verifiedDirectoryAttributes & FileAttributes.ReparsePoint) != 0
            || !visitedDirectories.Add(canonicalDirectory))
        {
            return false;
        }

        return true;
    }

    // Queues a subdirectory for the walk or records a ref lock file. An entry that cannot be inspected is skipped.
    private static void CollectRefLockEntry(
        string candidate,
        string commonDirectory,
        string canonicalRefsDirectory,
        Stack<string> pendingDirectories,
        List<string> lockPaths)
    {
        try
        {
            string fullPath = Path.GetFullPath(candidate);
            FileAttributes attributes = File.GetAttributes(fullPath);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                return;
            }

            string canonicalPath = Path.TrimEndingDirectorySeparator(
                VersionControlPathComparison.ResolveCanonicalPath(fullPath));
            FileAttributes verifiedAttributes = File.GetAttributes(fullPath);
            if (!IsCanonicalSameOrDescendant(
                    canonicalRefsDirectory,
                    canonicalPath)
                || (verifiedAttributes & FileAttributes.ReparsePoint) != 0
                || (verifiedAttributes & FileAttributes.Directory)
                != (attributes & FileAttributes.Directory))
            {
                return;
            }

            if ((verifiedAttributes & FileAttributes.Directory) != 0)
            {
                if (!ContainsReparsePoint(commonDirectory, fullPath))
                {
                    pendingDirectories.Push(fullPath);
                }

                return;
            }

            if (IsRefLockFile(fullPath)
                && !ContainsReparsePoint(
                    commonDirectory,
                    Path.GetDirectoryName(fullPath)!))
            {
                lockPaths.Add(fullPath);
            }
        }
        catch (Exception ex) when (IsFileInspectionFailure(ex))
        {
        }
    }

    private static bool IsCanonicalSameOrDescendant(string canonicalRoot, string canonicalPath)
    {
        if (string.Equals(canonicalRoot, canonicalPath, StringComparison.Ordinal))
        {
            return true;
        }

        string prefix = canonicalRoot.EndsWith(Path.DirectorySeparatorChar)
            ? canonicalRoot
            : canonicalRoot + Path.DirectorySeparatorChar;
        return canonicalPath.StartsWith(prefix, StringComparison.Ordinal);
    }

    private static bool IsRefLockFile(string path)
    {
        const string LockSuffix = ".lock";
        if (!Path.GetFileName(path).EndsWith(LockSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string lowercaseSuffixPath = path[..^LockSuffix.Length] + LockSuffix;
        return Path.Exists(lowercaseSuffixPath)
               && VersionControlPathComparison.AreSameCanonicalPath(path, lowercaseSuffixPath);
    }

    private static bool IsFileInspectionFailure(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException;
    }

    private string? GetCurrentBranchLockPath(string gitDirectory)
    {
        const string refPrefix = "ref: refs/heads/";
        string head = _readAllText(Path.Combine(gitDirectory, "HEAD")).Trim();
        if (!head.StartsWith(refPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        string branchPath = head[refPrefix.Length..];
        if (string.IsNullOrWhiteSpace(branchPath)
            || Path.IsPathFullyQualified(branchPath)
            || branchPath
                .Split(['/', '\\'])
                .Any(static segment => segment is "" or "." or ".."))
        {
            return null;
        }

        string commonDirectory = GetCommonDirectory(gitDirectory);
        string headsDirectory = Path.GetFullPath(
            Path.Combine(commonDirectory, "refs", "heads"));
        string refPath = Path.GetFullPath(Path.Combine(
            headsDirectory,
            branchPath.Replace('/', Path.DirectorySeparatorChar)));
        if (!VersionControlPathComparison.IsSameOrDescendant(headsDirectory, refPath))
        {
            return null;
        }

        if (ContainsReparsePoint(
                commonDirectory,
                Path.GetDirectoryName(refPath)!))
        {
            return null;
        }

        return refPath + ".lock";
    }

    private static bool ContainsReparsePoint(string root, string path)
    {
        string current = Path.GetFullPath(path);
        string boundary = Path.GetFullPath(root);
        while (true)
        {
            if (Directory.Exists(current)
                && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }

            if (VersionControlPathComparison.AreSameCanonicalPath(current, boundary))
            {
                return false;
            }

            string? parent = Path.GetDirectoryName(current);
            if (parent is null || VersionControlPathComparison.AreSameCanonicalPath(parent, current))
            {
                return true;
            }

            current = parent;
        }
    }

    private string GetCommonDirectory(string gitDirectory)
    {
        string commonDirectoryPath = Path.Combine(gitDirectory, "commondir");
        if (!File.Exists(commonDirectoryPath))
        {
            return gitDirectory;
        }

        string commonDirectory = _readAllText(commonDirectoryPath).Trim();
        if (!Path.IsPathFullyQualified(commonDirectory))
        {
            commonDirectory = Path.Combine(gitDirectory, commonDirectory);
        }

        return Path.GetFullPath(commonDirectory);
    }

    private string GetGitDirectory(RepositoryInfo repository)
    {
        string dotGitPath = Path.Combine(repository.RepoRoot, ".git");
        if (Directory.Exists(dotGitPath))
        {
            return Path.GetFullPath(dotGitPath);
        }

        if (File.Exists(dotGitPath))
        {
            const string prefix = "gitdir:";
            string contents = _readAllText(dotGitPath).Trim();
            if (contents.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                string gitDirectory = contents[prefix.Length..].Trim();
                if (!Path.IsPathFullyQualified(gitDirectory))
                {
                    gitDirectory = Path.Combine(repository.RepoRoot, gitDirectory);
                }

                return Path.GetFullPath(gitDirectory);
            }
        }

        return Path.GetFullPath(dotGitPath);
    }

    private static RepositoryLockFileSnapshot? TryReadPortableLockFileSnapshot(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        return new RepositoryLockFileSnapshot(
            new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero),
            Identity: null);
    }

    private static RepositoryLockFileSnapshot? TryReadWindowsLockFileSnapshot(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        using SafeFileHandle handle = WindowsNativeMethods.CreateFileW(
            path,
            WindowsNativeMethods.FileReadAttributes,
            WindowsNativeMethods.FileShareRead
            | WindowsNativeMethods.FileShareWrite
            | WindowsNativeMethods.FileShareDelete,
            0,
            WindowsNativeMethods.OpenExisting,
            WindowsNativeMethods.FileFlagOpenReparsePoint,
            0);
        return handle.IsInvalid
            ? null
            : TryReadWindowsLockFileSnapshot(handle);
    }

    private static RepositoryLockFileSnapshot? TryReadWindowsLockFileSnapshot(
        SafeFileHandle handle)
    {
        if (WindowsNativeMethods.GetFileInformationByHandle(
                handle,
                out ByHandleFileInformation info) == 0)
        {
            return null;
        }

        long lastWriteFileTime = ((long)info.LastWriteTimeHigh << 32)
                                 | info.LastWriteTimeLow;
        ulong fileIndex = ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow;
        return new RepositoryLockFileSnapshot(
            new DateTimeOffset(DateTime.FromFileTimeUtc(lastWriteFileTime), TimeSpan.Zero),
            new RepositoryLockFileIdentity(info.VolumeSerialNumber, fileIndex));
    }

    private static bool TryDeleteWindowsLockFileConditionally(
        string path,
        RepositoryLockFileSnapshot expected)
    {
        if (!OperatingSystem.IsWindows() || expected.Identity is null)
        {
            return false;
        }

        using SafeFileHandle handle = WindowsNativeMethods.CreateFileW(
            path,
            WindowsNativeMethods.Delete | WindowsNativeMethods.FileReadAttributes,
            shareMode: 0,
            0,
            WindowsNativeMethods.OpenExisting,
            WindowsNativeMethods.FileFlagOpenReparsePoint,
            0);
        RepositoryLockFileSnapshot? actual = handle.IsInvalid
            ? null
            : TryReadWindowsLockFileSnapshot(handle);
        if (actual != expected)
        {
            return false;
        }

        var disposition = new FileDispositionInfo { DeleteFile = 1 };
        return WindowsNativeMethods.SetFileInformationByHandle(
                   handle,
                   WindowsNativeMethods.FileDispositionInfo,
                   ref disposition,
                   (uint)Marshal.SizeOf<FileDispositionInfo>()) != 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public uint CreationTimeLow;
        public uint CreationTimeHigh;
        public uint LastAccessTimeLow;
        public uint LastAccessTimeHigh;
        public uint LastWriteTimeLow;
        public uint LastWriteTimeHigh;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileDispositionInfo
    {
        public byte DeleteFile;
    }

    private sealed class RepositoryLockFileIdentityBox(RepositoryLockFileIdentity identity)
    {
        public RepositoryLockFileIdentity Identity { get; } = identity;
    }

    private static partial class WindowsNativeMethods
    {
        internal const uint Delete = 0x00010000;
        internal const uint FileReadAttributes = 0x00000080;
        internal const uint FileShareRead = 0x00000001;
        internal const uint FileShareWrite = 0x00000002;
        internal const uint FileShareDelete = 0x00000004;
        internal const uint OpenExisting = 3;
        internal const uint FileFlagOpenReparsePoint = 0x00200000;
        internal const int FileDispositionInfo = 4;

        [LibraryImport(
            "kernel32.dll",
            EntryPoint = "CreateFileW",
            SetLastError = true,
            StringMarshalling = StringMarshalling.Utf16)]
        internal static partial SafeFileHandle CreateFileW(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            nint securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            nint templateFile);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        internal static partial int GetFileInformationByHandle(
            SafeFileHandle file,
            out ByHandleFileInformation fileInformation);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        internal static partial int SetFileInformationByHandle(
            SafeFileHandle file,
            int fileInformationClass,
            ref FileDispositionInfo fileInformation,
            uint bufferSize);
    }
}
