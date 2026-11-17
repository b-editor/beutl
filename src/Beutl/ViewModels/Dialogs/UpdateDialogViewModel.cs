using System.IO.Compression;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Services;
using Beutl.Configuration;
using Beutl.Logging;
using Beutl.Services;
using Beutl.Utilities;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using Reactive.Bindings;

namespace Beutl.ViewModels.Dialogs;

public enum AppUpdatePhase
{
    Preparing,
    Ready,
    Completed,
    Failed,
    Canceled,
    Installing,
}

public class UpdateDialogViewModel
{
    private static readonly object s_claimGate = new();
    private static FileStream? s_updateFolderClaim;
    private readonly UpdateConfig _configuration;
    private readonly CancellationTokenSource _cts = new();
    private readonly ILogger _logger = Log.CreateLogger<UpdateDialogViewModel>();
    private readonly FlatpakUpdater? _flatpak;
    private readonly TaskCompletionSource? _flatpakInstallRequested;
    private readonly HttpClient? _httpClient;
    private readonly Func<Task<AssetMetadataJson?>> _loadMetadata;
    private readonly Func<bool> _claimUpdateFolders;
    private readonly Func<string, CancellationToken, Task<int>>? _prepareExtensions;
    private readonly TimeProvider _timeProvider;
    private readonly ReactivePropertySlim<int> _preparedExtensionCount = new();
    private Task? _startTask;
    private string? _downloadFile;
    private bool _isInstalling;
    private bool _installationStarted;
    private bool _installOnQuit;

    public UpdateDialogViewModel(AppUpdateResponse update, UpdateConfig configuration) : this(update, configuration, FlatpakUpdater.IsRunning)
    {
        if (MainViewModel.TryGetCurrent(out var main))
        {
            _prepareExtensions = async (version, token) =>
            {
                if (!NuGetVersion.TryParse(version, out var host)) return 0;
                var manager = main._beutlClients.GetResource<PackageManager>();
                var result = await manager.StageExtensionUpdatesAsync(host, appUpdate: true, token);
                return result.Staged;
            };
        }
    }

    // Test seam: the app passes neither flatpak nor httpClient and uses the defaults.
    internal UpdateDialogViewModel(AppUpdateResponse update, UpdateConfig configuration, bool isFlatpak,
        FlatpakUpdater? flatpak = null, HttpClient? httpClient = null,
        Func<Task<AssetMetadataJson?>>? loadMetadata = null, Func<bool>? claimUpdateFolders = null,
        Func<string, CancellationToken, Task<int>>? prepareExtensions = null, TimeProvider? timeProvider = null)
    {
        _configuration = configuration;
        Update = update;
        _flatpakInstallRequested = isFlatpak ? new(TaskCreationOptions.RunContinuationsAsynchronously) : null;
        _flatpak = flatpak;
        _httpClient = httpClient;
        _loadMetadata = loadMetadata ?? BeutlApiApplication.LoadMetadata;
        _claimUpdateFolders = claimUpdateFolders ?? ClaimUpdateFolders;
        _prepareExtensions = prepareExtensions;
        _timeProvider = timeProvider ?? TimeProvider.System;
        ExtensionUpdateSummary = _preparedExtensionCount.CombineLatest(
            _configuration.ExtensionUpdates.GetObservable(), (_, _) => IncludedExtensions);
        PrimaryButtonText = new(isFlatpak
            ? ExtensionsStrings.Install
            : OperatingSystem.IsWindows()
                ? MessageStrings.UpdateAction_StartInstaller
                : MessageStrings.UpdateAction_RestartAndUpdate);
    }

    public AppUpdateResponse Update { get; set; }

    public ReactiveProperty<string> ProgressText { get; } = new();

    public IObservable<string?> ExtensionUpdateSummary { get; }

    internal string? IncludedExtensions =>
        _preparedExtensionCount.Value > 0 && _configuration.ShouldUpdateExtensions(true)
            ? string.Format(MessageStrings.ExtensionUpdatesIncluded, _preparedExtensionCount.Value)
            : null;

    public ReactiveProperty<double> ProgressValue { get; } = new();

    public ReactiveProperty<double> ProgressMax { get; } = new();

    public ReactiveProperty<bool> IsIndeterminate { get; } = new();

    public ReactiveProperty<bool> IsPrimaryButtonEnabled { get; } = new();

