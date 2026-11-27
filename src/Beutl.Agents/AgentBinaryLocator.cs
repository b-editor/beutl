using System.Diagnostics;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text.RegularExpressions;

namespace Beutl.Agents;

public sealed partial class AgentBinaryLocator(bool probeVersions = true)
{
    private static readonly string[] s_windowsExtensions = [".exe", ".cmd", ".bat"];
    private static readonly ConcurrentDictionary<string, VersionProbe> s_versions =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly Lazy<Task<string?>> _loginPath = new(ReadLoginPathAsync);

    public async Task<AgentProviderProbeResult> ProbeAsync(
        AgentProviderDescriptor provider, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (provider.Environment?.FirstOrDefault(pair =>
                string.Equals(pair.Key, "PATH", OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            is { Value: { } customPath })
        {
            path = customPath;
        }
        else if (!OperatingSystem.IsWindows())
        {
            string? loginPath = await _loginPath.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(loginPath) && loginPath != path)
                path = string.IsNullOrEmpty(path) ? loginPath : loginPath + Path.PathSeparator + path;
        }

        string? executable = Resolve(provider.Command, path);
        return new AgentProviderProbeResult(provider, executable, path,
            executable == null ? $"Could not find an executable for {provider.Command}." : null,
            executable is not null && probeVersions && provider.Kind != AgentProviderKind.Acp
                ? await ProbeVersionAsync(executable, provider, path, cancellationToken).ConfigureAwait(false)
                : null);
    }

    public async Task<bool?> CheckAuthenticationAsync(AgentProviderProbeResult probe, CancellationToken cancellationToken = default)
    {
        if (!probeVersions || probe.ExecutablePath is not { } executable)
            return null;

        string[]? arguments = probe.Provider.Kind switch
        {
            AgentProviderKind.ClaudeCode => ["auth", "status", "--text"],
            AgentProviderKind.Codex => ["login", "status"],
            _ => null
        };
        if (arguments is null)
            return null;

        var result = await RunCliProbeAsync(executable, probe.Provider, probe.SearchPath, arguments, cancellationToken)
            .ConfigureAwait(false);
        return result?.ExitCode switch { 0 => true, 1 => false, _ => null };
    }

    private static async Task<string?> ProbeVersionAsync(string executable, AgentProviderDescriptor provider,
        string? searchPath, CancellationToken cancellationToken)
    {
        DateTime lastWrite = File.GetLastWriteTimeUtc(executable);
        var probe = s_versions.AddOrUpdate(executable, _ => CreateProbe(), (_, previous) =>
            previous.LastWrite == lastWrite ? previous : CreateProbe());
        return await probe.Version.Value.WaitAsync(cancellationToken).ConfigureAwait(false);

        VersionProbe CreateProbe() => new(lastWrite, new Lazy<Task<string?>>(async () =>
        {
            var result = await RunCliProbeAsync(executable, provider, searchPath, ["--version"], CancellationToken.None)
                .ConfigureAwait(false);
            if (result is not { ExitCode: 0 } completed)
                return null;
            var match = CliVersionPattern().Match(completed.Output);
            return match.Success ? match.Groups["version"].Value : null;
        }));
    }

    private static async Task<(int ExitCode, string Output)?> RunCliProbeAsync(string executable,
        AgentProviderDescriptor provider, string? searchPath, string[] arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        if (provider.Environment is { } environment)
        {
            foreach (var (name, value) in environment)
                start.Environment[name] = value;
        }
        if (searchPath is not null)
            start.Environment["PATH"] = searchPath;

        if (OperatingSystem.IsWindows() && Path.GetExtension(executable) is { } extension
            && (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase)))
        {
            // Delayed expansion passes the path as data after cmd parses metacharacters.
            // The argument list comes exclusively from the built-in probe commands above.
            start.FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            start.Environment["BEUTL_AGENT_PROBE_EXECUTABLE"] = executable;
            start.Arguments = "/d /v:on /s /c \"\"!BEUTL_AGENT_PROBE_EXECUTABLE!\" " + string.Join(' ', arguments) + "\"";
        }
        else
        {
            foreach (string argument in arguments)
                start.ArgumentList.Add(argument);
        }

        var result = await RunProcessAsync(start, TimeSpan.FromSeconds(2), boundedOutput: true, cancellationToken)
            .ConfigureAwait(false);
        return result is { } completed ? (completed.ExitCode, completed.Stdout + "\n" + completed.Stderr) : null;
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)?> RunProcessAsync(
        ProcessStartInfo start, TimeSpan timeoutDuration, bool boundedOutput, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutDuration);
        try
        {
            timeout.Token.ThrowIfCancellationRequested();
            using var process = Process.Start(start);
            if (process is null)
                return null;

            try
            {
                if (start.RedirectStandardInput)
                    process.StandardInput.Close();
                var stdout = ReadOutputAsync(process.StandardOutput, boundedOutput, timeout.Token);
                var stderr = ReadOutputAsync(process.StandardError, boundedOutput, timeout.Token);
                await Task.WhenAll(process.WaitForExitAsync(timeout.Token), stdout, stderr).ConfigureAwait(false);
                return (process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
            }
            finally
            {
                TryKillProcessTree(process);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or IOException or OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
    }

    private static Task<string> ReadOutputAsync(StreamReader reader, bool bounded, CancellationToken cancellationToken)
        => bounded ? ReadProbeOutputAsync(reader, cancellationToken) : reader.ReadToEndAsync(cancellationToken);

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // The process exited while it was being terminated.
        }
    }

