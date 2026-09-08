using System.Buffers;
using System.Text;
using Beutl.Editor;

namespace Beutl.Editor.VersionControl;

internal static class ProjectConflictMarkerScanner
{
    private const int ScanChunkSize = 4096;
    private const int MinimumMarkerLength = 7;
    // Project opening waits for this scan, so bound both one unexpectedly large sidecar and a
    // project that references many otherwise-small files.
    private const long DefaultMaxBytesPerFile = 8L * 1024 * 1024;
    private const long DefaultMaxBytesPerInvocation = 32L * 1024 * 1024;

    private static readonly byte[] s_utf8Bom = [0xef, 0xbb, 0xbf];
    private static readonly HashSet<string> s_projectExtensions = new(
        [".bep", ".scene", ".belm"],
        StringComparer.OrdinalIgnoreCase);
    private static readonly string[] s_prunedDirectories =
    [
        ".beutl",
        ".git",
        ".idea",
        ".vs",
    ];

    public static Task<string?> FindFirstAsync(
        string projectFile,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFile);
        string? projectRoot = Path.GetDirectoryName(Path.GetFullPath(projectFile));
        // An extension can persist a sidecar under an extension this walk does not know, so the
        // files the project itself references are scanned as well: restoration follows those URIs
        // and would otherwise fail JSON parsing with no conflict guidance shown.
        IReadOnlySet<string> referenced = projectRoot is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : SerializedProjectGraph.TryGetRelativePaths(projectFile, projectRoot);
        return FindFirstAsync(projectFile, referenced, cancellationToken);
    }

    internal static async Task<string?> FindFirstAsync(
        string projectFile,
        IReadOnlySet<string> referencedRelativePaths,
        CancellationToken cancellationToken)
    {
        return await FindFirstAsync(
                projectFile,
                referencedRelativePaths,
                cancellationToken,
                DefaultMaxBytesPerFile,
                DefaultMaxBytesPerInvocation)
            .ConfigureAwait(false);
    }

    internal static async Task<string?> FindFirstAsync(
        string projectFile,
        IReadOnlySet<string> referencedRelativePaths,
        CancellationToken cancellationToken,
        long maxBytesPerFile,
        long maxBytesPerInvocation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFile);
        ArgumentNullException.ThrowIfNull(referencedRelativePaths);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytesPerFile);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytesPerInvocation);
        string projectRoot = Path.GetDirectoryName(Path.GetFullPath(projectFile))
                             ?? throw new ArgumentException(
                                 "The project file must have a parent directory.",
                                 nameof(projectFile));
        if (!Directory.Exists(projectRoot))
        {
            return null;
        }

        var budget = new ScanBudget(maxBytesPerFile, maxBytesPerInvocation);
        var scannedFiles = new HashSet<string>(StringComparer.Ordinal);
        string fullProjectFile = Path.GetFullPath(projectFile);
        string canonicalProjectFile = VersionControlPathComparison.ResolveCanonicalPath(fullProjectFile);
        scannedFiles.Add(canonicalProjectFile);
        if (await ContainsConflictMarkerAsync(
                    projectRoot,
                    canonicalProjectFile,
                    budget,
                    cancellationToken)
                .ConfigureAwait(false))
        {
            return fullProjectFile;
        }

        if (budget.IsExhausted)
        {
            return null;
        }

        var referencedProjectFiles = new SortedSet<string>(StringComparer.Ordinal);
        var referencedExtensionFiles = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string relativePath in referencedRelativePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string referenced = VersionControlPathComparison.ResolveCanonicalPath(
                Path.GetFullPath(
                    Path.Combine(
                        projectRoot,
                        relativePath.Replace('/', Path.DirectorySeparatorChar))));
            if (GitCliVersionControlService.IsSupportedMediaPath(referenced))
            {
                continue;
            }

            SortedSet<string> destination = s_projectExtensions.Contains(Path.GetExtension(referenced))
                ? referencedProjectFiles
                : referencedExtensionFiles;
            destination.Add(referenced);
        }

        // Serialized project state has to win the finite budget over arbitrary extension assets.
        foreach (string referenced in referencedProjectFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!scannedFiles.Add(referenced))
            {
                continue;
            }

            if (await ContainsConflictMarkerAsync(
                        projectRoot,
                        referenced,
                        budget,
                        cancellationToken)
                    .ConfigureAwait(false))
            {
                return referenced;
            }

            if (budget.IsExhausted)
            {
                return null;
            }
        }

        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(projectRoot);
        while (pendingDirectories.TryPop(out string? directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string[] files;
            string[] directories;
            try
            {
                files = Directory.GetFiles(directory);
                directories = Directory.GetDirectories(directory);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string childDirectory in directories.OrderByDescending(
                         static path => path,
                         StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ShouldDescendInto(childDirectory))
                {
                    pendingDirectories.Push(childDirectory);
                }
            }

            foreach (string file in files.OrderBy(
                         static path => path,
                         StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!s_projectExtensions.Contains(Path.GetExtension(file)))
                {
                    continue;
                }

                string canonicalFile;
                try
                {
                    canonicalFile = VersionControlPathComparison.ResolveCanonicalPath(file);
                }
                catch (Exception ex) when (ex is IOException
                                           or UnauthorizedAccessException
                                           or NotSupportedException
                                           or ArgumentException)
                {
                    continue;
                }

                if (!scannedFiles.Add(canonicalFile))
                {
                    continue;
                }

                if (await ContainsConflictMarkerAsync(
                            projectRoot,
                            canonicalFile,
                            budget,
                            cancellationToken)
                        .ConfigureAwait(false))
                {
                    return file;
                }

                if (budget.IsExhausted)
                {
                    return null;
                }
            }
        }

        foreach (string referenced in referencedExtensionFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!scannedFiles.Add(referenced))
            {
                continue;
            }

            if (await ContainsConflictMarkerAsync(
                        projectRoot,
                        referenced,
                        budget,
                        cancellationToken)
                    .ConfigureAwait(false))
            {
                return referenced;
            }

            if (budget.IsExhausted)
            {
                return null;
            }
        }

        return null;
    }

    private static async Task<bool> ContainsConflictMarkerAsync(
        string projectRoot,
        string file,
        ScanBudget budget,
        CancellationToken cancellationToken)
    {
        if (budget.IsExhausted
            || !TryGetScannableLength(projectRoot, file, budget, out long scanLength))
        {
            return false;
        }

        try
        {
            await using FileStream stream = new(
                file,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                ScanChunkSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await ContainsConflictMarkerAsync(stream, scanLength, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TryGetScannableLength(
        string projectRoot,
        string file,
        ScanBudget budget,
        out long scanLength)
    {
        scanLength = 0;
        try
        {
            var info = new FileInfo(file);
            info.Refresh();
            if (info.LinkTarget is not null
                || info.Length <= 0
                || !RepositoryPathComparer.IsContainedWithin(projectRoot, info.FullName))
            {
                return false;
            }

            scanLength = budget.Reserve(info.Length);
            return scanLength > 0;
        }
        catch (Exception ex)
            when (ex is IOException
                  or UnauthorizedAccessException
                  or NotSupportedException)
        {
            return false;
        }
    }

    private static async Task<bool> ContainsConflictMarkerAsync(
        Stream stream,
        long scanLength,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[ScanChunkSize];
        long lineLength = 0;
        long prefixRunLength = 0;
        long expectedMarkerLength = 0;
        byte firstByte = 0;
        byte byteAfterRun = 0;
        byte lastByte = 0;
        byte[] pendingRuneBytes = new byte[4];
        int pendingRuneByteCount = 0;
        bool hasLabelContent = false;
        MarkerSequenceState state = MarkerSequenceState.None;
        long remaining = scanLength;

        bool ProcessByte(byte value)
        {
            if (value == (byte)'\n')
            {
                long contentLength = lineLength > 0 && lastByte == (byte)'\r'
                    ? lineLength - 1
                    : lineLength;
                bool result = ProcessLine(
                    firstByte,
                    prefixRunLength,
                    byteAfterRun,
                    hasLabelContent || pendingRuneByteCount > 0,
                    contentLength,
                    ref state,
                    ref expectedMarkerLength);
                lineLength = 0;
                prefixRunLength = 0;
                firstByte = 0;
                byteAfterRun = 0;
                pendingRuneByteCount = 0;
                hasLabelContent = false;
                return result;
            }

            if (lineLength == 0)
            {
                firstByte = value;
                prefixRunLength = 1;
            }
            else if (lineLength == prefixRunLength)
            {
                if (value == firstByte)
                {
                    prefixRunLength++;
                }
                else
                {
                    byteAfterRun = value;
                }
            }
            else if (!hasLabelContent
                     && HasNonWhitespaceRune(
                         value,
                         pendingRuneBytes,
                         ref pendingRuneByteCount))
            {
                hasLabelContent = true;
            }

            lineLength++;
            lastByte = value;
            return false;
        }

        int initialLength = 0;
        bool reachedEnd = false;
        while (initialLength < s_utf8Bom.Length && remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int requested = (int)Math.Min(s_utf8Bom.Length - initialLength, remaining);
            int read = await stream.ReadAsync(
                buffer.AsMemory(initialLength, requested),
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                reachedEnd = true;
                break;
            }

            initialLength += read;
            remaining -= read;
        }

        int initialOffset = initialLength == s_utf8Bom.Length
                            && buffer.AsSpan(0, initialLength).SequenceEqual(s_utf8Bom)
            ? initialLength
            : 0;
        for (int i = initialOffset; i < initialLength; i++)
        {
            if (ProcessByte(buffer[i]))
            {
                return true;
            }
        }

        while (!reachedEnd && remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int requested = (int)Math.Min(ScanChunkSize, remaining);
            int read = await stream.ReadAsync(
                buffer.AsMemory(0, requested),
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            remaining -= read;
            for (int i = 0; i < read; i++)
            {
                if (ProcessByte(buffer[i]))
                {
                    return true;
                }
            }
        }

        long finalContentLength = lineLength > 0 && lastByte == (byte)'\r'
            ? lineLength - 1
            : lineLength;
        return ProcessLine(
            firstByte,
            prefixRunLength,
            byteAfterRun,
            hasLabelContent || pendingRuneByteCount > 0,
            finalContentLength,
            ref state,
            ref expectedMarkerLength);
    }

    private static bool ProcessLine(
        byte firstByte,
        long prefixRunLength,
        byte byteAfterRun,
        bool hasLabelContent,
        long lineLength,
        ref MarkerSequenceState state,
        ref long expectedMarkerLength)
    {
        if (IsLabeledMarker(
                firstByte,
                prefixRunLength,
                byteAfterRun,
                hasLabelContent,
                lineLength,
                (byte)'<'))
        {
            state = MarkerSequenceState.StartSeen;
            expectedMarkerLength = prefixRunLength;
        }
        else if (state == MarkerSequenceState.StartSeen
                 && firstByte == (byte)'='
                 && prefixRunLength == expectedMarkerLength
                 && lineLength == prefixRunLength)
        {
            state = MarkerSequenceState.SeparatorSeen;
        }
        else if (state == MarkerSequenceState.SeparatorSeen
                 && prefixRunLength == expectedMarkerLength
                 && IsLabeledMarker(
                     firstByte,
                     prefixRunLength,
                     byteAfterRun,
                     hasLabelContent,
                     lineLength,
                     (byte)'>'))
        {
            return true;
        }

        return false;
    }

    private static bool IsLabeledMarker(
        byte firstByte,
        long prefixRunLength,
        byte byteAfterRun,
        bool hasLabelContent,
        long lineLength,
        byte markerByte)
    {
        return firstByte == markerByte
               && prefixRunLength >= MinimumMarkerLength
               && lineLength - prefixRunLength >= 2
               && byteAfterRun == (byte)' '
               && hasLabelContent;
    }

    private static bool HasNonWhitespaceRune(
        byte value,
        byte[] pendingRuneBytes,
        ref int pendingRuneByteCount)
    {
        pendingRuneBytes[pendingRuneByteCount++] = value;
        OperationStatus status = Rune.DecodeFromUtf8(
            pendingRuneBytes.AsSpan(0, pendingRuneByteCount),
            out Rune rune,
            out _);
        if (status == OperationStatus.NeedMoreData
            && pendingRuneByteCount < pendingRuneBytes.Length)
        {
            return false;
        }

        pendingRuneByteCount = 0;
        return status != OperationStatus.Done || !Rune.IsWhiteSpace(rune);
    }

    internal static bool ShouldDescendInto(string directory)
    {
        string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));
        string parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(directory))
                        ?? directory;
        if (s_prunedDirectories.Any(pruned =>
                VersionControlPathComparison.TryAreSameChildPath(
                    parent,
                    name,
                    pruned,
                    out bool areSame)
                && areSame))
        {
            return false;
        }

        try
        {
            return new DirectoryInfo(directory).LinkTarget is null;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private sealed class ScanBudget(long maxBytesPerFile, long maxBytesPerInvocation)
    {
        private long _remainingBytes = maxBytesPerInvocation;

        public bool IsExhausted => _remainingBytes == 0 || maxBytesPerFile == 0;

        public long Reserve(long fileLength)
        {
            long reserved = Math.Min(fileLength, Math.Min(maxBytesPerFile, _remainingBytes));
            _remainingBytes -= reserved;
            return reserved;
        }
    }

    private enum MarkerSequenceState
    {
        None,
        StartSeen,
        SeparatorSeen,
    }
}