    internal bool IsFlatpak => _flatpakInstallRequested != null;
    // Test seam: tests await the started update.
    internal Task? UpdateTask => _startTask;

    // Where the update can be installed by hand when installing it from here keeps failing.
    public Uri? ReleaseUri => Uri.TryCreate(Update.Url, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps
        ? uri
        : null;

    public string VersionText => $"Beutl {BeutlApplication.Version} → {Update.LatestVersion}";

    public ReactiveProperty<string> PrimaryButtonText { get; }

    public ReactiveProperty<string> CloseButtonText { get; } = new(Strings.Cancel);

    public ReactiveProperty<string> SecondaryButtonText { get; } = new(MessageStrings.UpdateAction_RunInBackground);

    public ReactiveProperty<bool> IsSecondaryButtonEnabled { get; } = new(true);

    public ReactivePropertySlim<AppUpdatePhase> Phase { get; } = new(AppUpdatePhase.Preparing);

    // From 0 to 1 while a download of known size runs, and null otherwise.
    public ReactivePropertySlim<double?> DownloadProgress { get; } = new();

    // Canceled when the update is abandoned, so whatever still offers it can go away.
    internal CancellationToken CancellationToken => _cts.Token;

    // Set when the download or the preparation failed, so the user can try again without restarting Beutl.
    public ReactiveProperty<bool> IsRetryVisible { get; } = new();

    public async Task HandlePrimaryButtonClick()
    {
        if (_isInstalling || _installationStarted || _cts.IsCancellationRequested)
            return;

        if (_flatpakInstallRequested != null)
        {
            if (Phase.Value == AppUpdatePhase.Ready)
                _flatpakInstallRequested.TrySetResult();
            return;
        }

        _isInstalling = true;
        IsPrimaryButtonEnabled.Value = false;
        try
        {
            if (await CreateInstallerLaunchAsync(relaunch: true) is { } startInstaller)
            {
                await LaunchInstallerAsync(startInstaller);
            }
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            ProgressText.Value = MessageStrings.Canceled;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to handle primary button click");
            ProgressText.Value = DescribeFailure(MessageStrings.OperationFailed, e);
        }
        finally
        {
            _isInstalling = false;
            IsPrimaryButtonEnabled.Value = !_installationStarted && !_cts.IsCancellationRequested;
        }
    }

    private async Task<bool> InstallAndExitCoreAsync(Action startInstaller)
    {
        _cts.Token.ThrowIfCancellationRequested();
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime lifetime
            || lifetime.MainWindow?.DataContext is not MainViewModel main)
            throw new InvalidOperationException(MessageStrings.OperationFailed);

        if (!await main.TryDisposeForUpdateAsync(() =>
            {
                _cts.Token.ThrowIfCancellationRequested();
                startInstaller();
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

    private async Task LaunchInstallerAsync(Action startInstaller)
    {
        _installationStarted = await InstallAndExitCoreAsync(startInstaller);
        if (!_installationStarted)
            ProgressText.Value = MessageStrings.OperationFailed;
    }

    // Makes the prepared update install when Beutl quits. The application is left closed afterwards,
    // since the user was leaving anyway. False when that is impossible; ProgressText says why.
    public async Task<bool> ScheduleInstallOnQuitAsync()
    {
        if (IsFlatpak || Phase.Value != AppUpdatePhase.Ready
            || _isInstalling || _installationStarted || _installOnQuit || _cts.IsCancellationRequested)
            return false;

        // Both ways of installing write the same update script, so they must not overlap.
        _isInstalling = true;
        try
        {
            if (await CreateInstallerLaunchAsync(relaunch: false) is not { } startInstaller)
                return false;

            // The update can be canceled while the script is written, and Cancel had nothing to clear yet.
            if (_cts.IsCancellationRequested)
                return false;

            if (!MainViewModel.TryGetCurrent(out MainViewModel? main))
            {
                ProgressText.Value = MessageStrings.OperationFailed;
                return false;
            }

            main.SetUpdateOnQuit(startInstaller);
            _installOnQuit = true;
            ProgressText.Value = MessageStrings.UpdateWillInstallOnQuit;
            IsSecondaryButtonEnabled.Value = false;
            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to schedule the update for when Beutl quits");
            ProgressText.Value = DescribeFailure(MessageStrings.OperationFailed, e);
            return false;
        }
        finally
        {
            _isInstalling = false;
        }
    }

    // How to start the installer for this platform, or null with the reason in ProgressText.
    private async Task<Action?> CreateInstallerLaunchAsync(bool relaunch)
    {
        if (await _loadMetadata() is not { } metadata)
        {
            ProgressText.Value = MessageStrings.FailedToLoadMetadata;
            return null;
        }

        if (OperatingSystem.IsLinux()) return await CreateLinuxInstallerLaunchAsync(metadata, relaunch);
        ProcessStartInfo? startInfo = OperatingSystem.IsMacOS() ? await CreateMacInstallerStartInfoAsync(metadata, relaunch)
            : OperatingSystem.IsWindows() ? CreateWindowsInstallerStartInfo(metadata, relaunch) : null;
        return startInfo == null ? null : () => StartInstaller(startInfo);
    }

    private static void StartInstaller(ProcessStartInfo startInfo)
    {
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(MessageStrings.OperationFailed);
    }

    private ProcessStartInfo? CreateWindowsInstallerStartInfo(AssetMetadataJson metadata, bool relaunch)
    {
        if (metadata.Type != "installer") return null;

        if (_downloadFile == null)
        {
            ProgressText.Value = MessageStrings.DownloadFailed;
            return null;
        }

        // beutl-setup.iss reads this switch to leave out its "Launch Beutl" option.
        return new ProcessStartInfo(_downloadFile)
        {
            UseShellExecute = true,
            Verb = "open",
            Arguments = relaunch ? "" : "/BeutlOnQuit=1",
        };
    }

    private async Task<Action?> CreateLinuxInstallerLaunchAsync(AssetMetadataJson metadata, bool relaunch)
    {
        if (metadata.Type == "debian")
        {
            if (_downloadFile is not { } package)
            {
                ProgressText.Value = MessageStrings.DownloadFailed;
                return null;
            }

            var debianTerminal = FindTerminal(DebianPackageInstaller.Command, DebianPackageInstaller.GetManualCommand(package));
            return debianTerminal == null ? null : () => DebianPackageInstaller.Start(package, debianTerminal, StartInstaller);
        }

        if (metadata.Type != "zip") return null;
        if (await WriteUpdateScriptAsync("update.sh", "Beutl.Resources.linux-update.sh") is not { } scriptPath)
        {
            return null;
        }

        var directory = ZipStagingDirectory;
        var target = AppContext.BaseDirectory;
        string executable = relaunch ? $" \"{Path.Combine(AppContext.BaseDirectory, "Beutl")}\"" : "";
        string command = $"chmod +x \"{scriptPath}\" && \"{scriptPath}\" \"{directory}\" \"{target}\" Beutl{executable}";
        var terminal = FindTerminal(["bash", "-c", command], command);
        return terminal == null ? null : () => StartInstaller(terminal);
    }

    private ProcessStartInfo? FindTerminal(IReadOnlyList<string> arguments, string manualCommand)
    {
        if (TerminalLauncher.CreateStartInfo(arguments) is { } terminal) return terminal;
        _logger.LogWarning("No terminal emulator was found for the update command: {Command}", manualCommand);
        ProgressText.Value = string.Format(MessageStrings.TerminalNotFound, manualCommand);
        return null;
    }

    private async Task<ProcessStartInfo?> CreateMacInstallerStartInfoAsync(AssetMetadataJson metadata, bool relaunch)
    {
        // Only the signed app updates in place; the unsigned macOS zips are no longer published.
        if (metadata.Type != "app") return null;

        if (await WriteUpdateScriptAsync("update.sh", "Beutl.Resources.osx-update.sh") is not { } scriptPath)
        {
            return null;
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
            }
        };
        if (relaunch) psi.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Beutl"));
        return psi;
    }

    public void Start()
    {
        _startTask ??= Task.Run(async () =>
        {
            _logger.LogInformation("Starting update process");
            AppUpdatePhase result;
            try
            {
                result = await PrepareUpdateAsync();
                if (result == AppUpdatePhase.Ready)
                    await PrepareExtensionUpdatesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to prepare the update");
                ProgressText.Value = DescribeFailure(MessageStrings.OperationFailed, ex);
                result = AppUpdatePhase.Failed;
            }

            if (_cts.IsCancellationRequested)
            {
                Phase.Value = AppUpdatePhase.Canceled;
                return;
            }

            switch (result)
            {
                case AppUpdatePhase.Failed:
                    IsRetryVisible.Value = true;
                    CloseButtonText.Value = Strings.Close;
                    IsSecondaryButtonEnabled.Value = false;
                    break;
                case AppUpdatePhase.Completed:
                    IsSecondaryButtonEnabled.Value = false;
                    break;
                case AppUpdatePhase.Ready:
                    SecondaryButtonText.Value = MessageStrings.UpdateAction_UpdateOnQuit;
                    IsPrimaryButtonEnabled.Value = true;
                    break;
            }

            Phase.Value = result;
        });
    }

    private async Task PrepareExtensionUpdatesAsync()
    {
        if (_prepareExtensions == null || !_configuration.ShouldUpdateExtensions(true)) return;
        string previous = ProgressText.Value;
        ProgressText.Value = MessageStrings.PreparingExtensionUpdates;
        IsIndeterminate.Value = true;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30), _timeProvider);
        using var preparation = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, deadline.Token);
        try
        {
            _preparedExtensionCount.Value = await PrepareAsync(preparation.Token).WaitAsync(preparation.Token);
        }
        catch (OperationCanceledException) when (preparation.IsCancellationRequested)
        {
            // The deadline cancels only the extensions. Canceling the app update still withdraws it.
        }
        finally
        {
            IsIndeterminate.Value = false;
            ProgressText.Value = _cts.IsCancellationRequested ? MessageStrings.Canceled : previous;
        }

        async Task<int> PrepareAsync(CancellationToken token)
        {
            try
            {
                return await _prepareExtensions(Update.LatestVersion, token);
            }
            catch (Exception ex)
            {
                // A source may finish unwinding after the app is ready to install.
                if (!token.IsCancellationRequested)
                    _logger.LogWarning(ex, "Could not prepare extension updates alongside Beutl.");
                return 0;
            }
        }
    }

