using System.Text;

namespace Beutl.Editor.VersionControl;

internal sealed partial class GitCliVersionControlService
{
    private sealed record SnapshotCommit(
        string Commit,
        string Tree,
        string MessagePath,
        SnapshotIdentity Author,
        SnapshotIdentity Committer);

    private sealed record SnapshotIdentity(
        string Name,
        string Email,
        string Date);

    private enum CommitCleanupMode
    {
        Whitespace,
        Strip,
        Verbatim,
    }

    private sealed record SnapshotCommitSettings(
        SnapshotIdentity Author,
        SnapshotIdentity Committer,
        CommitCleanupMode CleanupMode,
        bool SignCommit);

    private async Task<SnapshotCommit?> CreateSnapshotCommitAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string tree,
        string? parentCommit,
        string message,
        SnapshotKind kind,
        CancellationToken cancellationToken)
    {
        string temporaryIndex = CreateUniqueTempPath("beutl-git-hook-index");
        string? messagePath = null;
        bool retainMessage = false;

        try
        {
            SnapshotCommitSettings settings = await ResolveSnapshotCommitSettingsAsync(
                    repository,
                    runner,
                    kind,
                    cancellationToken)
                .ConfigureAwait(false);
            string standardMessagePath = await ResolveGitPathAsync(
                    repository,
                    runner,
                    "COMMIT_EDITMSG",
                    cancellationToken)
                .ConfigureAwait(false);
            messagePath = Path.Combine(
                Path.GetDirectoryName(standardMessagePath)
                ?? throw new InvalidOperationException(
                    "The Git commit-message path has no parent directory."),
                $"beutl-commit-message-{Guid.NewGuid():N}.tmp");
            var hookEnvironment = new Dictionary<string, string?>
            {
                ["GIT_INDEX_FILE"] = temporaryIndex,
                ["GIT_EDITOR"] = ":",
                ["GIT_COMMIT_EDITMSG"] = messagePath,
                ["GIT_AUTHOR_NAME"] = settings.Author.Name,
                ["GIT_AUTHOR_EMAIL"] = settings.Author.Email,
                ["GIT_AUTHOR_DATE"] = settings.Author.Date,
                ["GIT_COMMITTER_NAME"] = settings.Committer.Name,
                ["GIT_COMMITTER_EMAIL"] = settings.Committer.Email,
                ["GIT_COMMITTER_DATE"] = settings.Committer.Date,
            };
            var hookOptions = new GitCommandOptions(
                GitCommandExecutionKind.Local,
                hookEnvironment);
            await RunSnapshotCommitHooksAsync(
                    repository,
                    runner,
                    tree,
                    message,
                    kind,
                    settings.CleanupMode,
                    messagePath,
                    hookOptions,
                    cancellationToken)
                .ConfigureAwait(false);

            GitCommandResult hookTreeResult = await runner.RunAsync(
                    repository,
                    ["write-tree"],
                    hookOptions,
                    cancellationToken)
                .ConfigureAwait(false);
            string hookTree = hookTreeResult.Stdout.Trim();
            GitRevisionValidator.ValidateCommitId(hookTree, nameof(hookTree));
            await ValidateHookModifiedSnapshotTreeAsync(
                    repository,
                    runner,
                    tree,
                    hookTree,
                    cancellationToken)
                .ConfigureAwait(false);
            bool isEmptyCommit = await IsEmptySnapshotCommitAsync(
                    repository,
                    runner,
                    hookTree,
                    parentCommit,
                    cancellationToken)
                .ConfigureAwait(false);

            if (isEmptyCommit)
            {
                if (kind == SnapshotKind.Init)
                {
                    throw new InvalidOperationException(
                        "A commit hook removed every change from the initial snapshot.");
                }

                return null;
            }

            List<string> arguments = CreateSnapshotCommitArguments(
                hookTree,
                parentCommit,
                settings.SignCommit,
                kind,
                messagePath);

            cancellationToken.ThrowIfCancellationRequested();
            // A signer may wait for a passphrase, so a signed commit runs without the local timeout and
            // stays cancellable. Nothing is published before the commit object exists.
            GitCommandResult commit = await runner.RunAsync(
                    repository,
                    arguments,
                    settings.SignCommit
                        ? hookOptions with { ExecutionKind = GitCommandExecutionKind.LocalUnbounded }
                        : hookOptions,
                    settings.SignCommit ? cancellationToken : CancellationToken.None)
                .ConfigureAwait(false);
            string commitId = commit.Stdout.Trim();
            GitRevisionValidator.ValidateCommitId(commitId, nameof(commitId));
            retainMessage = true;
            return new SnapshotCommit(
                commitId,
                hookTree,
                messagePath,
                settings.Author,
                settings.Committer);
        }
        finally
        {
            TryDeleteTemporaryIndex(temporaryIndex);
            if (!retainMessage && messagePath is not null)
            {
                TryDeleteTemporaryIndex(messagePath);
            }
        }
    }

    private static async Task<SnapshotCommitSettings> ResolveSnapshotCommitSettingsAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        SnapshotKind kind,
        CancellationToken cancellationToken)
    {
        SnapshotIdentity author = await ResolveSnapshotIdentityAsync(
                repository,
                runner,
                "GIT_AUTHOR_IDENT",
                "author",
                cancellationToken)
            .ConfigureAwait(false);
        SnapshotIdentity committer = await ResolveSnapshotIdentityAsync(
                repository,
                runner,
                "GIT_COMMITTER_IDENT",
                "committer",
                cancellationToken)
            .ConfigureAwait(false);
        CommitCleanupMode cleanupMode = await ResolveCommitCleanupModeAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);
        bool signCommit = kind == SnapshotKind.Manual
                          && await IsCommitSigningEnabledAsync(
                                  repository,
                                  runner,
                                  cancellationToken)
                              .ConfigureAwait(false);
        return new SnapshotCommitSettings(author, committer, cleanupMode, signCommit);
    }

    // Runs the hooks `git commit` would run on the snapshot and leaves the final message in messagePath.
    private static async Task RunSnapshotCommitHooksAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string tree,
        string message,
        SnapshotKind kind,
        CommitCleanupMode cleanupMode,
        string messagePath,
        GitCommandOptions hookOptions,
        CancellationToken cancellationToken)
    {
        byte[] initialMessage = await StripCommitMessageAsync(
                repository,
                runner,
                new UTF8Encoding(false).GetBytes(
                    CreateSnapshotCommitMessage(message, kind)),
                cleanupMode == CommitCleanupMode.Verbatim
                    ? CommitCleanupMode.Verbatim
                    : CommitCleanupMode.Whitespace,
                commentChar: null,
                hookOptions,
                cancellationToken)
            .ConfigureAwait(false);
        char commentChar = cleanupMode == CommitCleanupMode.Strip
            ? await ResolveCommitCommentCharAsync(
                    repository,
                    runner,
                    initialMessage,
                    cancellationToken)
                .ConfigureAwait(false)
            : '#';
        await runner.RunAsync(
                repository,
                ["read-tree", tree],
                hookOptions,
                cancellationToken)
            .ConfigureAwait(false);
        await RunCommitHookAsync(
                repository,
                runner,
                "pre-commit",
                [],
                hookOptions,
                cancellationToken)
            .ConfigureAwait(false);

        await WriteCommitMessageAsync(
                messagePath,
                initialMessage,
                createNew: true,
                cancellationToken)
            .ConfigureAwait(false);
        await RunCommitHookAsync(
                repository,
                runner,
                "prepare-commit-msg",
                [messagePath, "message"],
                hookOptions,
                cancellationToken)
            .ConfigureAwait(false);

        await RunCommitHookAsync(
                repository,
                runner,
                "commit-msg",
                [messagePath],
                hookOptions,
                cancellationToken)
            .ConfigureAwait(false);
        byte[] finalMessage = await StripCommitMessageAsync(
                repository,
                runner,
                await ReadCommitMessageAsync(messagePath, cancellationToken)
                    .ConfigureAwait(false),
                cleanupMode,
                commentChar,
                hookOptions,
                cancellationToken)
            .ConfigureAwait(false);
        if (IsEmptyCommitMessage(finalMessage))
        {
            throw new InvalidOperationException(
                "The snapshot commit message was empty after commit hooks ran.");
        }

        finalMessage = await EnsureSnapshotTrailerAsync(
                repository,
                runner,
                finalMessage,
                kind,
                hookOptions,
                cancellationToken)
            .ConfigureAwait(false);

        await WriteCommitMessageAsync(
                messagePath,
                finalMessage,
                createNew: false,
                cancellationToken)
            .ConfigureAwait(false);
    }

    // A root snapshot is empty when the hooks left no entry; a later one when its tree equals its parent's.
    private static async Task<bool> IsEmptySnapshotCommitAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string hookTree,
        string? parentCommit,
        CancellationToken cancellationToken)
    {
        if (parentCommit is null)
        {
            GitCommandResult entries = await runner.RunAsync(
                    repository,
                    ["ls-tree", "-r", "-z", hookTree],
                    GitCommandOptions.Local with { MaxStdoutBytes = 1 },
                    cancellationToken)
                .ConfigureAwait(false);
            return !entries.StdoutTruncated && entries.Stdout.Length == 0;
        }

        string parentTree = await ResolveTreeAsync(
                repository,
                runner,
                parentCommit,
                cancellationToken)
            .ConfigureAwait(false);
        return string.Equals(
            hookTree,
            parentTree,
            StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> CreateSnapshotCommitArguments(
        string hookTree,
        string? parentCommit,
        bool signCommit,
        SnapshotKind kind,
        string messagePath)
    {
        var arguments = new List<string>
        {
            "commit-tree",
            hookTree,
        };
        if (parentCommit is not null)
        {
            arguments.Add("-p");
            arguments.Add(parentCommit);
        }

        if (signCommit)
        {
            arguments.Add("-S");
        }
        else if (kind != SnapshotKind.Manual)
        {
            arguments.Add("--no-gpg-sign");
        }

        arguments.Add("-F");
        arguments.Add(messagePath);
        return arguments;
    }

    private static bool IsRegularFileMode(string? mode)
    {
        return mode is "100644" or "100755";
    }

    private static string CreateSnapshotCommitMessage(string message, SnapshotKind kind)
    {
        return $"{message}\n\nBeutl-Snapshot: {kind.ToString().ToLowerInvariant()}\n";
    }

    private static async Task<byte[]> ReadCommitMessageAsync(
        string messagePath,
        CancellationToken cancellationToken)
    {
        var file = new FileInfo(messagePath);
        file.Refresh();
        if (!file.Exists || file.Length > MaxCommitMessageBytes)
        {
            throw new InvalidOperationException(
                "The snapshot commit hook produced an invalid commit message file.");
        }

        await using var stream = new FileStream(
            messagePath,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            });
        var contents = new byte[MaxCommitMessageBytes + 1];
        int count = 0;
        while (count < contents.Length)
        {
            int read = await stream.ReadAsync(contents.AsMemory(count), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            count += read;
        }

        if (count > MaxCommitMessageBytes)
        {
            throw new InvalidOperationException(
                "The snapshot commit hook produced an invalid commit message file.");
        }

        Array.Resize(ref contents, count);
        return contents;
    }

    private static async Task WriteCommitMessageAsync(
        string messagePath,
        byte[] contents,
        bool createNew,
        CancellationToken cancellationToken)
    {
        if (contents.Length > MaxCommitMessageBytes)
        {
            throw new InvalidOperationException(
                "The snapshot commit message exceeded its safety limit.");
        }

        await using var stream = new FileStream(
            messagePath,
            new FileStreamOptions
            {
                Mode = createNew ? FileMode.CreateNew : FileMode.Create,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
            });
        await stream.WriteAsync(contents, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<byte[]> StripCommitMessageAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        byte[] message,
        CommitCleanupMode cleanupMode,
        char? commentChar,
        GitCommandOptions options,
        CancellationToken cancellationToken)
    {
        if (message.Length > MaxCommitMessageBytes)
        {
            throw new InvalidOperationException(
                "The snapshot commit message exceeded its safety limit.");
        }

        if (cleanupMode == CommitCleanupMode.Verbatim)
        {
            return message;
        }

        IReadOnlyList<string> arguments = cleanupMode == CommitCleanupMode.Strip
            ? ["-c", $"core.commentChar={commentChar ?? '#'}", "stripspace", "--strip-comments"]
            : ["stripspace"];
        GitCommandResult stripped = await runner.RunAsync(
                repository,
                arguments,
                options with
                {
                    MaxStdoutBytes = MaxCommitMessageBytes,
                    StandardInputBytes = message,
                    CaptureStdoutBytes = true,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (stripped.StdoutTruncated)
        {
            throw new InvalidOperationException(
                "The snapshot commit message exceeded its safety limit.");
        }

        return stripped.StdoutBytes
               ?? throw new InvalidOperationException(
                   "Git did not return the cleaned snapshot commit message bytes.");
    }

    private static async Task<byte[]> EnsureSnapshotTrailerAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        byte[] message,
        SnapshotKind kind,
        GitCommandOptions options,
        CancellationToken cancellationToken)
    {
        string expectedValue = kind.ToString().ToLowerInvariant();
        (int count, bool matchesExpectedValue, byte[] otherTrailers)
            = await ParseSnapshotTrailersAsync(
                repository,
                runner,
                message,
                expectedValue,
                options,
                cancellationToken)
            .ConfigureAwait(false);
        if (count == 1 && matchesExpectedValue)
        {
            return message;
        }

        byte[] canonicalTrailer = Encoding.UTF8.GetBytes(
            $"Beutl-Snapshot: {expectedValue}\n");
        int interTrailerNewline = otherTrailers.Length > 0
                                 && otherTrailers[^1] != (byte)'\n'
            ? 1
            : 0;
        int appendedLength = 2
                             + otherTrailers.Length
                             + interTrailerNewline
                             + canonicalTrailer.Length;
        if (message.Length > MaxCommitMessageBytes - appendedLength)
        {
            throw new InvalidOperationException(
                "The snapshot commit message exceeded its safety limit.");
        }

        var reconstructed = new byte[message.Length + appendedLength];
        int offset = 0;
        message.CopyTo(reconstructed, offset);
        offset += message.Length;
        reconstructed[offset++] = (byte)'\n';
        reconstructed[offset++] = (byte)'\n';
        otherTrailers.CopyTo(reconstructed, offset);
        offset += otherTrailers.Length;
        if (interTrailerNewline != 0)
        {
            reconstructed[offset++] = (byte)'\n';
        }

        canonicalTrailer.CopyTo(reconstructed, offset);
        (int reconstructedCount, bool reconstructedMatches, byte[] reconstructedOtherTrailers)
            = await ParseSnapshotTrailersAsync(
                repository,
                runner,
                reconstructed,
                expectedValue,
                options,
                cancellationToken)
            .ConfigureAwait(false);
        if (reconstructedCount != 1
            || !reconstructedMatches
            || !reconstructedOtherTrailers.AsSpan().SequenceEqual(otherTrailers))
        {
            throw new InvalidOperationException(
                "The snapshot commit message did not retain its required trailer after commit hooks ran.");
        }

        return reconstructed;
    }

    private static async Task<(int Count, bool MatchesExpectedValue, byte[] OtherTrailers)>
        ParseSnapshotTrailersAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        byte[] message,
        string expectedValue,
        GitCommandOptions options,
        CancellationToken cancellationToken)
    {
        GitCommandResult parsed = await runner.RunAsync(
                repository,
                [
                    "-c",
                    "trailer.separators=:=",
                    "interpret-trailers",
                    "--parse",
                    "--no-divider",
                ],
                options with
                {
                    MaxStdoutBytes = MaxCommitMessageBytes,
                    StandardInputBytes = message,
                    CaptureStdoutBytes = true,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (parsed.StdoutTruncated)
        {
            throw new InvalidOperationException(
                "The snapshot commit message exceeded its safety limit.");
        }

        return ParseSnapshotTrailerBytes(
            parsed.StdoutBytes
            ?? throw new InvalidOperationException(
                "Git did not return the parsed snapshot commit trailers."),
            expectedValue);
    }

    private static (int Count, bool MatchesExpectedValue, byte[] OtherTrailers)
        ParseSnapshotTrailerBytes(byte[] trailers, string expectedValue)
    {
        const string SnapshotTrailerToken = "Beutl-Snapshot";
        int snapshotCount = 0;
        bool matchesExpectedValue = false;
        using var otherTrailers = new MemoryStream(trailers.Length);
        int offset = 0;
        while (offset < trailers.Length)
        {
            int newline = Array.IndexOf(trailers, (byte)'\n', offset);
            int recordEnd = newline >= 0 ? newline + 1 : trailers.Length;
            int contentLength = (newline >= 0 ? newline : trailers.Length) - offset;
            if (contentLength > 0 && trailers[offset + contentLength - 1] == (byte)'\r')
            {
                contentLength--;
            }

            string line = Encoding.UTF8.GetString(trailers, offset, contentLength);
            int separator = line.IndexOf(':');
            bool isSnapshotTrailer = separator >= 0
                                     && string.Equals(
                                         line[..separator].Trim(),
                                         SnapshotTrailerToken,
                                         StringComparison.OrdinalIgnoreCase);
            if (isSnapshotTrailer)
            {
                snapshotCount++;
                matchesExpectedValue = string.Equals(
                    line[(separator + 1)..].Trim(),
                    expectedValue,
                    StringComparison.Ordinal);
            }
            else
            {
                otherTrailers.Write(trailers, offset, recordEnd - offset);
            }

            offset = recordEnd;
        }

        return (
            snapshotCount,
            snapshotCount == 1 && matchesExpectedValue,
            otherTrailers.ToArray());
    }

    private static bool IsEmptyCommitMessage(byte[] message)
    {
        // Git runs under LC_ALL=C and treats only ASCII whitespace as empty. Non-ASCII bytes are
        // message content regardless of i18n.commitEncoding.
        return message.All(static value => value is (byte)' '
            or (byte)'\t'
            or (byte)'\r'
            or (byte)'\n'
            or (byte)'\v'
            or (byte)'\f');
    }

    private static async Task<char> ResolveCommitCommentCharAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        byte[] initialMessage,
        CancellationToken cancellationToken)
    {
        string configured;
        try
        {
            GitCommandResult result = await runner.RunAsync(
                    repository,
                    ["config", "--get", "core.commentChar"],
                    GitCommandOptions.Local,
                    cancellationToken)
                .ConfigureAwait(false);
            configured = result.Stdout.TrimEnd('\r', '\n');
        }
        catch (GitOperationException ex) when (ex.ExitCode == 1)
        {
            return '#';
        }

        if (!string.Equals(configured, "auto", StringComparison.OrdinalIgnoreCase))
        {
            if (configured.Length != 1 || char.IsControl(configured[0]))
            {
                throw new InvalidOperationException(
                    "Git returned an invalid core.commentChar value.");
            }

            return configured[0];
        }

        const string Candidates = "#;@!$%^&|:";
        foreach (char candidate in Candidates)
        {
            byte candidateByte = checked((byte)candidate);
            bool startsLine = false;
            for (int i = 0; i < initialMessage.Length; i++)
            {
                if ((i == 0 || initialMessage[i - 1] == (byte)'\n')
                    && initialMessage[i] == candidateByte)
                {
                    startsLine = true;
                    break;
                }
            }

            if (!startsLine)
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            "Git could not select an automatic commit comment character.");
    }

    private static async Task<CommitCleanupMode> ResolveCommitCleanupModeAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        try
        {
            GitCommandResult result = await runner.RunAsync(
                    repository,
                    ["config", "--get", "commit.cleanup"],
                    GitCommandOptions.Local,
                    cancellationToken)
                .ConfigureAwait(false);
            return result.Stdout.Trim().ToLowerInvariant() switch
            {
                "" or "default" or "whitespace" or "scissors" =>
                    CommitCleanupMode.Whitespace,
                "strip" => CommitCleanupMode.Strip,
                "verbatim" => CommitCleanupMode.Verbatim,
                var value => throw new InvalidOperationException(
                    $"Git returned an unsupported commit.cleanup value: '{value}'."),
            };
        }
        catch (GitOperationException ex) when (ex.ExitCode == 1)
        {
            return CommitCleanupMode.Whitespace;
        }
    }

    private static async Task<SnapshotIdentity> ResolveSnapshotIdentityAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string variable,
        string description,
        CancellationToken cancellationToken)
    {
        GitCommandResult result = await runner.RunAsync(
                repository,
                ["var", variable],
                GitCommandOptions.Local,
                cancellationToken)
            .ConfigureAwait(false);
        string ident = result.Stdout.TrimEnd('\r', '\n');
        int closeBracket = ident.LastIndexOf('>');
        int openBracket = closeBracket < 0
            ? -1
            : ident.LastIndexOf('<', closeBracket);
        string name = openBracket <= 0 ? string.Empty : ident[..openBracket].TrimEnd();
        string email = openBracket < 0 || closeBracket <= openBracket
            ? string.Empty
            : ident[(openBracket + 1)..closeBracket];
        string date = closeBracket < 0 ? string.Empty : ident[(closeBracket + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(name)
            || string.IsNullOrWhiteSpace(email)
            || string.IsNullOrWhiteSpace(date)
            || name.Any(char.IsControl)
            || email.Any(char.IsControl)
            || date.Any(char.IsControl))
        {
            throw new InvalidOperationException(
                $"Git returned an invalid snapshot {description} identity.");
        }

        return new SnapshotIdentity(name, email, date);
    }

    private static Task RunCommitHookAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string hookName,
        IReadOnlyList<string> hookArguments,
        GitCommandOptions options,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            "hook",
            "run",
            "--ignore-missing",
            hookName,
        };
        if (hookArguments.Count > 0)
        {
            arguments.Add("--");
            arguments.AddRange(hookArguments);
        }

        return RunHookCoreAsync();

        async Task RunHookCoreAsync()
        {
            // Hooks are the user's own programs and can legitimately run long, so only cancellation
            // stops them, as with git commit.
            await runner.RunAsync(
                    repository,
                    arguments,
                    options with { ExecutionKind = GitCommandExecutionKind.LocalUnbounded },
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task ValidateHookModifiedSnapshotTreeAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string originalTree,
        string hookTree,
        CancellationToken cancellationToken)
    {
        await EnsureSnapshotTreeContainsNoGitlinksAsync(
                repository,
                runner,
                hookTree,
                cancellationToken)
            .ConfigureAwait(false);
        if (string.Equals(originalTree, hookTree, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        GitCommandResult changed = await runner.RunAsync(
                repository,
                [
                    "diff-tree",
                    "--no-commit-id",
                    "--name-only",
                    "-r",
                    "-z",
                    originalTree,
                    hookTree,
                ],
                GitCommandOptions.Local with
                {
                    MaxStdoutBytes = MaxSnapshotTreeInspectionBytes,
                },
                cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<string> changedPaths = GitCliRunner.SplitNullSeparated(changed.Stdout);
        if (changed.StdoutTruncated
            || changedPaths.Any(path => !IsHookWritableSnapshotPath(repository, path)))
        {
            throw new InvalidOperationException(
                "A commit hook changed content outside the safe project snapshot scope.");
        }

        GitCommandResult entries = await runner.RunAsync(
                repository,
                ["ls-tree", "-r", "-z", hookTree, "--", repository.Pathspec],
                GitCommandOptions.Local with
                {
                    MaxStdoutBytes = MaxSnapshotTreeInspectionBytes,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (entries.StdoutTruncated)
        {
            throw new InvalidOperationException(
                "A commit hook produced a project tree that exceeded its safety limit.");
        }

        var finalModes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string entry in GitCliRunner.SplitNullSeparated(entries.Stdout))
        {
            int metadataSeparator = entry.IndexOf('\t');
            int modeSeparator = entry.IndexOf(' ');
            if (metadataSeparator <= 0 || modeSeparator <= 0 || modeSeparator > metadataSeparator)
            {
                throw new InvalidOperationException(
                    "A commit hook produced an invalid project tree entry.");
            }

            finalModes[entry[(metadataSeparator + 1)..]] = entry[..modeSeparator];
        }

        if (changedPaths.Any(path =>
                !finalModes.TryGetValue(path, out string? mode)
                || !IsRegularFileMode(mode)))
        {
            throw new InvalidOperationException(
                "A commit hook removed content or introduced a non-regular project tree entry.");
        }

        EnsureHookKeptProjectFile(repository, finalModes);
    }

    private static bool IsHookWritableSnapshotPath(
        RepositoryInfo repository,
        string repositoryRelativePath)
    {
        string projectRelativePath;
        if (repository.Pathspec == ".")
        {
            projectRelativePath = repositoryRelativePath;
        }
        else
        {
            string prefix = repository.Pathspec + "/";
            if (!repositoryRelativePath.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            projectRelativePath = repositoryRelativePath[prefix.Length..];
        }

        if (projectRelativePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(static segment => string.Equals(
                segment,
                ".beutl",
                StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return !IsTemporaryProjectFile(projectRelativePath);
    }

    private void EnsureHookKeptProjectFile(
        RepositoryInfo repository,
        IReadOnlyDictionary<string, string> finalModes)
    {
        if (_projectFile is null
            || !RepositoryPathComparer.IsContainedWithin(repository.ProjectRoot, _projectFile))
        {
            return;
        }

        string projectFileRepositoryPath = GetRepositoryRelativeProjectFilePath(
            repository,
            _projectFile);
        if (!finalModes.TryGetValue(projectFileRepositoryPath, out string? projectMode)
            || !IsRegularFileMode(projectMode))
        {
            throw new InvalidOperationException(
                "A commit hook removed the project file from the snapshot tree.");
        }
    }

    private static async Task<bool> IsCommitSigningEnabledAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        try
        {
            GitCommandResult result = await runner.RunAsync(
                    repository,
                    ["config", "--bool", "--get", "commit.gpgSign"],
                    GitCommandOptions.Local,
                    cancellationToken)
                .ConfigureAwait(false);
            return result.Stdout.Trim() switch
            {
                "true" => true,
                "false" or "" => false,
                var value => throw new InvalidOperationException(
                    $"Git returned an invalid commit.gpgSign value: '{value}'."),
            };
        }
        catch (GitOperationException ex) when (ex.ExitCode == 1)
        {
            return false;
        }
    }
}
