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
                int? managedBlockIndex = useLfs || removeManagedLfs
                    ? RemoveManagedLfsBlocks(lines)
                    : null;
                foreach (string requiredLine in s_textAttributeLines)
                {
                    if (!lines.Contains(requiredLine, StringComparer.Ordinal))
                    {
                        lines.Add(requiredLine);
                    }
                }

                if (useLfs)
                {
                    int insertionIndex = managedBlockIndex is { } existingIndex
                        ? Math.Min(existingIndex, lines.Count)
                        : 0;
                    lines.InsertRange(
                        insertionIndex,
                        [ManagedLfsBeginMarker, .. s_lfsAttributeLines, ManagedLfsEndMarker]);
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
                   s_gitIgnoreLines,
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

    private static int? RemoveManagedLfsBlocks(List<string> lines)
    {
        int? firstBlockIndex = null;
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

            firstBlockIndex ??= index;
            lines.RemoveRange(index, end - index + 1);
        }

        return firstBlockIndex;
    }

    // The hygiene files are ordinary user-visible files. Portable .NET has no atomic
    // compare-and-delete/replace primitive, so rollback never mutates them after initialization;
    // retaining Beutl's additions is safer than risking deletion or overwrite of an external edit.
    private async Task UpdateHygieneFileAsync(
        string path,
        Func<List<string>, List<string>> updateLines,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < MaxHygieneWriteAttempts; attempt++)
        {
            HygieneFileSnapshot snapshot = await ReadHygieneFileSnapshotAsync(
                    path,
                    cancellationToken)
                .ConfigureAwait(false);
            List<string> lines = ReadHygieneLines(snapshot.Contents);
            string contents = string.Join('\n', updateLines(lines)) + '\n';
            if (snapshot.Exists
                && string.Equals(snapshot.Contents, contents, StringComparison.Ordinal))
            {
                return;
            }

            string? temporaryPath = await WriteTemporaryHygieneFileAsync(
                    path,
                    contents,
                    snapshot,
                    cancellationToken)
                .ConfigureAwait(false);
            try
            {
                if (_beforeHygieneFileReplace is not null)
                {
                    await _beforeHygieneFileReplace(path, cancellationToken).ConfigureAwait(false);
                }

                HygieneFileSnapshot current = await ReadHygieneFileSnapshotAsync(
                        path,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (current != snapshot)
                {
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                HygieneFileSnapshot finalSnapshot = await ReadHygieneFileSnapshotAsync(
                        path,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (finalSnapshot != snapshot)
                {
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (_beforeFileCommit is not null)
                {
                    await _beforeFileCommit(path, cancellationToken).ConfigureAwait(false);
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (snapshot.Exists)
                {
                    string candidatePath = temporaryPath;
                    temporaryPath = null;
                    if (await TryCommitExistingHygieneFileAsync(
                                path,
                                candidatePath,
                                snapshot)
                            .ConfigureAwait(false))
                    {
                        return;
                    }

                    continue;
                }

                try
                {
                    File.Move(temporaryPath, path, overwrite: false);
                }
                catch (IOException) when (File.Exists(path))
                {
                    continue;
                }

                return;
            }
            finally
            {
                if (temporaryPath is not null)
                {
                    TryDeleteHygieneTemporaryFile(temporaryPath);
                }
            }
        }

        throw new InvalidOperationException(
            $"Repository hygiene could not update '{path}' because it kept changing.");
    }

    private async Task<bool> TryCommitExistingHygieneFileAsync(
        string path,
        string candidatePath,
        HygieneFileSnapshot expectedSnapshot)
    {
        HygieneFileSnapshot candidateSnapshot = await ReadHygieneFileSnapshotAsync(
                candidatePath,
                CancellationToken.None)
            .ConfigureAwait(false);
        string displacedPath;
        try
        {
            displacedPath = AtomicFileExchange.ReplacePreservingTarget(path, candidatePath);
        }
        catch
        {
            await TryDeleteHygieneFileIfUnchangedAsync(candidatePath, candidateSnapshot)
                .ConfigureAwait(false);
            throw;
        }

        Exception? verificationFailure = null;
        HygieneFileSnapshot? displacedSnapshot = null;
        try
        {
            if (_afterFileExchange is not null)
            {
                await _afterFileExchange(path, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            displacedSnapshot = await ReadHygieneFileSnapshotAsync(
                    displacedPath,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            verificationFailure = ex;
        }

        if (verificationFailure is null && displacedSnapshot == expectedSnapshot)
        {
            if (!_deleteVerifiedHygieneFile(displacedPath))
            {
                throw new InvalidOperationException(
                    $"Repository hygiene was updated, but the verified prior contents could not be removed and were retained at '{displacedPath}'.");
            }

            return true;
        }

        string recoveredCandidatePath;
        try
        {
            recoveredCandidatePath = AtomicFileExchange.ReplacePreservingTarget(
                path,
                displacedPath);
        }
        catch (Exception rollbackFailure)
        {
            Exception failure = verificationFailure is null
                ? rollbackFailure
                : new AggregateException(verificationFailure, rollbackFailure);
            throw new InvalidOperationException(
                $"Repository hygiene changed concurrently; the prior contents were retained at '{displacedPath}' because they could not be restored safely.",
                failure);
        }

        HygieneFileSnapshot recoveredCandidate;
        try
        {
            recoveredCandidate = await ReadHygieneFileSnapshotAsync(
                    recoveredCandidatePath,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception inspectionFailure)
        {
            throw new InvalidOperationException(
                $"Repository hygiene changed concurrently; the displaced replacement was retained at '{recoveredCandidatePath}' because it could not be inspected safely.",
                inspectionFailure);
        }

        if (recoveredCandidate != candidateSnapshot)
        {
            throw new InvalidOperationException(
                $"Repository hygiene changed more than once during recovery. The original file was restored and the later contents were retained at '{recoveredCandidatePath}'.");
        }

        if (!_deleteVerifiedHygieneFile(recoveredCandidatePath))
        {
            throw new InvalidOperationException(
                $"Repository hygiene was restored, but the displaced replacement could not be removed and was retained at '{recoveredCandidatePath}'.");
        }

        if (verificationFailure is not null)
        {
            throw new InvalidOperationException(
                "Repository hygiene changed to an entry that could not be inspected safely; the original entry was restored.",
                verificationFailure);
        }

        return false;
    }

    private static async Task TryDeleteHygieneFileIfUnchangedAsync(
        string path,
        HygieneFileSnapshot expectedSnapshot)
    {
        try
        {
            HygieneFileSnapshot current = await ReadHygieneFileSnapshotAsync(
                    path,
                    CancellationToken.None)
                .ConfigureAwait(false);
            if (current == expectedSnapshot)
            {
                TryDeleteHygieneTemporaryFile(path);
            }
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or InvalidOperationException
                                   or NotSupportedException)
        {
            // A path that no longer identifies our exact candidate is retained rather than deleted.
        }
    }

    private static async Task<string> WriteTemporaryHygieneFileAsync(
        string path,
        string contents,
        HygieneFileSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(path)
                           ?? throw new InvalidOperationException(
                               $"The repository hygiene path '{path}' has no parent directory.");
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             new FileStreamOptions
                             {
                                 Mode = FileMode.CreateNew,
                                 Access = FileAccess.Write,
                                 Share = FileShare.None,
                                 Options = FileOptions.Asynchronous,
                             }))
            await using (var writer = new StreamWriter(
                             stream,
                             snapshot.IsLegacyEncoded
                                 ? Encoding.Latin1
                                 : new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                await writer.WriteAsync(contents.AsMemory(), cancellationToken)
                    .ConfigureAwait(false);
            }

            CopyHygieneFileMetadata(temporaryPath, snapshot);
            return temporaryPath;
        }
        catch
        {
            TryDeleteHygieneTemporaryFile(temporaryPath);
            throw;
        }
    }

    private static void CopyHygieneFileMetadata(
        string temporaryPath,
        HygieneFileSnapshot snapshot)
    {
        if (snapshot.Attributes is { } attributes)
        {
            File.SetAttributes(temporaryPath, attributes);
        }

        if (!OperatingSystem.IsWindows() && snapshot.UnixMode is { } unixMode)
        {
            File.SetUnixFileMode(temporaryPath, unixMode);
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
                return new HygieneFileSnapshot(
                    Exists: false,
                    Contents: null,
                    Attributes: null,
                    UnixMode: null);
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

            FileAttributes attributes = File.GetAttributes(path);
            UnixFileMode? unixMode = null;
            if (!OperatingSystem.IsWindows())
            {
                unixMode = File.GetUnixFileMode(path);
            }

            EnsureHygienePathIsSafe(path);
            return new HygieneFileSnapshot(
                Exists: true,
                contents,
                attributes,
                unixMode,
                isLegacyEncoded);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            EnsureHygienePathIsSafe(path);
            return new HygieneFileSnapshot(
                Exists: false,
                Contents: null,
                Attributes: null,
                UnixMode: null);
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

    private static bool TryDeleteVerifiedHygieneFile(string path)
    {
        try
        {
            File.Delete(path);
            return !Path.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private sealed record HygieneFileSnapshot(
        bool Exists,
        string? Contents,
        FileAttributes? Attributes,
        UnixFileMode? UnixMode,
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
