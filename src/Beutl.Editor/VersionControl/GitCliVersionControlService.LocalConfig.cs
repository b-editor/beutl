namespace Beutl.Editor.VersionControl;

internal sealed partial class GitCliVersionControlService
{
    private static async Task<GitIdentity?> GetIdentityCoreAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        string? name = await TryGetConfigValueAsync(
            repository,
            runner,
            "user.name",
            cancellationToken).ConfigureAwait(false);
        string? email = await TryGetConfigValueAsync(
            repository,
            runner,
            "user.email",
            cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email)
            ? null
            : new GitIdentity(name, email);
    }

    // Git locks the configuration for each value it writes. A failure between the two writes leaves the
    // name written.
    private static async Task SetLocalIdentityCoreAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        GitIdentity identity,
        CancellationToken cancellationToken)
    {
        await runner.RunAsync(
            repository,
            ["config", "--local", "--replace-all", "user.name", identity.Name],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        await runner.RunAsync(
            repository,
            ["config", "--local", "--replace-all", "user.email", identity.Email],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateIdentity(GitIdentity identity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.Email);
    }

    private static async Task<bool> GetLocalBooleanConfigAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string key,
        CancellationToken cancellationToken)
    {
        string? value = await TryGetConfigValueAsync(
            repository,
            runner,
            key,
            cancellationToken).ConfigureAwait(false);
        return bool.TryParse(value, out bool parsed) && parsed;
    }

    private static async Task SetLocalConfigValueAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string key,
        string value,
        CancellationToken cancellationToken)
    {
        await runner.RunAsync(
            repository,
            ["config", "--local", key, value],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string?> TryGetConfigValueAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string key,
        CancellationToken cancellationToken)
    {
        try
        {
            GitCommandResult result = await runner.RunAsync(
                repository,
                ["config", "--get", key],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            string value = result.Stdout.Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }
        catch (GitOperationException ex) when (ex.ExitCode == 1)
        {
            return null;
        }
    }
}
