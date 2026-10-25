using System.IO.Compression;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Logging;
using Beutl.Services;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.ViewModels.Dialogs;

public class UpdateDialogViewModel
{
    private readonly CancellationTokenSource _cts = new();
    private readonly ILogger _logger = Log.CreateLogger<UpdateDialogViewModel>();
    private readonly FlatpakUpdater? _flatpak;
    private readonly HttpClient? _httpClient;
    private Task? _startTask;
    private string? _downloadFile;
    private bool _isInstalling;
    private bool _installationStarted;

    public UpdateDialogViewModel(AppUpdateResponse update) : this(update, FlatpakUpdater.IsRunning)
    {
    }

    // Test seam: the app passes neither flatpak nor httpClient and uses the defaults.
    internal UpdateDialogViewModel(AppUpdateResponse update, bool isFlatpak,
        FlatpakUpdater? flatpak = null, HttpClient? httpClient = null)
    {
        Update = update;
        IsFlatpak = isFlatpak;
        _flatpak = flatpak;
        _httpClient = httpClient;
    }

    public AppUpdateResponse Update { get; set; }

    public ReactiveProperty<string> ProgressText { get; } = new();

    public ReactiveProperty<double> ProgressValue { get; } = new();

    public ReactiveProperty<double> ProgressMax { get; } = new();

    public ReactiveProperty<bool> IsIndeterminate { get; } = new();

    public ReactiveProperty<bool> IsPrimaryButtonEnabled { get; } = new();

    internal bool IsFlatpak { get; }
    // Test seam: tests await the started update.
    internal Task? UpdateTask => _startTask;

    public string PrimaryButtonText => IsFlatpak ? "" : Strings.Next;

    public ReactiveProperty<string> CloseButtonText { get; } = new(Strings.Cancel);

    public async Task HandlePrimaryButtonClick()
    {
        if (IsFlatpak || _isInstalling || _installationStarted || _cts.IsCancellationRequested)
            return;

        _isInstalling = true;
        IsPrimaryButtonEnabled.Value = false;
        try
        {
            var metadata = await BeutlApiApplication.LoadMetadata();
            if (metadata == null)
            {
                ProgressText.Value = MessageStrings.FailedToLoadMetadata;
                return;
            }

            if (OperatingSystem.IsMacOS())
            {
                await InstallOnOSX(metadata);
            }
            else if (OperatingSystem.IsLinux())
            {
                await InstallOnLinux(metadata);
            }
            else if (OperatingSystem.IsWindows())
            {
                await InstallOnWindows(metadata);
            }
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            ProgressText.Value = MessageStrings.Canceled;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to handle primary button click");
            NotificationService.ShowError(Strings.Error, e.Message);
        }
        finally
        {
            _isInstalling = false;
            IsPrimaryButtonEnabled.Value = !_installationStarted && !_cts.IsCancellationRequested;
        }
    }

    private async Task<bool> InstallAndExitCoreAsync(ProcessStartInfo startInfo)
    {
        _cts.Token.ThrowIfCancellationRequested();
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime lifetime
            || lifetime.MainWindow?.DataContext is not MainViewModel main)
            throw new InvalidOperationException(MessageStrings.OperationFailed);

        if (!await main.TryDisposeForUpdateAsync(() =>
            {
                _cts.Token.ThrowIfCancellationRequested();
                using Process process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException(MessageStrings.OperationFailed);
                return true;
            }))
            return false;

        try
        {
            await main.WaitForDisposalAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cleanup failed after the update handoff was accepted.");
        }

