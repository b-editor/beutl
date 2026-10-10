namespace Beutl.Editor.VersionControl;

internal sealed partial class GitCliVersionControlService
{
    private sealed record IndexFileSnapshot(
        bool Exists,
        byte[] Contents,
        FileAttributes? Attributes,
        UnixFileMode? UnixMode,
        DateTime? LastWriteTimeUtc);

    private sealed class IndexRollbackAmbiguousException : InvalidOperationException
    {
        public IndexRollbackAmbiguousException(string message)
            : base(message)
        {
        }

        public IndexRollbackAmbiguousException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    private static async Task<IndexFileSnapshot> CaptureIndexFileSnapshotAsync(
        string indexPath,
        CancellationToken cancellationToken)
    {
        EnsureIndexPathIsRegular(indexPath);
        try
        {
            if (!File.Exists(indexPath))
            {
                return new IndexFileSnapshot(
                    Exists: false,
                    Contents: [],
                    Attributes: null,
                    UnixMode: null,
                    LastWriteTimeUtc: null);
            }

            byte[] contents = await File.ReadAllBytesAsync(indexPath, cancellationToken)
                .ConfigureAwait(false);
            FileAttributes attributes = File.GetAttributes(indexPath);
            UnixFileMode? unixMode = null;
            if (!OperatingSystem.IsWindows())
            {
                unixMode = File.GetUnixFileMode(indexPath);
            }

            EnsureIndexPathIsRegular(indexPath);
            return new IndexFileSnapshot(
                Exists: true,
                contents,
                attributes,
                unixMode,
                File.GetLastWriteTimeUtc(indexPath));
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            EnsureIndexPathIsRegular(indexPath);
            return new IndexFileSnapshot(
                Exists: false,
                Contents: [],
                Attributes: null,
                UnixMode: null,
                LastWriteTimeUtc: null);
        }
    }

    private static void EnsureIndexPathIsRegular(string path)
    {
        try
        {
            var file = new FileInfo(path);
            file.Refresh();
            if (file.Exists
                && (file.LinkTarget is not null
                    || (file.Attributes & FileAttributes.ReparsePoint) != 0))
            {
                throw new InvalidOperationException(
                    $"Git index path '{path}' must be a regular file.");
            }

            if (Directory.Exists(path))
            {
                throw new InvalidOperationException(
                    $"Git index path '{path}' must be a regular file.");
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
                $"The Git index path '{path}' could not be inspected safely.",
                ex);
        }
    }

    private static bool IndexBytesEqual(
        IndexFileSnapshot left,
        IndexFileSnapshot right)
    {
        return left.Exists == right.Exists
               && left.Contents.AsSpan().SequenceEqual(right.Contents)
               && left.Attributes == right.Attributes
               && left.UnixMode == right.UnixMode;
    }

    private static Task<IndexFileSnapshot> TransformIndexSnapshotAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string indexPath,
        IndexFileSnapshot beforeTransform,
        IReadOnlyList<string> arguments,
        GitCommandOptions options,
        string mismatchMessage,
        CancellationToken cancellationToken)
        => TransformIndexSnapshotAsync(
            repository,
            runner,
            indexPath,
            beforeTransform,
            [arguments],
            options,
            mismatchMessage,
            cancellationToken);

