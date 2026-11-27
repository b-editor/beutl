using System.Diagnostics;

namespace Beutl.Agents;

public sealed class AgentBinaryLocator
{
    private static readonly string[] s_windowsExtensions = [".exe", ".cmd", ".bat"];
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
            executable == null ? $"Could not find an executable for {provider.Command}." : null);
    }

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

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            using var process = Process.Start(start);
            if (process == null)
                return null;

            try
            {
                var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
                var errors = process.StandardError.ReadToEndAsync(timeout.Token);
                await Task.WhenAll(process.WaitForExitAsync(timeout.Token), output, errors).ConfigureAwait(false);
                string text = await output.ConfigureAwait(false);
                int end = text.LastIndexOf('\u001e');
                int begin = end > 0 ? text.LastIndexOf('\u001e', end - 1) : -1;
                return begin >= 0 ? text[(begin + 1)..end].TrimEnd('\r', '\n') : null;
            }
            finally
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException or OperationCanceledException)
        {
            return null;
        }
    }
}
