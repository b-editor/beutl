using System.Text;

namespace Beutl.Editor.VersionControl;

internal sealed partial class GitCliVersionControlService
{
    private async Task EnsureLinesAsync(
        string path,
        IReadOnlyList<string> requiredLines,
        CancellationToken cancellationToken)
    {
        await UpdateHygieneFileAsync(
            path,
            lines =>
            {
                foreach (string requiredLine in requiredLines)
                {
                    if (!lines.Contains(requiredLine, StringComparer.Ordinal))
                    {
                        lines.Add(requiredLine);
                    }
                }

                return lines;
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureAttributesAsync(
        string path,
        bool useLfs,
        bool removeManagedLfs,
        CancellationToken cancellationToken)
    {
        await UpdateHygieneFileAsync(
            path,
            lines =>
            {
                // A machine that does not use LFS leaves a block another machine wrote alone.
                if (removeManagedLfs)
                {
                    RemoveManagedLfsBlocks(lines);
                }

                foreach (string requiredLine in s_textAttributeLines)
                {
                    if (!lines.Contains(requiredLine, StringComparer.Ordinal))
                    {
                        lines.Add(requiredLine);
                    }
                }

                if (useLfs)
                {
                    AddMissingManagedLfsLines(lines);
                }

                return lines;
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> IsRepositoryHygieneAppliedCoreAsync(
        string projectRoot,
        CancellationToken cancellationToken)
    {
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot));
        return await HygieneFileContainsLinesAsync(
                   Path.Combine(normalizedRoot, ".gitignore"),
                   ProjectPathRules.GitIgnoreLines,
                   cancellationToken)
                   .ConfigureAwait(false)
               && await HygieneFileContainsLinesAsync(
                   Path.Combine(normalizedRoot, ".gitattributes"),
                   s_textAttributeLines,
                   cancellationToken)
                   .ConfigureAwait(false);
    }

    private static async Task<bool> HygieneFileContainsLinesAsync(
        string path,
        IReadOnlyList<string> requiredLines,
        CancellationToken cancellationToken)
    {
        HygieneFileSnapshot snapshot = await ReadHygieneFileSnapshotAsync(path, cancellationToken)
            .ConfigureAwait(false);
        if (!snapshot.Exists)
        {
            return false;
        }

        List<string> lines = ReadHygieneLines(snapshot.Contents);
        return requiredLines.All(required => lines.Contains(required, StringComparer.Ordinal));
    }

    // Keeps the lines other builds wrote, so builds with other extension lists stop rewriting the block.
    private static void AddMissingManagedLfsLines(List<string> lines)
    {
        List<(int Begin, int End)> blocks = FindManagedLfsBlocks(lines);
        if (blocks.Count == 0)
        {
            lines.InsertRange(0, [ManagedLfsBeginMarker, .. s_lfsAttributeLines, ManagedLfsEndMarker]);
            return;
        }

        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach ((int begin, int end) in blocks)
        {
            present.UnionWith(lines.GetRange(begin + 1, end - begin - 1));
        }

        lines.InsertRange(blocks[0].End, s_lfsAttributeLines.Where(line => !present.Contains(line)));
    }

    private static void RemoveManagedLfsBlocks(List<string> lines)
    {
        List<(int Begin, int End)> blocks = FindManagedLfsBlocks(lines);
        for (int i = blocks.Count - 1; i >= 0; i--)
        {
            lines.RemoveRange(blocks[i].Begin, blocks[i].End - blocks[i].Begin + 1);
        }
    }

    private static List<(int Begin, int End)> FindManagedLfsBlocks(List<string> lines)
    {
        var blocks = new List<(int Begin, int End)>();
        int index = 0;
        while (index < lines.Count)
        {
            if (!string.Equals(lines[index], ManagedLfsBeginMarker, StringComparison.Ordinal))
            {
                index++;
                continue;
            }

            int end = lines.FindIndex(
                index + 1,
                static line => string.Equals(
                    line,
                    ManagedLfsEndMarker,
                    StringComparison.Ordinal));
            if (end < 0)
            {
                index++;
                continue;
            }

            blocks.Add((index, end));
            index = end + 1;
        }

        return blocks;
    }

    // A temporary file renamed over the target, so Git never reads a half-written file. An edit another
    // program makes between the read and the rename is overwritten.
    private static async Task UpdateHygieneFileAsync(
        string path,
        Func<List<string>, List<string>> updateLines,
        CancellationToken cancellationToken)
    {
        HygieneFileSnapshot snapshot = await ReadHygieneFileSnapshotAsync(path, cancellationToken)
            .ConfigureAwait(false);
        string contents = string.Join('\n', updateLines(ReadHygieneLines(snapshot.Contents))) + '\n';
        if (snapshot.Exists && string.Equals(snapshot.Contents, contents, StringComparison.Ordinal))
        {
            return;
        }

        string directory = Path.GetDirectoryName(path)
                           ?? throw new InvalidOperationException(
                               $"The repository hygiene path '{path}' has no parent directory.");
        string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            // Git reads these files as bytes, so a file in a legacy encoding is written back in it.
            await File.WriteAllTextAsync(
                    temporaryPath,
                    contents,
                    snapshot.IsLegacyEncoded ? Encoding.Latin1 : new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch
        {
            TryDeleteHygieneTemporaryFile(temporaryPath);
            throw;
        }
    }

    private static async Task<HygieneFileSnapshot> ReadHygieneFileSnapshotAsync(
        string path,
        CancellationToken cancellationToken)
    {
        EnsureHygienePathIsSafe(path);
        try
        {
            if (!File.Exists(path))
            {
                return new HygieneFileSnapshot(Exists: false, Contents: null);
            }

            byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken)
                .ConfigureAwait(false);
            int offset = bytes.AsSpan().StartsWith(s_utf8Bom)
                ? s_utf8Bom.Length
                : 0;
            string contents;
            bool isLegacyEncoded = false;
            try
            {
                contents = s_strictUtf8.GetString(bytes.AsSpan(offset));
            }
            catch (DecoderFallbackException)
            {
                // Git reads these files as bytes, so a comment in a legacy encoding is valid. Latin-1
                // maps each byte to one character and keeps those bytes intact around Beutl's lines.
                contents = Encoding.Latin1.GetString(bytes);
                isLegacyEncoded = true;
            }

            if (contents.Contains('\0'))
            {
                // Git matches these files line by line on raw bytes, which a UTF-16 file defeats, and
                // appending Beutl's ASCII lines would corrupt it further.
                throw new InvalidDataException(
                    $"Repository hygiene cannot edit '{path}' because it contains NUL bytes.");
            }

            EnsureHygienePathIsSafe(path);
            return new HygieneFileSnapshot(Exists: true, contents, isLegacyEncoded);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            EnsureHygienePathIsSafe(path);
            return new HygieneFileSnapshot(Exists: false, Contents: null);
        }
    }

    private static List<string> ReadHygieneLines(string? contents)
    {
        if (string.IsNullOrEmpty(contents))
        {
            return [];
        }

        var lines = new List<string>();
        using var reader = new StringReader(contents);
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return lines;
    }

    private static void TryDeleteHygieneTemporaryFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed record HygieneFileSnapshot(
        bool Exists,
        string? Contents,
        bool IsLegacyEncoded = false);

    private static void EnsureHygienePathsAreSafe(RepositoryInfo repository)
    {
        EnsureHygienePathIsSafe(Path.Combine(repository.ProjectRoot, ".gitignore"));
        EnsureHygienePathIsSafe(Path.Combine(repository.ProjectRoot, ".gitattributes"));
    }

    private static void EnsureHygienePathIsSafe(string path)
    {
        try
        {
            var file = new FileInfo(path);
            file.Refresh();
            if (file.LinkTarget is not null
                || (file.Exists && (file.Attributes & FileAttributes.ReparsePoint) != 0)
                || Directory.Exists(path))
            {
                throw new InvalidOperationException(
                    $"Repository hygiene requires '{path}' to be a regular file.");
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or NotSupportedException)
        {
            throw new InvalidOperationException(
                $"The repository hygiene path '{path}' could not be inspected safely.",
                ex);
        }
    }
}
