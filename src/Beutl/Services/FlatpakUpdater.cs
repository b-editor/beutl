using System.Text.RegularExpressions;

namespace Beutl.Services;

internal sealed class FlatpakUpdater
{
    internal const long MaximumBundleBytes = 1024L * 1024 * 1024;
    private const string AppRef = "net.beditor.Beutl/x86_64/master";
    private readonly string _appPath;

    internal static bool IsRunning => File.Exists("/.flatpak-info")
        || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FLATPAK_ID"));

    internal static FlatpakUpdater Create() => new(File.ReadAllText("/.flatpak-info"));

    internal FlatpakUpdater(string sandboxInfo)
    {
        // Flatpak writes these fields in [Instance]. Only paths need GKeyFile unescaping.
        var instance = sandboxInfo.Split('\n').Select(line => line.Trim())
            .SkipWhile(line => line != "[Instance]").Skip(1).TakeWhile(line => !line.StartsWith('['))
            .Select(line => line.Split('=', 2)).Where(pair => pair.Length == 2)
            .ToDictionary(pair => pair[0], pair => pair[1], StringComparer.Ordinal);
        if (instance.GetValueOrDefault("arch") != "x86_64" || instance.GetValueOrDefault("branch") != "master")
            throw new InvalidDataException(MessageStrings.DownloadFailed);
        _appPath = Unescape(instance.GetValueOrDefault("original-app-path") ?? instance.GetValueOrDefault("app-path") ?? "");
        string root = Unescape(instance.GetValueOrDefault("instance-path") ?? "");
        if (!Path.IsPathFullyQualified(root) || !Path.IsPathFullyQualified(_appPath) || !_appPath.EndsWith("/files", StringComparison.Ordinal))
            throw new InvalidDataException(MessageStrings.DownloadFailed);
        string cache = Path.Combine(root, "cache");
        Directory.CreateDirectory(cache);
        DownloadPath = Path.Combine(cache, $"beutl-update-{Guid.NewGuid():N}.flatpak");
    }

    internal string DownloadPath { get; }

    internal async Task InstallAsync(CancellationToken cancellationToken,
        Func<string[], CancellationToken, Task<string>>? runHost = null,
        Func<string, string?>? readBundleRef = null)
    {
        runHost ??= RunHostAsync;
        readBundleRef ??= FlatpakBundleMetadata.ReadRef;
        cancellationToken.ThrowIfCancellationRequested();
        if (readBundleRef(DownloadPath) != "app/" + AppRef)
            throw new InvalidDataException(MessageStrings.DownloadFailed);
        string installations = await runHost(["list", "--app", "--columns=ref,installation"], cancellationToken);
        foreach (string line in installations.Split('\n'))
        {
            string[] fields = line.Split('\t', StringSplitOptions.TrimEntries);
            if (fields.Length != 2 || fields[0] != AppRef) continue;
            string option = fields[1] switch
            {
                "user" => "--user",
                "system" => "--system",
                var name when name.StartsWith("system (", StringComparison.Ordinal) && name.EndsWith(')') => "--installation=" + name[8..^1],
                _ => throw new InvalidDataException(MessageStrings.DownloadFailed)
            };
            string location = (await runHost(["info", option, "--show-location", AppRef], cancellationToken)).Trim();
            if (Path.GetDirectoryName(location) != Path.GetDirectoryName(Path.GetDirectoryName(_appPath))) continue;
            cancellationToken.ThrowIfCancellationRequested();
            await runHost(["install", option, "--bundle", "--or-update", "--noninteractive", "--assumeyes", DownloadPath], cancellationToken);
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

    private static async Task<string> RunHostAsync(string[] arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startInfo = new ProcessStartInfo("/usr/bin/flatpak-spawn")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            ArgumentList = { "--host", "--watch-bus", "--directory=/", "--env=LC_ALL=C", "flatpak" }
        };
        foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        process.StandardInput.Close();
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(output, error);
            string message = (await error).Trim();
            if (process.ExitCode != 0) throw new IOException(message.Length > 0 ? message : MessageStrings.DownloadFailed);
            return await output;
        }
        finally
        {
            // Closing flatpak-spawn's connection also stops the host installer (--watch-bus).
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
}
