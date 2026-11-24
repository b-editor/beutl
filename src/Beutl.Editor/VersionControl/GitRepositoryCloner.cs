using Beutl.Configuration;
using Beutl.Language;

namespace Beutl.Editor.VersionControl;

internal sealed class GitRepositoryCloner(
    GitInstallationLocator installation, Func<string, IGitCliRunner>? runnerFactory = null)
{
    public GitRepositoryCloner(VersionControlConfig config) : this(new GitInstallationLocator(config))
    {
    }

    public Task<GitAvailability> GetAvailabilityAsync(CancellationToken cancellationToken)
        => installation.LocateAsync(cancellationToken);

    public Task CloneAsync(
        string remoteUrl, string destination, string? revision = null,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        => CloneAsync(GitRemoteUrl.Parse(remoteUrl.Trim()), destination, revision, progress, cancellationToken);

    public async Task CloneAsync(
        GitRemoteUrl remote, string destination, string? revision = null,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (revision is not null && !GitRevisionValidator.IsWebRevision(revision))
            throw new ArgumentException(Strings.HostedGit_InvalidRevision, nameof(revision));

        destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        string parent = Path.GetDirectoryName(destination)
            ?? throw new ArgumentException(Strings.HostedGit_InvalidDestination, nameof(destination));
        bool existedEmpty = CheckDestination(destination);
        GitAvailability availability = await GetAvailabilityAsync(cancellationToken).ConfigureAwait(false);
        string gitPath = availability.GitPath
            ?? throw new InvalidOperationException(Strings.VersionControl_GitNotInstalled);
        IGitCliRunner runner = runnerFactory?.Invoke(gitPath) ?? new GitCliRunner(gitPath);
        string source = ResolveCloneSource(remote.Url);
        Directory.CreateDirectory(parent);

        // Stage on the destination volume. On failure only the staging area belongs to this operation.
        string stagingFolder = Path.Combine(parent, $".beutl-clone-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingFolder);
        try
        {
            string checkout = Path.Combine(stagingFolder, "repository");
            var workingDirectory = new RepositoryInfo(stagingFolder, stagingFolder);
            var environment = new Dictionary<string, string?> { ["GIT_CEILING_DIRECTORIES"] = parent };
            var local = GitCommandOptions.Local with { EnvironmentOverrides = environment };
            var network = GitCommandOptions.Network with { EnvironmentOverrides = environment };
            string? helper = await remote.StoreCredentialsAsync(workingDirectory, runner, cancellationToken, local)
                .ConfigureAwait(false);

            var arguments = new List<string>();
            if (remote.HasCredentials)
                remote.AddCredentialOptions(arguments, helper);

            arguments.AddRange(["clone", "--origin=origin", "--progress", "--no-hardlinks", "--no-checkout"]);
            const string headsPrefix = "refs/heads/";
            bool explicitBranch = revision?.StartsWith(headsPrefix, StringComparison.Ordinal) == true;
            string? branch = explicitBranch ? revision![headsPrefix.Length..] : revision;
            bool hexadecimalRevision = GitRevisionValidator.IsHexadecimalRevision(revision);
            bool checkoutRevision = !explicitBranch && revision is not null
                && (revision.StartsWith("refs/", StringComparison.Ordinal) || hexadecimalRevision);
            if (!checkoutRevision && branch is not null && (explicitBranch || branch != "HEAD"))
                arguments.Add($"--branch={branch}");

            arguments.AddRange(["--", source, checkout]);
            IProgress<string>? safeProgress = progress is null || !remote.HasCredentials
                ? progress
                : new RedactedProgress(remote, progress);
            await runner.RunAsync(workingDirectory, arguments, network, cancellationToken, safeProgress)
                .ConfigureAwait(false);

            var repository = new RepositoryInfo(checkout, checkout);
            if (explicitBranch)
            {
                // --branch can select a tag, which leaves HEAD detached.
                GitCommandResult selected = await runner.RunAsync(repository, ["branch", "--show-current"], local,
                    cancellationToken).ConfigureAwait(false);
                if (!string.Equals(selected.Stdout.TrimEnd('\r', '\n'), branch, StringComparison.Ordinal))
                    throw new InvalidOperationException(Strings.HostedGit_InvalidRevision);
            }
            if (remote.HasCredentials)
                await remote.ConfigureLocalCredentialsAsync(repository, runner, helper, cancellationToken, local)
                    .ConfigureAwait(false);

            string reference = "HEAD";
            string[] checkoutArguments = ["reset", "--hard"];
            if (checkoutRevision)
            {
                (reference, checkoutArguments) = await ResolveRevisionCheckoutAsync(repository, runner, revision!, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (availability.LfsInstalled)
            {
                // A clone does not inherit the source repository's local LFS filters.
                await runner.RunAsync(repository, ["lfs", "install", "--local", "--skip-repo"],
                    local, cancellationToken).ConfigureAwait(false);
            }
            else if (await GitCliVersionControlService.TryResolveCommitAsync(repository, runner, reference, cancellationToken)
                         .ConfigureAwait(false) is { } commit
                     && await GitCliVersionControlService.TargetContainsLfsPointerAsync(
                         repository, runner, commit, LfsPrefetchScope.RepositoryWide, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException(Strings.HostedGit_CloneRequiresLfs);
            }

            // Materialize all media before publishing the clone, including when other Git commands skip LFS.
            var checkoutEnvironment = new Dictionary<string, string?>(environment)
            {
                ["GIT_LFS_SKIP_SMUDGE"] = "0",
                ["GIT_LFS_SKIP_DOWNLOAD_ERRORS"] = "0",
            };
            await runner.RunAsync(repository,
                [.. GitCliVersionControlService.LfsPathFilterOverrides, "-c", "lfs.skipdownloaderrors=false", .. checkoutArguments],
                network with { EnvironmentOverrides = checkoutEnvironment }, cancellationToken, safeProgress).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            if (existedEmpty)
            {
                if (CheckDestination(destination))
                    Directory.Delete(destination, recursive: false);
            }
            try
            {
                Directory.Move(checkout, destination);
            }
            catch
            {
                if (existedEmpty && !Path.Exists(destination))
                    Directory.CreateDirectory(destination);
                throw;
            }
        }
        catch (GitOperationException ex) when (remote.HasCredentials)
        {
            throw new GitOperationException(ex.ExitCode, remote.RedactDiagnostics(ex.Stderr));
        }
        finally
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    // Git makes object files read-only. The clone owns these copies.
                    var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
                    foreach (string file in Directory.EnumerateFiles(stagingFolder, "*", options))
                    {
                        FileAttributes attributes = File.GetAttributes(file);
                        if (attributes.HasFlag(FileAttributes.ReadOnly))
                            File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
                    }
                }
                Directory.Delete(stagingFolder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Preserve the clone failure if Git, antivirus, or another process holds an open file.
            }
        }
    }

    private static async Task<(string Reference, string[] Arguments)> ResolveRevisionCheckoutAsync(
        RepositoryInfo repository, IGitCliRunner runner, string revision, CancellationToken cancellationToken)
    {
        if (GitRevisionValidator.IsAbbreviatedObjectId(revision))
        {
            // A short hex name can name a real branch as well as an abbreviated object ID.
            string branchRef = $"refs/remotes/origin/{revision}";
            if (await GitCliVersionControlService.RefExistsAsync(repository, runner, branchRef, cancellationToken)
                    .ConfigureAwait(false))
            {
                return (branchRef, ["switch", "-C", revision, "--track", "--", branchRef]);
            }
        }

        string hash = await GitCliVersionControlService.TryResolveHostedCommitAsync(repository, runner, revision, cancellationToken)
            .ConfigureAwait(false) ?? throw new InvalidOperationException(Strings.HostedGit_InvalidRevision);
        GitRevisionValidator.ValidateCommitId(hash, nameof(revision));
        return (hash, ["switch", "--detach", "--", hash]);
    }

    internal static bool CheckDestination(string destination)
    {
        if (!Path.Exists(destination))
            return false;

        var directory = new DirectoryInfo(destination);
        if (!directory.Exists
            || (directory.Attributes & FileAttributes.ReparsePoint) != 0
            || directory.EnumerateFileSystemInfos().Any())
            throw new IOException(Strings.HostedGit_DestinationExists);

        return true;
    }

    private static string ResolveCloneSource(string url)
    {
        // Local paths are relative to the caller, while host:path is Git's SSH syntax.
        int colon = url.IndexOf(':');
        int slash = url.IndexOfAny(['/', '\\']);
        return colon < 0 || (slash >= 0 && slash < colon) || Path.IsPathFullyQualified(url)
            ? Path.GetFullPath(url)
            : url;
    }

    internal static IReadOnlyList<string> FindProjectFiles(string directory, CancellationToken cancellationToken = default)
    {
        var files = new List<string>();
        var pending = new Queue<DirectoryInfo>();
        pending.Enqueue(new DirectoryInfo(directory));
        while (pending.TryDequeue(out DirectoryInfo? current))
        {
            cancellationToken.ThrowIfCancellationRequested();
            files.AddRange(current.EnumerateFiles()
                .Where(static file => file.Extension.Equals("." + EditorConstants.ProjectFileExtension, StringComparison.OrdinalIgnoreCase)
                    && (file.Attributes & FileAttributes.ReparsePoint) == 0)
                .OrderBy(static file => file.Name, StringComparer.OrdinalIgnoreCase)
                .Select(static file => file.FullName));

            foreach (DirectoryInfo child in current.EnumerateDirectories().OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (!child.Name.StartsWith('.') && (child.Attributes & (FileAttributes.Hidden | FileAttributes.ReparsePoint)) == 0)
                    pending.Enqueue(child);
            }
        }
        return files;
    }

    private sealed class RedactedProgress(GitRemoteUrl remote, IProgress<string> progress) : IProgress<string>
    {
        public void Report(string value) => progress.Report(remote.RedactDiagnostics(value));
    }
}
