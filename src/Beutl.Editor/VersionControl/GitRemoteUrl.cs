using Beutl.Language;

namespace Beutl.Editor.VersionControl;

internal sealed class GitCredentialStorageException()
    : Exception(Strings.VersionControl_RemoteCredentialsSaveFailed)
{
}

internal sealed class GitRemoteUrl
{
    private readonly string? _username;
    private readonly string? _password;

    private GitRemoteUrl(string url, string? username = null, string? password = null)
    {
        Url = url;
        _username = username;
        _password = password;
    }

    public string Url { get; }

    public bool HasCredentials => _password is not null;

    public static GitRemoteUrl Parse(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        if (HasInvalidCredentialCharacters(url))
        {
            throw new ArgumentException("Remote URLs must not contain control characters.", nameof(url));
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            if (url.Contains("://", StringComparison.Ordinal))
            {
                throw new ArgumentException("The remote URL is invalid.", nameof(url));
            }

            return new GitRemoteUrl(url);
        }

        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ArgumentException(
                "Remote URLs must not embed credentials in a query or fragment. Use user:password@host or a Git credential helper instead.",
                nameof(url));
        }

        if (string.IsNullOrEmpty(uri.UserInfo))
        {
            return new GitRemoteUrl(url);
        }

        int separator = uri.UserInfo.IndexOf(':');
        if (uri.Scheme is ("http" or "https") && separator >= 0)
        {
            string username = Uri.UnescapeDataString(uri.UserInfo[..separator]);
            string password = Uri.UnescapeDataString(uri.UserInfo[(separator + 1)..]);
            if (HasInvalidCredentialCharacters(username) || HasInvalidCredentialCharacters(password))
            {
                throw new ArgumentException("Remote credentials must not contain control characters.", nameof(url));
            }

            var remote = new UriBuilder(uri) { UserName = string.Empty, Password = string.Empty };
            return new GitRemoteUrl(remote.Uri.AbsoluteUri, username, password);
        }

        if (Uri.UnescapeDataString(uri.UserInfo).Contains(':'))
        {
            throw new ArgumentException(
                "Remote URLs must not embed credentials. Configure a Git credential helper instead.",
                nameof(url));
        }

        if (uri.Scheme is not ("ssh" or "git+ssh"))
        {
            throw new ArgumentException(
                "Supply HTTP credentials as user:password@host, or configure a Git credential helper.",
                nameof(url));
        }

        return new GitRemoteUrl(url);
    }

    // Returns a helper to add to this URL's local configuration, only when none is configured.
    public async Task<string?> StoreCredentialsAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        if (!HasCredentials)
        {
            return null;
        }

        try
        {
            string? helper = await FindFallbackHelperAsync(repository, runner, cancellationToken)
                .ConfigureAwait(false);
            List<string> arguments =
            [
                "-c", "credential.interactive=false",
                "-c", "core.askpass=",
                // Keep the supplied account scoped to this repository, including its path.
                "-c", $"credential.{Url}.useHttpPath=true",
            ];
            if (helper is not null)
            {
                arguments.AddRange(["-c", "credential.helper=", "-c", $"credential.helper={helper}"]);
            }

            arguments.AddRange(["credential", "approve"]);
            GitCommandOptions options = GitCommandOptions.Local with
            {
                EnvironmentOverrides = new Dictionary<string, string?>
                {
                    ["GIT_ASKPASS"] = string.Empty,
                    ["SSH_ASKPASS"] = string.Empty,
                },
                // Secrets go through stdin, never through arguments or repository configuration.
                StandardInput = $"url={Url}\nusername={_username}\npassword={_password}\n\n",
                MaxStdoutBytes = 64 * 1024,
            };
            await runner.RunAsync(repository, arguments, options, cancellationToken).ConfigureAwait(false);

            // approve succeeds even when a helper fails to store anything. Check the same lookup
            // that the next push/fetch will use before reporting that the remote is configured.
            arguments[^1] = "fill";
            GitCommandResult saved = await runner.RunAsync(
                repository,
                arguments,
                options with { StandardInput = $"url={Url}\n\n" },
                cancellationToken).ConfigureAwait(false);
            string[] attributes = saved.Stdout.Split('\n')
                .Select(static line => line.TrimEnd('\r')).ToArray();
            if (saved.StdoutTruncated
                || !attributes.Contains($"username={_username}", StringComparer.Ordinal)
                || !attributes.Contains($"password={_password}", StringComparer.Ordinal))
            {
                throw new GitCredentialStorageException();
            }

            return helper;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Helpers may echo the stdin credentials in their diagnostics. Do not retain their
            // exception, stderr, or stdout in a notification or an inner exception.
            throw new GitCredentialStorageException();
        }
    }

    private async Task<string?> FindFallbackHelperAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        try
        {
            GitCommandResult configured = await runner.RunAsync(
                repository,
                ["config", "--null", "--get-urlmatch", "credential.helper", Url],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            if (configured.Stdout.TrimEnd('\0').Length > 0)
            {
                return null;
            }
        }
        catch (GitOperationException ex) when (ex.ExitCode == 1)
        {
        }

        GitCommandResult available = await runner.RunAsync(
            repository, ["help", "-a"], GitCommandOptions.Local, cancellationToken).ConfigureAwait(false);
        var commands = available.Stdout.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.Ordinal);
        GitCommandResult execPath = await runner.RunAsync(
            repository, ["--exec-path"], GitCommandOptions.Local, cancellationToken).ConfigureAwait(false);
        string helperDirectory = execPath.Stdout.Trim();
        foreach (string helper in new[] { "manager", "manager-core", "osxkeychain", "wincred", "libsecret" })
        {
            string executable = $"git-credential-{helper}{(OperatingSystem.IsWindows() ? ".exe" : "")}";
            // Apple's Git installs osxkeychain here without listing it in `git help -a`.
            if (commands.Contains($"credential-{helper}") || File.Exists(Path.Combine(helperDirectory, executable)))
            {
                return helper;
            }
        }

        if (!OperatingSystem.IsWindows() && commands.Contains("credential-cache"))
        {
            return "cache --timeout=3600";
        }

        throw new GitCredentialStorageException();
    }

    private static bool HasInvalidCredentialCharacters(string value)
        => value.IndexOfAny(['\0', '\r', '\n']) >= 0;
}