    private static async Task<string> ReadProbeOutputAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        int count = await reader.ReadBlockAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
        return new string(buffer, 0, count);
    }

    private sealed record VersionProbe(DateTime LastWrite, Lazy<Task<string?>> Version);

    [GeneratedRegex(@"(?m)^\s*(?:(?:codex(?:-cli)?|claude code)\s+)?v?(?<version>[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9A-Za-z.+-]+)?)(?=\s|$)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex CliVersionPattern();

    public static string? Resolve(string command, string? searchPath)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;

        if (Path.IsPathRooted(command) || command.Contains(Path.DirectorySeparatorChar) || command.Contains(Path.AltDirectorySeparatorChar))
            return ResolveCandidate(command);

        foreach (string entry in (searchPath ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string directory = entry.Trim('"');
            if (!Path.IsPathFullyQualified(directory))
                continue;

            if (ResolveCandidate(Path.Combine(directory, command)) is { } match)
                return match;
        }

        return null;
    }

    private static string? ResolveCandidate(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            if (s_windowsExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                return File.Exists(path) ? Path.GetFullPath(path) : null;

            foreach (string extension in s_windowsExtensions)
            {
                string candidate = path + extension;
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }
            return null;
        }

        if (!File.Exists(path))
            return null;

        try
        {
            const UnixFileMode executable = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            return (File.GetUnixFileMode(path) & executable) != 0 ? Path.GetFullPath(path) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static async Task<string?> ReadLoginPathAsync()
    {
        string? shell = Environment.GetEnvironmentVariable("SHELL");
        if (string.IsNullOrWhiteSpace(shell))
            shell = OperatingSystem.IsMacOS() ? "/bin/zsh" : "/bin/bash";

        var start = new ProcessStartInfo(shell)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            ArgumentList = { "-lc", "printf '\\036'; printenv PATH; printf '\\036'" }
        };

        var result = await RunProcessAsync(start, TimeSpan.FromSeconds(3), boundedOutput: false, CancellationToken.None)
            .ConfigureAwait(false);
        if (result is not { Stdout: var text })
            return null;

        int end = text.LastIndexOf('\u001e');
        int begin = end > 0 ? text.LastIndexOf('\u001e', end - 1) : -1;
        return begin >= 0 ? text[(begin + 1)..end].TrimEnd('\r', '\n') : null;
    }
}