    public void Retry()
    {
        if (!IsRetryVisible.Value || _cts.IsCancellationRequested) return;

        _logger.LogInformation("Retrying update process");
        IsRetryVisible.Value = false;
        CloseButtonText.Value = Strings.Cancel;
        IsSecondaryButtonEnabled.Value = true;
        Phase.Value = AppUpdatePhase.Preparing;
        _startTask = null;
        Start();
    }

    // Failed when the update could not be prepared; ProgressText says why.
    private async Task<AppUpdatePhase> PrepareUpdateAsync()
    {
        if (IsFlatpak) return await PrepareFlatpakUpdateAsync();

        // Read first, so an update that cannot be prepared here downloads nothing.
        AssetMetadataJson? metadata = await _loadMetadata();
        if (metadata == null)
        {
            ProgressText.Value = MessageStrings.FailedToLoadMetadata;
            return AppUpdatePhase.Failed;
        }

        // Unpacking empties folders that every running Beutl shares, where another Beutl may have its update ready.
        if (Unpacks(metadata) && !_claimUpdateFolders())
        {
            _logger.LogInformation("Another Beutl holds the update folders");
            ProgressText.Value = MessageStrings.UpdateFoldersInUse;
            return AppUpdatePhase.Failed;
        }

        UpdateDownloadCache? cache = UpdateDownloadCache.Open(DownloadDirectory);
        try
        {
            _downloadFile = await DownloadFile(httpClient: _httpClient, cache: cache);
            if (_downloadFile == null) return AppUpdatePhase.Failed;
            if (Unpacks(metadata))
            {
                return await StageExtractedUpdateAsync(metadata, _downloadFile)
                    ? AppUpdatePhase.Ready
                    : AppUpdatePhase.Failed;
            }

            if (metadata.Type is "installer" or "debian")
            {
                // Keep this file available through install-on-quit. Canceling or exiting releases the folder.
                _cts.Token.Register(cache.Dispose);
                cache = null;
                ProgressText.Value = MessageStrings.StartInstaller;
                PrimaryButtonText.Value = MessageStrings.UpdateAction_StartInstaller;
                return AppUpdatePhase.Ready;
            }

            ProgressText.Value = MessageStrings.OperationFailed;
            return AppUpdatePhase.Failed;
        }
        finally
        {
            cache?.Dispose();
        }
    }

