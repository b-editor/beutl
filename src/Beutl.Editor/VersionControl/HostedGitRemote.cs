using System.Reflection;
using Beutl.Api;

namespace Beutl.Editor.VersionControl;

internal static class HostedGitRemote
{
    private const string Prefix = "/api/v3/git/";

    public static bool TryParse(string? value, out Guid repositoryId)
    {
        repositoryId = default;
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            || !Uri.TryCreate(BeutlApiApplication.BaseUrl, UriKind.Absolute, out Uri? service)
            || uri.Scheme != service.Scheme
            || uri.Host != service.Host
            || uri.Port != service.Port
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !uri.AbsolutePath.StartsWith(Prefix, StringComparison.Ordinal)
            || !uri.AbsolutePath.EndsWith(".git", StringComparison.Ordinal))
        {
            return false;
        }

        string id = uri.AbsolutePath[Prefix.Length..^4];
        return Guid.TryParseExact(id, "D", out repositoryId)
               && string.Equals(id, repositoryId.ToString("D"), StringComparison.Ordinal);
    }

    public static GitCommandOptions CreateOptions(string remoteUrl, string token, GitCommandOptions baseline)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        if (!TryParse(remoteUrl, out _))
            throw new ArgumentException("The remote is not a Beutl hosted Git repository.", nameof(remoteUrl));
        if (token.Contains('\r') || token.Contains('\n'))
            throw new ArgumentException("Invalid Git token.", nameof(token));

        // Git and git-lfs inherit these settings for this process only. Never
        // write the bearer to .git/config or include it in process arguments.
        string executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("The Beutl executable path is unavailable.");
        string args = "--git-lfs-transfer";
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            string assembly = Assembly.GetEntryAssembly()?.Location
                ?? throw new InvalidOperationException("The Beutl assembly path is unavailable.");
            args = $"\"{assembly}\" {args}";
        }
        var values = new (string Key, string Value)[]
        {
            ($"http.{remoteUrl}.extraheader", $"Authorization: Bearer {token}"),
            ("lfs.customtransfer.beutl-r2-multipart.path", executable),
            ("lfs.customtransfer.beutl-r2-multipart.args", args),
            ("lfs.customtransfer.beutl-r2-multipart.concurrent", "false"),
        };
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["GIT_CONFIG_COUNT"] = values.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["GIT_TRACE_CURL"] = null,
            ["GIT_CURL_VERBOSE"] = null,
        };
        for (int index = 0; index < values.Length; index++)
        {
            environment[$"GIT_CONFIG_KEY_{index}"] = values[index].Key;
            environment[$"GIT_CONFIG_VALUE_{index}"] = values[index].Value;
        }
        return baseline with { EnvironmentOverrides = environment };
    }
}