        lifetime.TryShutdown();
        return true;
    }

    private async Task LaunchInstallerAsync(ProcessStartInfo startInfo)
    {
        _installationStarted = await InstallAndExitCoreAsync(startInfo);
        if (!_installationStarted)
            ProgressText.Value = MessageStrings.OperationFailed;
    }

    private async Task InstallOnWindows(AssetMetadataJson metadata)
    {
        if (metadata.Type == "installer")
        {
            if (_downloadFile == null)
            {
                ProgressText.Value = MessageStrings.DownloadFailed;
                return;
            }

            var psi = new ProcessStartInfo(_downloadFile) { UseShellExecute = true, Verb = "open" };
            await LaunchInstallerAsync(psi);
        }
    }

    private async Task InstallOnLinux(AssetMetadataJson metadata)
    {
        string command;
        if (metadata.Type == "debian")
        {
            if (_downloadFile == null)
            {
                ProgressText.Value = MessageStrings.DownloadFailed;
                return;
            }

            command = $"sudo apt update && sudo apt install \"{_downloadFile}\"";
        }
        else if (metadata.Type == "zip")
        {
            if (await WriteUpdateScriptAsync("update.sh", "Beutl.Resources.linux-update.sh") is not { } scriptPath)
            {
                return;
            }

            var directory = ZipStagingDirectory;
            var target = AppContext.BaseDirectory;

            command = $"chmod +x \"{scriptPath}\" && \"{scriptPath}\" \"{directory}\" \"{target}\" Beutl \"{Path.Combine(AppContext.BaseDirectory, "Beutl")}\"";
        }
        else
        {
            return;
        }

        if (TerminalLauncher.CreateStartInfo(["bash", "-c", command]) is not { } psi)
        {
            // Beutl keeps running, and the user can finish the update by running the command themselves.
            _logger.LogWarning("No terminal emulator was found for the update command: {Command}", command);
            ProgressText.Value = string.Format(MessageStrings.TerminalNotFound, command);
            return;
        }

        await LaunchInstallerAsync(psi);
    }

    private async Task InstallOnOSX(AssetMetadataJson metadata)
    {
        // Only the signed app updates in place; the unsigned macOS zips are no longer published.
        if (metadata.Type != "app") return;

        if (await WriteUpdateScriptAsync("update.sh", "Beutl.Resources.osx-update.sh") is not { } scriptPath)
        {
            return;
        }

        // The update unpacks to Beutl.app in the staging folder and replaces the bundle the application runs from.
        var directory = Path.Combine(UpdateStagingRoot, "Beutl.app");
        var target = Path.GetFullPath("../../", AppContext.BaseDirectory);

        var psi = new ProcessStartInfo("bash")
        {
            UseShellExecute = true,
            Verb = "open",
            ArgumentList =
            {
                scriptPath,
                directory,
                target,
                "Beutl",
                Path.Combine(AppContext.BaseDirectory, "Beutl")
            }
        };
        await LaunchInstallerAsync(psi);
    }

    public void Start()
    {
        _startTask ??= Task.Run(async () =>
        {
            _logger.LogInformation("Starting update process");
            FlatpakUpdater? flatpak = null;
            try
            {
                if (IsFlatpak) flatpak = _flatpak ?? FlatpakUpdater.Create();
                _downloadFile = await DownloadFile(flatpak?.DownloadPath, _httpClient);
                if (_downloadFile == null) return;
                if (flatpak != null)
                {
                    ProgressText.Value = ExtensionsStrings.Installing;
                    IsIndeterminate.Value = true;
                    await flatpak.InstallAsync(_cts.Token);
                    ProgressText.Value = MessageStrings.FlatpakUpdateCompleted;
                    return;
                }
            }
            catch (OperationCanceledException) { ProgressText.Value = MessageStrings.Canceled; return; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to install update");
                ProgressText.Value = ex.Message;
                ProgressValue.Value = 0;
                return;
            }
            finally
            {
                if (IsFlatpak)
                {
                    IsIndeterminate.Value = false;
                    CloseButtonText.Value = Strings.Close;
                    try { if (flatpak != null) File.Delete(flatpak.DownloadPath); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    { _logger.LogWarning(ex, "Failed to remove update download"); }
                }
            }

            var metadata = await BeutlApiApplication.LoadMetadata();
            if (metadata == null)
            {
                ProgressText.Value = MessageStrings.FailedToLoadMetadata;
                return;
            }

            // Only the Linux zip and the macOS app install from an unpacked update. Windows and macOS zips
            // are no longer published and have no update path.
            if ((metadata.Type == "zip" && OperatingSystem.IsLinux()) || metadata.Type == "app")
            {
                if (!await StageExtractedUpdateAsync(metadata, _downloadFile)) return;
            }

            if (metadata.Type is "installer" or "debian")
            {
                ProgressText.Value = MessageStrings.StartInstaller;
                IsPrimaryButtonEnabled.Value = true;
            }
        });
    }

    // Where an update is unpacked before the update script copies it over the application.
    private static string UpdateStagingRoot
        => Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "tmp", "update");

    // Downloads go to their own folder, so a server-chosen name cannot overwrite the update
    // scripts in tmp, which this application later executes.
    private static string DownloadDirectory
        => Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "tmp", "download");

    // A zip update unpacks into a folder named like the one the application runs from.
    private static string ZipStagingDirectory
        => GetZipStagingDirectory(AppContext.BaseDirectory, UpdateStagingRoot);

    // The update script copies the update over the application, so it stays in the home directory.
    internal static string GetZipStagingDirectory(string appDirectory, string stagingRoot)
        => Path.Combine(stagingRoot, new DirectoryInfo(appDirectory).Name);

    // Unpacks a zip or app update into an empty staging folder and offers the restart.
    private async Task<bool> StageExtractedUpdateAsync(AssetMetadataJson metadata, string file)
    {
        var destination = metadata.Type == "zip" ? ZipStagingDirectory : UpdateStagingRoot;

        try
        {
            PrepareStagingDirectory(destination);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Failed to prepare the update folder {Destination}", destination);
            ProgressText.Value = ex.Message;
            return false;
        }

        var result = await ExtractIfNeeded(metadata, file, destination);
        if (!result) return false;

        ProgressText.Value = MessageStrings.ApplicationRestartRequired;
        IsPrimaryButtonEnabled.Value = true;
        return true;
    }

    // Creates an empty staging folder in the home directory's tmp folder, which belongs to Beutl.
    internal static void PrepareStagingDirectory(string destination)
    {
        if (Directory.Exists(destination))
        {
            Directory.Delete(destination, true);
        }

        Directory.CreateDirectory(destination);
    }

    // Writes the update script resource to the home directory's tmp folder. A script that
    // cannot be loaded is reported and gives no path.
    private async Task<string?> WriteUpdateScriptAsync(string fileName, string resourceName)
    {
        string scriptPath = Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "tmp", fileName);
        await using (var fs = File.Create(scriptPath))
        {
            if (!await LoadScript(resourceName, fs))
            {
                ProgressText.Value = MessageStrings.FailedToLoadScript;
                return null;
            }
        }

        return scriptPath;
    }

    // The file the update downloads to: the given path, or a sanitized name the server or the
    // URL gives it in a folder of its own, which is created when missing.
    private string ResolveDownloadTarget(HttpResponseMessage response, string? destinationPath)
    {
        var directory = destinationPath != null
            ? Path.GetDirectoryName(Path.GetFullPath(destinationPath))!
            : DownloadDirectory;
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (destinationPath != null)
            return Path.GetFullPath(destinationPath);

        string name = NormalizeFileName(response.Content.Headers.ContentDisposition?.FileName)
            ?? NormalizeFileName(Uri.TryCreate(Update.DownloadUrl, UriKind.Absolute, out Uri? uri)
                ? Uri.UnescapeDataString(uri.AbsolutePath)
                : null)
            ?? "update";

        string file = Path.GetFullPath(Path.Combine(directory, name));
        if (!FilePathComparison.IsSameOrDescendant(directory, file))
            throw new InvalidDataException(MessageStrings.DownloadFailed);

        _logger.LogInformation("Resolved download file name: {FileName}", name);
        return file;
    }

    // Keep a single path component made of characters that are safe both as a file name on
    // every platform and inside the Linux update command, which runs through a shell.
    internal static string? NormalizeFileName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string name = value.Trim().Trim('"').Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        name = string.Concat(name.Select(c =>
                char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_'))
            .Trim(' ', '.');
        return name.Length == 0 ? null : name;
    }

    // The digest is hex, optionally written the way GitHub release assets spell it.
    private static string? NormalizeSha256(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string hash = value.Trim();
        if (hash.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            hash = hash["sha256:".Length..];
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit))
            throw new InvalidDataException(MessageStrings.DownloadFailed);

        return hash;
    }

    // Test seam: only tests pass maximumBytes.
    internal async Task<string?> DownloadFile(string? destinationPath = null, HttpClient? httpClient = null, long? maximumBytes = null)
    {
        string? temporaryPath = null;
        try
        {
            ProgressValue.Value = 0;
            ProgressMax.Value = 1;
            IsIndeterminate.Value = false;
            ProgressText.Value = MessageStrings.Downloading;
            var ct = _cts.Token;
            if (!Uri.TryCreate(Update.DownloadUrl, UriKind.Absolute, out Uri? uri)
                || uri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidDataException(MessageStrings.DownloadFailed);

            _logger.LogInformation("Downloading update from {DownloadUrl}", Update.DownloadUrl);
            using var client = httpClient ?? new HttpClient();
            using var response =
                await client.GetAsync(Update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            // A redirect may leave HTTPS, so check where the response actually came from.
            if (response.RequestMessage?.RequestUri is { } finalUri
                && !string.Equals(finalUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(MessageStrings.DownloadFailed);

            long? contentLength = response.Content.Headers.ContentLength;
            long limit = maximumBytes ?? (IsFlatpak ? FlatpakUpdater.MaximumBundleBytes : long.MaxValue);
            if (contentLength > limit) throw new InvalidDataException(MessageStrings.DownloadFailed);
            var file = ResolveDownloadTarget(response, destinationPath);

            string? expectedHash = NormalizeSha256(Update.Sha256);
            if (expectedHash == null)
            {
                // Older responses have no digest; verify every download that carries one.
                _logger.LogWarning(
                    "The update response has no SHA-256 hash; the downloaded file cannot be verified.");
            }

            using IncrementalHash? hash = expectedHash != null
                ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
                : null;

            // Download beside the target and publish only a complete, verified file.
            const int bufferSize = 81920;
            temporaryPath = Path.Combine(Path.GetDirectoryName(file)!, $".{Guid.NewGuid():N}.part");
            await using (var destination = new FileStream(
                             temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             bufferSize, FileOptions.Asynchronous))
            {
                await using Stream download = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

                IsIndeterminate.Value = !contentLength.HasValue;
                byte[] buffer = new byte[bufferSize];
                long totalBytesRead = 0;
                int bytesRead;
                while ((bytesRead = await download.ReadAsync(buffer, ct).ConfigureAwait(false)) != 0)
                {
                    totalBytesRead += bytesRead;
                    if (totalBytesRead > limit || (contentLength.HasValue && totalBytesRead > contentLength.Value))
                        throw new InvalidDataException(MessageStrings.DownloadFailed);
                    hash?.AppendData(buffer, 0, bytesRead);
                    await destination.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);
                    if (contentLength > 0) ProgressValue.Value = totalBytesRead / (double)contentLength.Value;
                }

                if (totalBytesRead == 0 || (contentLength.HasValue && totalBytesRead != contentLength.Value))
                    throw new InvalidDataException(MessageStrings.DownloadFailed);
            }

            if (hash != null)
            {
                string actualHash = Convert.ToHexString(hash.GetHashAndReset());
                if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogError(
                        "The update file does not match its advertised SHA-256. Expected {Expected}, got {Actual}.",
                        expectedHash, actualHash);
                    throw new InvalidDataException(MessageStrings.DownloadFailed);
                }
            }

            File.Move(temporaryPath, file, overwrite: true);
            temporaryPath = null;

            ProgressText.Value = MessageStrings.DownloadComplete;
            ProgressValue.Value = 1;
            IsIndeterminate.Value = false;
            _logger.LogInformation("Downloaded update to {FilePath}", file);

            return file;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Download canceled");
            ProgressText.Value = MessageStrings.Canceled;
            return null;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to download update");
            ProgressText.Value = e.Message;
            return null;
        }
        finally
        {
            if (temporaryPath != null)
            {
                try { File.Delete(temporaryPath); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { _logger.LogWarning(ex, "Failed to remove the incomplete update download"); }
            }
        }
    }

    private async Task<bool> ExtractIfNeeded(AssetMetadataJson metadata, string file, string destination)
    {
        var ct = _cts.Token;
        bool extracted = false;
        _logger.LogInformation("Extracting update to {Destination}", destination);
        try
        {
            if (metadata.Type is "zip")
            {
                // Not back on the caller's context: like the extractor's own awaits, the rest of
                // this method runs wherever the extraction finished.
                await ExtractZipAsync(metadata, file, destination, ct).ConfigureAwait(false);
            }
            else if (metadata.Type is "app")
            {
                await ExtractAppBundleAsync(metadata, file, destination, ct);
            }

            File.Delete(file);
            _logger.LogInformation("Extraction complete");
            ProgressText.Value = MessageStrings.ExtractionComplete;
            ProgressValue.Value = ProgressMax.Value;
            IsIndeterminate.Value = false;
            extracted = true;
            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Extraction canceled");
            ProgressText.Value = MessageStrings.Canceled;
            return false;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to extract update");
            ProgressText.Value = e.Message;
            return false;
        }
        finally
        {
            if (!extracted)
            {
                try
                {
                    if (Directory.Exists(destination))
                        Directory.Delete(destination, recursive: true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to remove incomplete update files from {Destination}.", destination);
                }
            }
        }
    }

    private async Task ExtractZipAsync(
        AssetMetadataJson metadata,
        string file,
        string destination,
        CancellationToken ct)
    {
        using var source = ZipFile.Open(file, ZipArchiveMode.Read);
        string extractionRoot = Path.GetFullPath(destination);
        if (!Path.EndsInDirectorySeparator(extractionRoot))
            extractionRoot += Path.DirectorySeparatorChar;

        ProgressMax.Value = source.Entries.Count;
        ProgressText.Value = MessageStrings.Extracting;
        foreach (var entry in source.Entries)
        {
            if (entry.Length != 0)
            {
                string dst = Path.GetFullPath(Path.Combine(extractionRoot, entry.FullName));
                if (!dst.StartsWith(extractionRoot, StringComparison.Ordinal))
                {
                    _logger.LogError("Entry is outside of the target directory: {Entry}", entry.FullName);
                    throw new InvalidOperationException("Entry is outside of the target directory.");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                // The framework extractor restores Unix permissions from the ZIP, including
                // the executable bits required by the FFmpeg worker and other helper apps.
                await entry.ExtractToFileAsync(dst, overwrite: true, ct).ConfigureAwait(false);
            }

            ProgressValue.Value++;
        }

        ValidateApplicationPayload(metadata, destination);
    }

    private async Task ExtractAppBundleAsync(
        AssetMetadataJson metadata,
        string file,
        string destination,
        CancellationToken ct)
    {
        IsIndeterminate.Value = true;
        var psi = new ProcessStartInfo("/usr/bin/ditto")
        {
            ArgumentList =
            {
                "-xk",
                file,
                destination
            }
        };
        using var process = Process.Start(psi);
        if (process == null)
        {
            _logger.LogError("Failed to start ditto");
            throw new InvalidOperationException("Failed to start ditto");
        }

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // Join the extractor before removing its partial output.
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }

        string contents = Path.Combine(destination, "Beutl.app", "Contents");
        string executable = Path.Combine(contents, "MacOS", "Beutl");
        if (process.ExitCode != 0
            || !File.Exists(Path.Combine(contents, "Info.plist"))
            || !File.Exists(executable)
            || new FileInfo(executable).Length == 0)
        {
            _logger.LogError("The update bundle was not completely extracted. Exit code: {ExitCode}",
                process.ExitCode);
            throw new InvalidDataException(MessageStrings.OperationFailed);
        }

        ValidateApplicationPayload(metadata, Path.Combine(contents, "MacOS"));
    }

    private void ValidateApplicationPayload(AssetMetadataJson metadata, string directory)
    {
        // Windows updates through its installer, so only Linux zips and the macOS app get here.
        string executable = metadata.OS switch
        {
            "linux" or "osx" => "Beutl",
            _ => throw new InvalidDataException(MessageStrings.OperationFailed),
        };
        List<string> requiredFiles = [executable, "Beutl.dll", "Beutl.deps.json", "Beutl.runtimeconfig.json"];
        if (string.Equals(metadata.Standalone, "true", StringComparison.OrdinalIgnoreCase))
        {
            requiredFiles.Add("System.Private.CoreLib.dll");
            foreach (string library in new[] { "coreclr", "hostfxr", "hostpolicy" })
            {
                requiredFiles.Add(metadata.OS switch
                {
                    "linux" => "lib" + library + ".so",
                    _ => "lib" + library + ".dylib",
                });
            }
        }

        // A valid ZIP is not necessarily an application. Reject incomplete payloads before
        // enabling installation: the updater removes its backup after a successful copy.
        foreach (string required in requiredFiles)
        {
            string path = Path.Combine(directory, required);
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
            {
                _logger.LogError("The update is missing a required nonempty file: {File}", required);
                throw new InvalidDataException(MessageStrings.OperationFailed);
            }
        }
    }

    public void Cancel()
    {
        if (_cts.IsCancellationRequested) return;
        _logger.LogInformation("Canceling update process");
        _cts.Cancel();
    }

    private async Task<bool> LoadScript(string name, Stream stream)
    {
        _logger.LogInformation("Loading script {Name}", name);
        var source = typeof(UpdateDialogViewModel).Assembly.GetManifestResourceStream(name);
        if (source == null)
        {
            _logger.LogError("Failed to load script {Name}", name);
            return false;
        }

        using var reader = new StreamReader(source);
        await using var writer = new StreamWriter(stream);

        var renderer = new SimpleTemplateRenderer(
            await reader.ReadToEndAsync(), [typeof(Strings), typeof(MessageStrings)]);
        var script = renderer.Render();
        await writer.WriteAsync(script);
        return true;
    }
}