    private async Task<AppUpdatePhase> PrepareFlatpakUpdateAsync()
    {
        FlatpakUpdater? flatpak = null;
        try
        {
            flatpak = _flatpak ?? FlatpakUpdater.Create();
            _downloadFile = await DownloadFile(flatpak.DownloadPath, _httpClient);
            if (_downloadFile == null) return AppUpdatePhase.Failed;
            await PrepareExtensionUpdatesAsync();
            _cts.Token.ThrowIfCancellationRequested();
            // Keep the bundle until the user chooses Install. Retrying an explicitly requested
            // installation already has that choice, including when the host asks for authorization again.
            Task installRequested = _flatpakInstallRequested!.Task;
            if (!installRequested.IsCompleted)
            {
                IsPrimaryButtonEnabled.Value = true;
                SecondaryButtonText.Value = MessageStrings.UpdateAction_Later;
                Phase.Value = AppUpdatePhase.Ready;
                await installRequested.WaitAsync(_cts.Token);
            }

            _cts.Token.ThrowIfCancellationRequested();
            IsPrimaryButtonEnabled.Value = false;
            SecondaryButtonText.Value = MessageStrings.UpdateAction_RunInBackground;
            ProgressText.Value = ExtensionsStrings.Installing;
            IsIndeterminate.Value = true;
            Phase.Value = AppUpdatePhase.Installing;
            await flatpak.InstallAsync(_cts.Token);
            ProgressText.Value = MessageStrings.FlatpakUpdateCompleted;
            return AppUpdatePhase.Completed;
        }
        catch (OperationCanceledException) { ProgressText.Value = MessageStrings.Canceled; return AppUpdatePhase.Canceled; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install update");
            ProgressText.Value = ex.Message;
            ProgressValue.Value = 0;
            return AppUpdatePhase.Failed;
        }
        finally
        {
            IsPrimaryButtonEnabled.Value = false;
            IsIndeterminate.Value = false;
            CloseButtonText.Value = Strings.Close;
            try { if (flatpak != null) File.Delete(flatpak.DownloadPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { _logger.LogWarning(ex, "Failed to remove update download"); }
        }
    }

    internal static string FormatDownloadProgress(long downloaded, long total)
        => $"{MessageStrings.Downloading} {downloaded * 100 / total}% "
           + $"({StringFormats.ToHumanReadableSize(downloaded)} / {StringFormats.ToHumanReadableSize(total)})";

    // Names the step that failed, then the cause when it adds something.
    internal static string DescribeFailure(string step, Exception exception)
        => exception.Message == step ? step : $"{step}\n{exception.Message}";

    // Where an update is unpacked before the update script copies it over the application.
    private static string UpdateStagingRoot
        => Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "tmp", "update");

    // Only the Linux zip and the macOS app install from an unpacked update. Windows and macOS zips
    // are no longer published and have no update path.
    private static bool Unpacks(AssetMetadataJson metadata)
        => (metadata.Type == "zip" && OperatingSystem.IsLinux()) || metadata.Type == "app";

    // Every running Beutl unpacks its update into the same folders, and unpacking empties them first. So only the Beutl
    // that claims them unpacks one, or starts one unasked, and the claim lasts until the process exits. The lock file
    // stays, since unlinking it could split ownership.
    internal static bool ClaimUpdateFolders()
    {
        lock (s_claimGate)
        {
            s_updateFolderClaim ??= TryClaim(Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "tmp", "update.lock"));
            return s_updateFolderClaim != null;
        }
    }

