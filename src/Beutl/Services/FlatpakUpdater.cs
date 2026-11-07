using System.Text.RegularExpressions;

namespace Beutl.Services;

internal sealed class FlatpakUpdater
{
    internal const long MaximumBundleBytes = 1024L * 1024 * 1024;
    private readonly string _appRef;
    private readonly string _appPath;
    private readonly Func<string[], CancellationToken, Task<string>> _runHost;
    private readonly Func<string, string?> _readBundleRef;

    internal static bool IsRunning => File.Exists("/.flatpak-info");
    internal static bool RequiresManualUpdate => !IsRunning && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FLATPAK_ID"));

    internal static FlatpakUpdater Create() => new(File.ReadAllText("/.flatpak-info"));

    internal FlatpakUpdater(string sandboxInfo,
        Func<string[], CancellationToken, Task<string>>? runHost = null,
        Func<string, string?>? readBundleRef = null)
    {
        _runHost = runHost ?? ((args, token) => RunHostAsync(args, token));
        _readBundleRef = readBundleRef ?? FlatpakBundleMetadata.ReadRef;
        // Flatpak writes these fields in [Instance]. Only paths need GKeyFile unescaping.
        var instance = sandboxInfo.Split('\n').Select(line => line.Trim())
            .SkipWhile(line => line != "[Instance]").Skip(1).TakeWhile(line => !line.StartsWith('['))
            .Select(line => line.Split('=', 2)).Where(pair => pair.Length == 2)
            .ToDictionary(pair => pair[0], pair => pair[1], StringComparer.Ordinal);
        string? arch = instance.GetValueOrDefault("arch");
        // Beutl publishes bundles for these architectures, as Flatpak names them.
        if (arch is not ("x86_64" or "aarch64") || instance.GetValueOrDefault("branch") != "master")
            throw new InvalidDataException(MessageStrings.DownloadFailed);
        // Update the installation that is running, never another architecture's copy of the app.
        _appRef = $"net.beditor.Beutl/{arch}/master";
        _appPath = Unescape(instance.GetValueOrDefault("original-app-path") ?? instance.GetValueOrDefault("app-path") ?? "");
        string root = Unescape(instance.GetValueOrDefault("instance-path") ?? "");
        if (!Path.IsPathFullyQualified(root) || !Path.IsPathFullyQualified(_appPath) || !_appPath.EndsWith("/files", StringComparison.Ordinal))
            throw new InvalidDataException(MessageStrings.DownloadFailed);
        string cache = Path.Combine(root, "cache");
        Directory.CreateDirectory(cache);
        DownloadPath = Path.Combine(cache, $"beutl-update-{Guid.NewGuid():N}.flatpak");
    }

    internal string DownloadPath { get; }

    internal async Task InstallAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_readBundleRef(DownloadPath) != "app/" + _appRef)
            throw new InvalidDataException(MessageStrings.DownloadFailed);
        string installations = await _runHost(["flatpak", "list", "--app", "--columns=ref,installation"], cancellationToken);
        foreach (string line in installations.Split('\n'))
        {
            string[] fields = line.Split('\t', StringSplitOptions.TrimEntries);
            if (fields.Length != 2 || fields[0] != _appRef) continue;
            string option = fields[1] switch
            {
                "user" => "--user",
                "system" => "--system",
                var name when name.StartsWith("system (", StringComparison.Ordinal) && name.EndsWith(')') => "--installation=" + name[8..^1],
                _ => throw new InvalidDataException(MessageStrings.DownloadFailed)
            };
            string location = (await _runHost(["flatpak", "info", option, "--show-location", _appRef], cancellationToken)).Trim();
            if (Path.GetDirectoryName(location) != Path.GetDirectoryName(Path.GetDirectoryName(_appPath))) continue;
            cancellationToken.ThrowIfCancellationRequested();
            string[] install = ["flatpak", "install", option, "--bundle", "--or-update", "--noninteractive", "--assumeyes", DownloadPath];
            // A system installation needs an administrator, whom flatpak itself cannot ask for here: up to
            // 1.18 both --noninteractive and --assumeyes forbid the polkit prompt, and without them it answers
            // its own "Proceed?" with no, having no terminal. pkexec asks instead and runs it as root.
            if (option != "--user") install = ["pkexec", .. install];
            try
            {
                await _runHost(install, cancellationToken);
            }
            // pkexec's codes for a dismissed prompt (126) and for authorization it could not get (127).
            catch (HostCommandException e) when (e.ExitCode is 126 or 127)
            {
                throw new UnauthorizedAccessException(MessageStrings.FlatpakUpdateNeedsAdministrator, e);
            }

            return;
        }
        throw new InvalidOperationException(MessageStrings.DownloadFailed);
    }

    private static string Unescape(string value) => Regex.Replace(value, @"\\(.)", match => match.Groups[1].Value switch
    {
        "s" => " ",
        "n" => "\n",
        "r" => "\r",
        "t" => "\t",
        "\\" => "\\",
        _ => throw new InvalidDataException(MessageStrings.DownloadFailed)
    });

    // Runs a command line on the host, outside the sandbox.
    internal static async Task<string> RunHostAsync(string[] command, CancellationToken cancellationToken,
        string executable = "/usr/bin/flatpak-spawn")
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            ArgumentList = { "--host", "--watch-bus", "--directory=/", "--env=LC_ALL=C" }
        };
        foreach (string argument in command) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        process.StandardInput.Close();
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(output, error);
            string message = (await error).Trim();
            if (process.ExitCode != 0)
                throw new HostCommandException(message.Length > 0 ? message : MessageStrings.DownloadFailed, process.ExitCode);
            return await output;
        }
        finally
        {
            // Closing flatpak-spawn's connection also stops the host command (--watch-bus), unless pkexec
            // already runs it as root.
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                await process.WaitForExitAsync(CancellationToken.None);
            }
            try { await Task.WhenAll(output, error); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        }
    }

    // A host command that failed, with the exit code that tells pkexec's outcomes apart.
    internal sealed class HostCommandException(string message, int exitCode) : IOException(message)
    {
        public int ExitCode { get; } = exitCode;
    }
}
