using System.Text;
using Beutl.Api.Clients;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

internal readonly record struct FlatpakUpdateProgress(string Message, double? Fraction = null);

internal sealed class FlatpakUpdateService(
    HttpClient client,
    Func<ProcessStartInfo, CancellationToken, Task<string>> runCommand,
    Func<string, string?>? readBundleRef = null,
    long maximumDownloadBytes = FlatpakUpdateService.MaxDownloadBytes)
{
    internal const long MaxDownloadBytes = 1024L * 1024 * 1024;
    private const string AppId = "net.beditor.Beutl";
    private static readonly SemaphoreSlim s_updateGate = new(1, 1);
    private readonly Func<string, string?> _readBundleRef = readBundleRef ?? FlatpakBundleMetadata.ReadRef;

    internal static bool IsRunningInFlatpak => File.Exists("/.flatpak-info")
        || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FLATPAK_ID"));

    internal static async Task InstallAsync(AppUpdateResponse update,
        IProgress<FlatpakUpdateProgress> progress, CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        var service = new FlatpakUpdateService(client, RunCommandAsync);
        string info = await File.ReadAllTextAsync("/.flatpak-info", cancellationToken);
        await service.InstallAsync(update, info, progress, cancellationToken);
    }

    internal async Task InstallAsync(AppUpdateResponse update, string flatpakInfo,
        IProgress<FlatpakUpdateProgress> progress, CancellationToken cancellationToken)
    {
        if (!await s_updateGate.WaitAsync(0, cancellationToken))
            throw new InvalidOperationException(MessageStrings.FlatpakUpdateInProgress);

        string? directory = null;
        try
        {
            progress.Report(new(MessageStrings.FlatpakPreparingUpdate));
            if (!Uri.TryCreate(update.DownloadUrl, UriKind.Absolute, out var downloadUri)
                || downloadUri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException(MessageStrings.FlatpakUpdateUnavailable);

            var info = ParseInfo(flatpakInfo);
            string installation = await FindInstallationAsync(info, cancellationToken);

            // /tmp is private to the sandbox. The instance's persistent cache has the
            // same absolute path on the host, including when XDG_CACHE_HOME is remapped.
            directory = Path.Combine(info.InstancePath, "cache", "beutl-updates", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string bundle = Path.Combine(directory, "update.flatpak");
            await DownloadAsync(downloadUri, bundle, progress, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            string? bundleRef = await Task.Run(() => _readBundleRef(bundle), cancellationToken);
            if (bundleRef != "app/" + info.Ref)
                throw new InvalidDataException(MessageStrings.FlatpakInvalidBundle);
            cancellationToken.ThrowIfCancellationRequested();

            progress.Report(new(ExtensionsStrings.Installing));
            await runCommand(CreateStartInfo("install", installation, "--bundle", "--or-update",
                "--noninteractive", "--assumeyes", bundle), cancellationToken);
            // Flatpak atomically deploys the new version. The running sandbox keeps
            // its old deployment until the user closes it through the normal save flow.
        }
        finally
        {
            if (directory != null)
            {
                try
                {
                    Directory.Delete(directory, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Log.CreateLogger<FlatpakUpdateService>().LogWarning(ex, "Failed to clean up Flatpak update download");
                }
            }

            s_updateGate.Release();
        }
    }

    private async Task<string> FindInstallationAsync(InstallationInfo info, CancellationToken cancellationToken)
    {
        string installations = await runCommand(CreateStartInfo("list", "--app", "--columns=ref,installation"), cancellationToken);
        foreach (string line in installations.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = line.Split('\t', StringSplitOptions.TrimEntries);
            if (fields.Length != 2 || fields[0] != info.Ref) continue;

            string option = fields[1] switch
            {
                "user" => "--user",
                "system" => "--system",
                var name when name.StartsWith("system (", StringComparison.Ordinal) && name.EndsWith(')')
                    => "--installation=" + name[8..^1],
                _ => throw new InvalidOperationException(MessageStrings.FlatpakUpdateUnavailable)
            };
            string location = (await runCommand(CreateStartInfo("info", option, "--show-location", info.Ref), cancellationToken)).Trim();
            // Compare the branch directory, not the commit: another updater may have
            // deployed a new commit since this sandbox started. Never pick another copy
            // just because the same app is also installed there.
            if (Path.GetDirectoryName(location) == Path.GetDirectoryName(Path.GetDirectoryName(info.AppPath)))
                return option;
        }

        throw new InvalidOperationException(MessageStrings.FlatpakUpdateUnavailable);
    }

    private async Task DownloadAsync(Uri downloadUri, string path,
        IProgress<FlatpakUpdateProgress> progress, CancellationToken cancellationToken)
    {
        progress.Report(new(MessageStrings.Downloading));
        using var response = await client.GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        long? contentLength = response.Content.Headers.ContentLength;
        if (contentLength > maximumDownloadBytes)
            throw new InvalidDataException(MessageStrings.FlatpakUpdateTooLarge);
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = File.Create(path);
        byte[] buffer = new byte[81920];
        long total = 0;
        int count;
        while ((count = await source.ReadAsync(buffer, cancellationToken)) != 0)
        {
            total += count;
            if (total > maximumDownloadBytes)
                throw new InvalidDataException(MessageStrings.FlatpakUpdateTooLarge);
            if (contentLength.HasValue && total > contentLength.Value)
                throw new InvalidDataException(MessageStrings.DownloadFailed);
            await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            progress.Report(new(MessageStrings.Downloading,
                contentLength > 0 ? (double)total / contentLength.Value : null));
        }

        if (total == 0 || (contentLength.HasValue && total != contentLength.Value))
            throw new InvalidDataException(MessageStrings.DownloadFailed);
        progress.Report(new(MessageStrings.Downloading, 1));
    }

    internal static InstallationInfo ParseInfo(string text)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        string section = "";
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith('#') || line.Length == 0) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1];
                continue;
            }

            int equals = line.IndexOf('=');
            if (equals > 0)
            {
                string key = section + "/" + line[..equals].Trim();
                if (key is "Application/name" or "Instance/arch" or "Instance/branch"
                    or "Instance/instance-path" or "Instance/app-path" or "Instance/original-app-path")
                    values[key] = Unescape(line[(equals + 1)..]);
            }
        }

        string? Get(string key) => values.GetValueOrDefault(key);
        // Flatpak writes the running architecture and branch under [Instance],
        // not [Application]: flatpak-run.c, flatpak_run_add_app_info_args().
        // The release workflow currently builds this one architecture and branch.
        // Refuse other channels rather than installing a second, unrelated copy.
        if (Get("Application/name") != AppId || Get("Instance/arch") != "x86_64" || Get("Instance/branch") != "master"
            || Get("Instance/instance-path") is not { } instancePath || !Path.IsPathFullyQualified(instancePath)
            || (Get("Instance/original-app-path") ?? Get("Instance/app-path")) is not { } appPath || !Path.IsPathFullyQualified(appPath)
            || !appPath.EndsWith("/files", StringComparison.Ordinal))
            throw new InvalidOperationException(MessageStrings.FlatpakUpdateUnavailable);

        return new InstallationInfo($"{AppId}/x86_64/master", instancePath, appPath);
    }

    private static string Unescape(string value)
    {
        var result = new StringBuilder();
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                result.Append(value[++i] switch
                {
                    's' => ' ',
                    'n' => '\n',
                    't' => '\t',
                    'r' => '\r',
                    '\\' => '\\',
                    _ => throw new InvalidDataException(MessageStrings.FlatpakUpdateUnavailable)
                });
            }
            else result.Append(value[i]);
        }
        return result.ToString();
    }

    internal static ProcessStartInfo CreateStartInfo(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("/usr/bin/flatpak-spawn")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            ArgumentList = { "--host", "--watch-bus", "--directory=/", "--env=LC_ALL=C", "flatpak" }
        };
        foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);
        return startInfo;
    }

    internal static async Task<string> RunCommandAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var process = Process.Start(startInfo) ?? throw new IOException(MessageStrings.FlatpakUpdateUnavailable);
        process.StandardInput.Close();
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(output, error);
            if (process.ExitCode != 0)
                throw new InvalidOperationException((await error).Trim() is { Length: > 0 } message
                    ? message[..Math.Min(message.Length, 4096)] : MessageStrings.FlatpakUpdateFailed);
            return await output;
        }
        finally
        {
            // --watch-bus also terminates the host command when flatpak-spawn exits.
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

    internal sealed record InstallationInfo(string Ref, string InstancePath, string AppPath);
}