    // The lock on path, or null while another process holds it.
    internal static FileStream? TryClaim(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

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
            ProgressText.Value = DescribeFailure(MessageStrings.OperationFailed, ex);
            return false;
        }

        var result = await ExtractIfNeeded(metadata, file, destination);
        if (!result) return false;

        ProgressText.Value = MessageStrings.ApplicationRestartRequired;
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
    private string ResolveDownloadTarget(HttpResponseMessage response, string? destinationPath, string? downloadDirectory)
    {
        var directory = destinationPath != null
            ? Path.GetDirectoryName(Path.GetFullPath(destinationPath))!
            : downloadDirectory ?? DownloadDirectory;
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (destinationPath != null)
            return Path.GetFullPath(destinationPath);

        string name = UpdateDownloadCache.NormalizeFileName(response.Content.Headers.ContentDisposition?.FileName)
            ?? UpdateDownloadCache.NormalizeFileName(Uri.TryCreate(Update.DownloadUrl, UriKind.Absolute, out Uri? uri)
                ? Uri.UnescapeDataString(uri.AbsolutePath)
                : null)
            ?? "update";

        string file = Path.GetFullPath(Path.Combine(directory, name));
        if (!FilePathComparison.IsSameOrDescendant(directory, file))
            throw new InvalidDataException(MessageStrings.DownloadFailed);

        _logger.LogInformation("Resolved download file name: {FileName}", name);
        return file;
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
    internal async Task<string?> DownloadFile(string? destinationPath = null, HttpClient? httpClient = null,
        long? maximumBytes = null, UpdateDownloadCache? cache = null)
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

            string? expectedHash = NormalizeSha256(Update.Sha256);
            long limit = maximumBytes ?? (IsFlatpak ? FlatpakUpdater.MaximumBundleBytes : long.MaxValue);
            if (cache != null && expectedHash != null
                && await cache.FindVerifiedAsync(expectedHash, limit, ct) is { } cachedFile)
            {
                _logger.LogInformation("Reusing verified update {FilePath}", cachedFile);
                return CompleteDownload(cachedFile, cache);
            }

            _logger.LogInformation("Downloading update from {DownloadUrl}", Update.DownloadUrl);
            // A client that was passed in outlives this attempt, so a retry can use it again.
            using var ownedClient = httpClient == null ? new HttpClient() : null;
            HttpClient client = httpClient ?? ownedClient!;
            using var response =
                await client.GetAsync(Update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            // A redirect may leave HTTPS, so check where the response actually came from.
            if (response.RequestMessage?.RequestUri is { } finalUri
                && !string.Equals(finalUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(MessageStrings.DownloadFailed);

            long? contentLength = response.Content.Headers.ContentLength;
            if (contentLength > limit) throw new InvalidDataException(MessageStrings.DownloadFailed);
            var file = ResolveDownloadTarget(response, destinationPath, cache?.DirectoryPath);

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
                long shownPercent = -1;
                int bytesRead;
                while ((bytesRead = await download.ReadAsync(buffer, ct).ConfigureAwait(false)) != 0)
                {
                    totalBytesRead += bytesRead;
                    if (totalBytesRead > limit || (contentLength.HasValue && totalBytesRead > contentLength.Value))
                        throw new InvalidDataException(MessageStrings.DownloadFailed);
                    hash?.AppendData(buffer, 0, bytesRead);
                    await destination.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);
                    if (contentLength > 0)
                    {
                        ProgressValue.Value = totalBytesRead / (double)contentLength.Value;
                        long percent = totalBytesRead * 100 / contentLength.Value;
                        if (percent != shownPercent)
                        {
                            shownPercent = percent;
                            DownloadProgress.Value = percent / 100.0;
                            ProgressText.Value = FormatDownloadProgress(totalBytesRead, contentLength.Value);
                        }
                    }
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

            ct.ThrowIfCancellationRequested();
            File.Move(temporaryPath, file, overwrite: true);
            temporaryPath = null;

            _logger.LogInformation("Downloaded update to {FilePath}", file);
            return CompleteDownload(file, cache);
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            _logger.LogWarning("Download canceled");
            ProgressText.Value = MessageStrings.Canceled;
            return null;
        }
        catch (Exception e)
        {
            // Includes the HTTP client's own timeout, which is a failure the user did not ask for.
            _logger.LogError(e, "Failed to download update");
            ProgressText.Value = DescribeFailure(MessageStrings.DownloadFailed, e);
            return null;
        }
        finally
        {
            DownloadProgress.Value = null;
            if (temporaryPath != null)
            {
                try { File.Delete(temporaryPath); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { _logger.LogWarning(ex, "Failed to remove the incomplete update download"); }
            }
        }
    }

    private string CompleteDownload(string file, UpdateDownloadCache? cache)
    {
        cache?.RemoveOtherDownloads(file);
        ProgressText.Value = MessageStrings.DownloadComplete;
        ProgressValue.Value = 1;
        IsIndeterminate.Value = false;
        return file;
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

            if (string.IsNullOrWhiteSpace(Update.Sha256)) File.Delete(file);
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
            ProgressText.Value = DescribeFailure(MessageStrings.OperationFailed, e);
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
        if (_installOnQuit && MainViewModel.TryGetCurrent(out MainViewModel? main))
        {
            main.SetUpdateOnQuit(null);
        }

        if (Phase.Value is AppUpdatePhase.Preparing or AppUpdatePhase.Ready or AppUpdatePhase.Installing or AppUpdatePhase.Failed)
        {
            Phase.Value = AppUpdatePhase.Canceled;
        }
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
