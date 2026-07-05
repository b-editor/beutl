using System.Diagnostics;

namespace Beutl.AgentToolkit.Installation;

public sealed record McpCliCommand(string Executable, IReadOnlyList<string> Arguments)
{
    public string ToDisplayString()
    {
        return string.Join(" ", new[] { Executable }.Concat(Arguments.Select(Quote)));
    }

    private static string Quote(string argument)
    {
        return argument.Contains(' ') || argument.Contains('"')
            ? "\"" + argument.Replace("\"", "\\\"") + "\""
            : argument;
    }
}

public sealed record McpCliResult(bool Success, int ExitCode, string Output);

/// <summary>
/// Builds `&lt;agent&gt; mcp add` command lines for agents whose MCP registry
/// cannot be edited as a JSON file (Codex's TOML config) or should not be
/// (Claude Code's app-managed ~/.claude.json).
/// </summary>
public static class AgentMcpCliCommands
{
    public static bool SupportsStdio(string agentId, AgentInstallScope scope)
    {
        return (agentId, scope) is ("claude-code", AgentInstallScope.Global)
            or ("codex", AgentInstallScope.Global);
    }

    public static bool SupportsRemote(string agentId, AgentInstallScope scope)
    {
        return (agentId, scope) is ("claude-code", AgentInstallScope.Global);
    }

    // `--env` / `--header` are variadic in the Claude CLI and swallow every
    // following argument, so the server name (and URL) must come first and
    // variadic options must be terminated by `--` or the end of the line.
    public static McpCliCommand? BuildStdio(
        string agentId,
        AgentInstallScope scope,
        string serverName,
        string command,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment)
    {
        return (agentId, scope) switch
        {
            ("claude-code", AgentInstallScope.Global) => new McpCliCommand(
                "claude",
                ["mcp", "add", "--scope", "user", serverName, .. EnvFlags(environment), "--", command, .. arguments]),
            ("codex", AgentInstallScope.Global) => new McpCliCommand(
                "codex",
                ["mcp", "add", serverName, .. EnvFlags(environment), "--", command, .. arguments]),
            _ => null,
        };
    }

    public static McpCliCommand? BuildRemote(
        string agentId,
        AgentInstallScope scope,
        string serverName,
        Uri url,
        IReadOnlyDictionary<string, string> headers)
    {
        return (agentId, scope) switch
        {
            ("claude-code", AgentInstallScope.Global) => new McpCliCommand(
                "claude",
                [
                    "mcp", "add", "--scope", "user", "--transport", "http",
                    serverName, url.ToString(), .. HeaderFlags(headers),
                ]),
            _ => null,
        };
    }

    // `mcp add` fails when the server name already exists, so installs run a
    // best-effort remove first to stay idempotent.
    public static McpCliCommand? BuildRemove(string agentId, AgentInstallScope scope, string serverName)
    {
        return (agentId, scope) switch
        {
            ("claude-code", AgentInstallScope.Global) => new McpCliCommand(
                "claude", ["mcp", "remove", "--scope", "user", serverName]),
            ("codex", AgentInstallScope.Global) => new McpCliCommand(
                "codex", ["mcp", "remove", serverName]),
            _ => null,
        };
    }

    private static IEnumerable<string> EnvFlags(IReadOnlyDictionary<string, string> environment)
    {
        foreach (KeyValuePair<string, string> pair in environment)
        {
            yield return "--env";
            yield return $"{pair.Key}={pair.Value}";
        }
    }

    private static IEnumerable<string> HeaderFlags(IReadOnlyDictionary<string, string> headers)
    {
        foreach (KeyValuePair<string, string> pair in headers)
        {
            yield return "--header";
            yield return $"{pair.Key}: {pair.Value}";
        }
    }
}

public static class McpCliRunner
{
    public static async Task<McpCliResult> RunAsync(McpCliCommand command, CancellationToken cancellationToken = default)
    {
        try
        {
            var startInfo = new ProcessStartInfo(command.Executable)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (string argument in command.Arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Could not start '{command.Executable}'.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));

            Task<string> stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);

            string output = string.Join(
                Environment.NewLine,
                new[] { await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false) }
                    .Where(text => !string.IsNullOrWhiteSpace(text))).Trim();
            return new McpCliResult(process.ExitCode == 0, process.ExitCode, output);
        }
        catch (Exception ex)
        {
            return new McpCliResult(false, -1, ex.Message);
        }
    }
}
