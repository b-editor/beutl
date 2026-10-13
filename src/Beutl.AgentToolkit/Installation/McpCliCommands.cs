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
/// Builds `claude mcp add`/`remove` command lines for Claude Code's user scope, whose
/// ~/.claude.json is app-managed and is not edited as a file. The Beutl entry is a stdio
/// launcher that also forwards to running editors.
/// </summary>
public static class AgentMcpCliCommands
{
    public static bool SupportsStdio(string agentId, AgentInstallScope scope)
    {
        return (agentId, scope) is ("claude-code", AgentInstallScope.Global);
    }

    // `--env` is variadic in the Claude CLI and swallows every following
    // argument, so the server name must come first and the option list must
    // be terminated by `--` before the command.
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
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Disposing Process only releases the handle; kill the tree so a hung CLI does not
                // linger and hold agent-config locks after this returns failure.
                KillProcessTree(process);
                return new McpCliResult(false, -1, $"'{command.Executable}' timed out after 60s.");
            }

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

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception)
        {
            // The process already exited or cannot be signalled; nothing left to clean up.
        }
    }
}
