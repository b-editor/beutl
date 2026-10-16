using System.Text;
using Beutl.Editor;

namespace Beutl.Editor.VersionControl;

internal static class ProjectConflictMarkerScanner
{
    private const int ScanChunkSize = 4096;
    private const int MinimumMarkerLength = 7;
    // Project opening waits for this scan, so bound both one unexpectedly large document and a
    // project with many otherwise-small documents.
    private const long DefaultMaxBytesPerFile = 8L * 1024 * 1024;
    private const long DefaultMaxBytesPerInvocation = 32L * 1024 * 1024;

    private static readonly byte[] s_utf8Bom = [0xef, 0xbb, 0xbf];
    private static readonly HashSet<string> s_projectExtensions = new(
        ProjectPathRules.DocumentExtensions,
        StringComparer.OrdinalIgnoreCase);
    private static readonly string[] s_prunedDirectories =
    [
        EditorConstants.BeutlFolder,
        ".git",
        ".idea",
        ".vs",
    ];

    public static Task<string?> FindFirstAsync(
        string projectFile,
        CancellationToken cancellationToken)
    {
        return FindFirstAsync(
            projectFile,
            cancellationToken,
            DefaultMaxBytesPerFile,
            DefaultMaxBytesPerInvocation);
    }

    // The project file is read first, then every project document under its folder, a stale one
    // included: a conflicted file that the project no longer uses costs a little budget, while one it
    // still uses would otherwise fail JSON parsing with no conflict guidance shown.
    internal static async Task<string?> FindFirstAsync(
        string projectFile,
        CancellationToken cancellationToken,
        long maxBytesPerFile,
        long maxBytesPerInvocation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFile);
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
        string canonicalProjectFile = FilePathComparison.ResolveCanonicalPath(fullProjectFile);
        switch (await ScanOnceAsync(
                        projectRoot,
                        canonicalProjectFile,
                        scannedFiles,
                        budget,
                        cancellationToken)
                    .ConfigureAwait(false))
        {
            case FileScan.ConflictFound:
                return fullProjectFile;
            case FileScan.BudgetExhausted:
                return null;
        }

        return await WalkProjectFilesAsync(
                projectRoot,
                scannedFiles,
                budget,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<string?> WalkProjectFilesAsync(
        string projectRoot,
        HashSet<string> scannedFiles,
        ScanBudget budget,
        CancellationToken cancellationToken)
    {
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
                if (!IsProjectDocument(file))
                {
                    continue;
                }

                string canonicalFile;
                try
                {
                    canonicalFile = FilePathComparison.ResolveCanonicalPath(file);
                }
                catch (Exception ex) when (ex is IOException
                                           or UnauthorizedAccessException
                                           or NotSupportedException
                                           or ArgumentException)
                {
                    continue;
                }

                switch (await ScanOnceAsync(projectRoot, canonicalFile, scannedFiles, budget, cancellationToken)
                            .ConfigureAwait(false))
                {
                    case FileScan.ConflictFound:
                        return file;
                    case FileScan.BudgetExhausted:
                        return null;
                }
            }
        }

        return null;
    }

    // The project file is reached again by the walk; it is read once.
    private static async Task<FileScan> ScanOnceAsync(
        string projectRoot,
        string file,
        HashSet<string> scannedFiles,
        ScanBudget budget,
        CancellationToken cancellationToken)
    {
        if (!scannedFiles.Add(file))
        {
            return FileScan.Continue;
        }

        if (await ContainsConflictMarkerAsync(
                    projectRoot,
                    file,
                    budget,
                    cancellationToken)
                .ConfigureAwait(false))
        {
            return FileScan.ConflictFound;
        }

        return budget.IsExhausted ? FileScan.BudgetExhausted : FileScan.Continue;
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
                || !FilePathComparison.IsSameOrDescendant(projectRoot, info.FullName))
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
        byte[] buffer = new byte[(int)Math.Min(scanLength, Array.MaxLength)];
        int read = await stream.ReadAtLeastAsync(
                buffer,
                buffer.Length,
                throwOnEndOfStream: false,
                cancellationToken)
            .ConfigureAwait(false);
        ReadOnlySpan<byte> content = buffer.AsSpan(0, read);
        if (content.StartsWith(s_utf8Bom))
        {
            content = content[s_utf8Bom.Length..];
        }

        return ContainsConflictMarker(Encoding.UTF8.GetString(content), cancellationToken);
    }

    // Git writes a start marker of at least seven '<', a separator line of as many '=' and an end marker of
    // as many '>'; the start and end markers are followed by a space and a label.
    private static bool ContainsConflictMarker(ReadOnlySpan<char> text, CancellationToken cancellationToken)
    {
        int markerLength = 0;
        bool separatorSeen = false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int newline = text.IndexOf('\n');
            ReadOnlySpan<char> line = newline < 0 ? text : text[..newline];
            if (line.EndsWith('\r'))
            {
                line = line[..^1];
            }

            int run = 0;
            while (run < line.Length && line[run] == line[0])
            {
                run++;
            }

            if (IsLabeledMarker(line, run, '<'))
            {
                markerLength = run;
                separatorSeen = false;
            }
            else if (markerLength > 0 && !separatorSeen && run == markerLength && line.Length == run
                     && line[0] == '=')
            {
                separatorSeen = true;
            }
            else if (separatorSeen && run == markerLength && IsLabeledMarker(line, run, '>'))
            {
                return true;
            }

            if (newline < 0)
            {
                return false;
            }

            text = text[(newline + 1)..];
        }
    }

    private static bool IsLabeledMarker(ReadOnlySpan<char> line, int run, char markerCharacter)
    {
        return run >= MinimumMarkerLength
               && line[0] == markerCharacter
               && run + 1 < line.Length
               && line[run] == ' '
               && !line[(run + 1)..].IsWhiteSpace();
    }

    private static bool IsProjectDocument(string file)
    {
        if (s_projectExtensions.Contains(Path.GetExtension(file)))
        {
            return true;
        }

        // A scene's media fingerprints are a project document too, though their extension is the common .json.
        // The name is compared like a pruned folder's, by the rules of the volume that holds it.
        string name = Path.GetFileName(file);
        const string fingerprints = Beutl.ProjectSystem.MediaFingerprintFile.FileName;
        return string.Equals(name, fingerprints, StringComparison.OrdinalIgnoreCase)
               && FilePathComparison.TryAreSameChildPath(
                   Path.GetDirectoryName(file) ?? file,
                   name,
                   fingerprints,
                   out bool areSame)
               && areSame;
    }

    internal static bool ShouldDescendInto(string directory)
    {
        string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));
        string parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(directory))
                        ?? directory;
        if (s_prunedDirectories.Any(pruned =>
                FilePathComparison.TryAreSameChildPath(
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

    private enum FileScan
    {
        Continue,
        ConflictFound,
        BudgetExhausted,
    }
}
