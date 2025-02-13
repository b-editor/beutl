using System.IO.Compression;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Configuration;
using Beutl.Logging;
using Beutl.Services;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.ViewModels.Dialogs;

public class UpdateDialogViewModel
{
    private readonly CancellationTokenSource _cts = new();
    private readonly ILogger _logger = Log.CreateLogger<UpdateDialogViewModel>();
    private string? _downloadFile;

    public UpdateDialogViewModel(AppUpdateResponse update)
    {
        Update = update;
    }

    public AppUpdateResponse Update { get; set; }

    public ReactiveProperty<string> ProgressText { get; } = new();

    public ReactiveProperty<double> ProgressValue { get; } = new();

    public ReactiveProperty<double> ProgressMax { get; } = new();

    public ReactiveProperty<bool> IsIndeterminate { get; } = new();

    public ReactiveProperty<bool> IsPrimaryButtonEnabled { get; } = new();

    public async Task HandlePrimaryButtonClick()
    {
        try
        {
            var metadata = await BeutlApiApplication.LoadMetadata();
            if (metadata == null)
            {
                ProgressText.Value = Message.Failed_to_load_metadata;
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
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to handle primary button click");
            NotificationService.ShowError("Error", e.Message);
        }
    }

    private async Task InstallOnWindows(AssetMetadataJson metadata)
    {
        if (metadata.Type == "installer")
        {
            if (_downloadFile == null)
            {
                ProgressText.Value = Message.Download_failed;
                return;
            }

            var psi = new ProcessStartInfo(_downloadFile) { UseShellExecute = true, Verb = "open" };
            Process.Start(psi);
            (Application.Current?.ApplicationLifetime as IControlledApplicationLifetime)?.Shutdown();
        }
        else if (metadata.Type == "zip")
        {
            string scriptPath = Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "tmp", "update.sh");
            await using (var fs = File.Create(scriptPath))
            {
                // UTF-8 BOMを書き込む
                await fs.WriteAsync(new byte[] { 0xEF, 0xBB, 0xBF });
                if (!await LoadScript("Beutl.Resources.win-update.ps1", fs))
                {
                    ProgressText.Value = Message.Failed_to_load_script;
                    return;
                }
            }

            var directory = Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "tmp", "update",
                new DirectoryInfo(AppContext.BaseDirectory).Name);
            var target = AppContext.BaseDirectory;

            var psi = new ProcessStartInfo(@"C:\WINDOWS\System32\WindowsPowerShell\v1.0\powershell.EXE")
            {
                WorkingDirectory = BeutlEnvironment.GetHomeDirectoryPath(),
                CreateNoWindow = !Preferences.Default.Get("Updater.ShowWindow", false),
                ArgumentList =
                {
                    "-ExecutionPolicy",
                    "Bypass",
                    "-File",
                    scriptPath,
                    directory,
                    target,
                    "Beutl",
                    Path.Combine(AppContext.BaseDirectory, "Beutl.exe")
                }
            };

            Process.Start(psi);
            (Application.Current?.ApplicationLifetime as IControlledApplicationLifetime)?.Shutdown();
        }
    }

    private async Task InstallOnLinux(AssetMetadataJson metadata)
    {
        if (metadata.Type == "debian")
        {
            if (_downloadFile == null)
            {
                ProgressText.Value = Message.Download_failed;
                return;
            }

            var psi = new ProcessStartInfo("gnome-terminal")
            {
                ArgumentList =
                {
                    "--",
                    "bash",
                    "-c",
                     $"sudo apt update && sudo apt install \"{_downloadFile}\""
                }
            };
            _ = Process.Start(psi);
            (Application.Current?.ApplicationLifetime as IControlledApplicationLifetime)?.Shutdown();
        }
        else if (metadata.Type == "zip")
        {
            string scriptPath = Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "tmp", "update.sh");
            await using (var fs = File.Create(scriptPath))
            {
                if (!await LoadScript("Beutl.Resources.linux-update.sh", fs))
                {
                    ProgressText.Value = Message.Failed_to_load_script;
                    return;
                }
            }

            var directory = Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "tmp", "update",
                new DirectoryInfo(AppContext.BaseDirectory).Name);
            var target = AppContext.BaseDirectory;

            var psi = new ProcessStartInfo("gnome-terminal")
            {
                ArgumentList =
                {
                    "--",
                    "bash",
                    "-c",
                    $"chmod +x \"{scriptPath}\" && \"{scriptPath}\" \"{directory}\" \"{target}\" Beutl \"{Path.Combine(AppContext.BaseDirectory, "Beutl")}\""
                }
            };

            Process.Start(psi);
            (Application.Current?.ApplicationLifetime as IControlledApplicationLifetime)?.Shutdown();
        }
    }

    private async Task InstallOnOSX(AssetMetadataJson metadata)
    {
        string scriptPath = Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "tmp", "update.sh");
        await using (var fs = File.Create(scriptPath))
        {
            if (!await LoadScript("Beutl.Resources.osx-update.sh", fs))
            {
                ProgressText.Value = Message.Failed_to_load_script;
                return;
            }
        }

        var directory = Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "tmp", "update");
        if (metadata.Type == "zip")
        {
            directory = Path.Combine(directory, new DirectoryInfo(AppContext.BaseDirectory).Name);
        }
        else if (metadata.Type == "app")
        {
            directory = Path.Combine(directory, "Beutl.app");
        }

        var target = AppContext.BaseDirectory;
        if (metadata.Type == "app")
        {
            target = Path.GetFullPath("../../", AppContext.BaseDirectory);
        }

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
        Process.Start(psi);
        (Application.Current?.ApplicationLifetime as IControlledApplicationLifetime)?.Shutdown();
    }

    public void Start()
    {
        Task.Run(async () =>
        {
            _logger.LogInformation("Starting update process");
            _downloadFile = await DownloadFile();
            if (_downloadFile == null) return;

            var metadata = await BeutlApiApplication.LoadMetadata();
            if (metadata == null)
            {
                ProgressText.Value = Message.Failed_to_load_metadata;
                return;
            }

            if (metadata.Type is "zip" or "app")
            {
                var destination = Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "tmp", "update");
                if (metadata.Type == "zip")
                {
                    destination = Path.Combine(destination, new DirectoryInfo(AppContext.BaseDirectory).Name);
                }

                if (Directory.Exists(destination))
                {
                    Directory.Delete(destination, true);
                }

                Directory.CreateDirectory(destination);
                var result = await ExtractIfNeeded(metadata, _downloadFile, destination);
                if (!result) return;

                ProgressText.Value = Message.The_application_needs_to_be_restarted;
                IsPrimaryButtonEnabled.Value = true;
            }

            if (metadata.Type is "installer" or "debian")
            {
                ProgressText.Value = Message.Start_the_installer;
                IsPrimaryButtonEnabled.Value = true;
            }
        });
    }

    private async Task<string?> DownloadFile()
    {
        try
        {
            ProgressValue.Value = 0;
            ProgressMax.Value = 1;
            IsIndeterminate.Value = false;
            ProgressText.Value = Message.Downloading;
            var ct = _cts.Token;

            _logger.LogInformation("Downloading update from {DownloadUrl}", Update.DownloadUrl);
            using var client = new HttpClient();
            using var response =
                await client.GetAsync(Update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            long? contentLength = response.Content.Headers.ContentLength;
            var file = response.Content.Headers.ContentDisposition?.FileName;
            if (file == null)
            {
                // urlからファイル名を取得
                var arr = Update.DownloadUrl!.Split('/');
                file = arr[^1].Length == 0 ? arr[^2] : arr[^1];
            }

            _logger.LogInformation("Guessed file name: {FileName}", file);

            var directory = Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "tmp");
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            file = Path.Combine(directory, file);

            await using var destination = File.Create(file);
            await using Stream download = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

            if (!contentLength.HasValue)
            {
                IsIndeterminate.Value = true;
                await download.CopyToAsync(destination, ct).ConfigureAwait(false);
            }
            else
            {
                const int bufferSize = 81920;
                byte[] buffer = new byte[bufferSize];
                long totalBytesRead = 0;
                int bytesRead;
                while ((bytesRead = await download.ReadAsync(buffer, ct).ConfigureAwait(false)) != 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);
                    totalBytesRead += bytesRead;
                    ProgressValue.Value = totalBytesRead / (double)contentLength.Value;
                }
            }

            ProgressText.Value = Message.Download_is_complete;
            ProgressValue.Value = 1;
            IsIndeterminate.Value = false;
            _logger.LogInformation("Downloaded update to {FilePath}", file);

            return file;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Download canceled");
            ProgressText.Value = Message.Canceled;
            return null;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to download update");
            ProgressText.Value = e.Message;
            return null;
        }
    }

    private async Task<bool> ExtractIfNeeded(AssetMetadataJson metadata, string file, string destination)
    {
        var ct = _cts.Token;
        _logger.LogInformation("Extracting update to {Destination}", destination);
        try
        {
            if (metadata.Type is "zip")
            {
                using var source = ZipFile.Open(file, ZipArchiveMode.Read);

                ProgressMax.Value = source.Entries.Count;
                ProgressText.Value = Message.Extracting;
                foreach (var entry in source.Entries)
                {
                    if (entry.Length != 0)
                    {
                        string dst = Path.GetFullPath(Path.Combine(destination, entry.FullName));
                        if (!dst.StartsWith(destination))
                        {
                            _logger.LogError("Entry is outside of the target directory: {Entry}", entry.FullName);
                            throw new InvalidOperationException("Entry is outside of the target directory.");
                        }

                        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                        await using var fs = File.Create(dst);
                        await using var es = entry.Open();
                        await es.CopyToAsync(fs, ct).ConfigureAwait(false);
                    }

                    ProgressValue.Value++;
                }
            }
            else if (metadata.Type is "app")
            {
                // dittoを使って展開
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
                var process = Process.Start(psi);
                if (process == null)
                {
                    _logger.LogError("Failed to start ditto");
                    throw new InvalidOperationException("Failed to start ditto");
                }

                await process.WaitForExitAsync(ct);
            }

            File.Delete(file);
            _logger.LogInformation("Extraction complete");
            ProgressText.Value = Message.Extraction_is_complete;
            ProgressValue.Value = ProgressMax.Value;
            IsIndeterminate.Value = false;
            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Extraction canceled");
            ProgressText.Value = Message.Canceled;
            return false;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to extract update");
            ProgressText.Value = e.Message;
            return false;
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
            await reader.ReadToEndAsync(), [typeof(Strings), typeof(Message)]);
        var script = renderer.Render();
        await writer.WriteAsync(script);
        return true;
    }
}
