using System.Buffers;
using System.Text;

namespace Beutl.Editor.VersionControl;

internal static class ProjectConflictMarkerScanner
{
    private const int ScanChunkSize = 4096;
    private const int MinimumMarkerLength = 1;

    private static readonly byte[] s_utf8Bom = [0xef, 0xbb, 0xbf];
    private static readonly HashSet<string> s_projectExtensions = new(
        [".bep", ".scene", ".belm"],
        StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> s_prunedDirectories = new(
        [
            ".beutl",
            ".git",
            ".idea",
            ".vs",
            "bin",
            "node_modules",
            "obj",
            "packages",
        ],
        StringComparer.OrdinalIgnoreCase);

    public static async Task<string?> FindFirstAsync(
        string projectFile,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFile);
        string projectRoot = Path.GetDirectoryName(Path.GetFullPath(projectFile))
                             ?? throw new ArgumentException(
                                 "The project file must have a parent directory.",
                                 nameof(projectFile));
        if (!Directory.Exists(projectRoot))
        {
            return null;
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

            foreach (string childDirectory in directories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ShouldDescendInto(childDirectory))
                {
                    pendingDirectories.Push(childDirectory);
                }
            }

            foreach (string file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!s_projectExtensions.Contains(Path.GetExtension(file)))
                {
                    continue;
                }

                if (!TryGetScannableLength(projectRoot, file, out long scanLength))
                {
                    continue;
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
                    if (await ContainsConflictMarkerAsync(
                            stream,
                            scanLength,
                            cancellationToken).ConfigureAwait(false))
                    {
                        return file;
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        return null;
    }

    private static bool TryGetScannableLength(
        string projectRoot,
        string file,
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

            scanLength = info.Length;
            return true;
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
        if (s_prunedDirectories.Contains(name))
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

    private enum MarkerSequenceState
    {
        None,
        StartSeen,
        SeparatorSeen,
    }
}