    private static async Task<IndexFileSnapshot> TransformIndexSnapshotAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string indexPath,
        IndexFileSnapshot beforeTransform,
        IReadOnlyList<IReadOnlyList<string>> commands,
        GitCommandOptions options,
        string mismatchMessage,
        CancellationToken cancellationToken)
    {
        string indexDirectory = Path.GetDirectoryName(indexPath)
                                ?? throw new InvalidOperationException(
                                    $"The Git index path '{indexPath}' has no parent directory.");
        string temporaryIndexPath = Path.Combine(
            indexDirectory,
            $".beutl-index-{Guid.NewGuid():N}.tmp");
        try
        {
            if (beforeTransform.Exists)
            {
                await using var stream = new FileStream(
                    temporaryIndexPath,
                    new FileStreamOptions
                    {
                        Mode = FileMode.CreateNew,
                        Access = FileAccess.Write,
                        Share = FileShare.None,
                        Options = FileOptions.Asynchronous,
                    });
                await stream.WriteAsync(beforeTransform.Contents, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (beforeTransform.LastWriteTimeUtc is { } lastWriteTimeUtc)
            {
                File.SetLastWriteTimeUtc(temporaryIndexPath, lastWriteTimeUtc);
            }

            var environmentOverrides = options.EnvironmentOverrides is null
                ? new Dictionary<string, string?>()
                : new Dictionary<string, string?>(options.EnvironmentOverrides);
            environmentOverrides["GIT_INDEX_FILE"] = temporaryIndexPath;
            GitCommandOptions temporaryIndexOptions = options with
            {
                EnvironmentOverrides = environmentOverrides,
            };
            foreach (IReadOnlyList<string> arguments in commands)
            {
                await runner.RunAsync(
                        repository,
                        arguments,
                        temporaryIndexOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            IndexFileSnapshot afterAdd = await CaptureIndexFileSnapshotAsync(
                    temporaryIndexPath,
                    cancellationToken)
                .ConfigureAwait(false);
            if (beforeTransform.Exists)
            {
                afterAdd = afterAdd with
                {
                    Attributes = beforeTransform.Attributes,
                    UnixMode = beforeTransform.UnixMode,
                };
            }
            await ApplyIndexSnapshotAsync(
                    indexPath,
                    expectedCurrent: beforeTransform,
                    replacement: afterAdd,
                    mismatchMessage: mismatchMessage)
                .ConfigureAwait(false);
            return afterAdd;
        }
        finally
        {
            TryDeleteTemporaryIndex(temporaryIndexPath);
        }
    }

    private static FileStream AcquireIndexLock(string indexPath)
    {
        string lockPath = indexPath + ".lock";
        try
        {
            return new FileStream(
                lockPath,
                new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.ReadWrite,
                    Share = FileShare.None,
                    Options = FileOptions.WriteThrough,
                });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new GitOperationException(
                128,
                $"Unable to acquire the worktree Git index lock '{lockPath}': {ex.Message}");
        }
    }

    private static async Task ApplyIndexSnapshotAsync(
        string indexPath,
        IndexFileSnapshot expectedCurrent,
        IndexFileSnapshot replacement,
        string mismatchMessage)
    {
        string lockPath = indexPath + ".lock";
        FileStream lockStream = AcquireIndexLock(indexPath);
        bool lockMoved = false;
        try
        {
            // The lockfile blocks Git's normal index writers for the entire compare/write/rename
            // sequence. A changed byte stream means another writer owns the index and this
            // operation must not overwrite it.
            IndexFileSnapshot current = await CaptureIndexFileSnapshotAsync(
                    indexPath,
                    CancellationToken.None)
                .ConfigureAwait(false);
            if (!IndexBytesEqual(current, expectedCurrent))
            {
                throw new IndexRollbackAmbiguousException(mismatchMessage);
            }

            await lockStream.WriteAsync(replacement.Contents, CancellationToken.None)
                .ConfigureAwait(false);
            await lockStream.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            lockStream.Flush(flushToDisk: true);
            lockStream.Dispose();

            if (replacement.Exists)
            {
                if (replacement.Attributes is { } attributes)
                {
                    File.SetAttributes(lockPath, attributes);
                }

                if (!OperatingSystem.IsWindows() && replacement.UnixMode is { } unixMode)
                {
                    File.SetUnixFileMode(lockPath, unixMode);
                }

                if (replacement.LastWriteTimeUtc is { } lastWriteTimeUtc)
                {
                    File.SetLastWriteTimeUtc(lockPath, lastWriteTimeUtc);
                }

                File.Move(lockPath, indexPath, overwrite: true);
            }
            else
            {
                // An absent replacement has no byte stream to rename. Keep the lockfile in place
                // while removing the index so compliant Git writers cannot recreate it between
                // the compare and delete operations.
                File.Delete(indexPath);
                File.Delete(lockPath);
            }

            lockMoved = true;
        }
        catch (IndexRollbackAmbiguousException)
        {
            throw;
        }
        catch (GitOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new IndexRollbackAmbiguousException(
                $"The Git index '{indexPath}' could not be changed without risking an overwrite.",
                ex);
        }
        finally
        {
            lockStream.Dispose();

            if (!lockMoved)
            {
                try
                {
                    File.Delete(lockPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Preserve the original ownership/operation failure. The lock is still
                    // visible to compliant Git writers until the caller can recover it.
                }
            }
        }
    }
}
