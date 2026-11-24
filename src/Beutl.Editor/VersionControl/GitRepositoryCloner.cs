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
        string gitPath = (await GetAvailabilityAsync(cancellationToken).ConfigureAwait(false)).GitPath
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

            arguments.AddRange(["clone", "--progress", "--no-hardlinks"]);
            const string headsPrefix = "refs/heads/";
            string? branch = revision?.StartsWith(headsPrefix, StringComparison.Ordinal) == true
                ? revision[headsPrefix.Length..]
                : revision;
            bool checkoutRevision = branch == revision && revision is not null
                && (revision.StartsWith("refs/", StringComparison.Ordinal)
                    || (revision.Length is 40 or 64 && revision.All(char.IsAsciiHexDigit)));
            if (checkoutRevision)
                arguments.Add("--no-checkout");
            else if (branch is not null and not "HEAD")
                arguments.Add($"--branch={branch}");

            arguments.AddRange(["--", source, checkout]);
            IProgress<string>? safeProgress = progress is null || !remote.HasCredentials
                ? progress
                : new RedactedProgress(remote, progress);
            await runner.RunAsync(workingDirectory, arguments, network, cancellationToken, safeProgress)
                .ConfigureAwait(false);

            var repository = new RepositoryInfo(checkout, checkout);
            if (remote.HasCredentials)
                await remote.ConfigureLocalCredentialsAsync(repository, runner, helper, cancellationToken, local)
                    .ConfigureAwait(false);
            if (checkoutRevision)
                await CheckoutRevisionAsync(repository, runner, revision!, network, safeProgress, cancellationToken)
                    .ConfigureAwait(false);

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

    private static async Task CheckoutRevisionAsync(
        RepositoryInfo repository, IGitCliRunner runner, string revision,
        GitCommandOptions options, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        string hash = (await runner.RunAsync(repository,
            ["rev-parse", "--verify", "--end-of-options", $"{revision}^{{commit}}"], GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false)).Stdout.Trim();
        GitRevisionValidator.ValidateCommitId(hash, nameof(revision));
        await runner.RunAsync(repository, ["switch", "--detach", "--", hash],
            options, cancellationToken, progress).ConfigureAwait(false);
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
