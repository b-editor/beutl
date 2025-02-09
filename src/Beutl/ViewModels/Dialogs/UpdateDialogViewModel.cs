using System.IO.Compression;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Beutl.Api;
using Beutl.Api.Clients;
using Reactive.Bindings;

namespace Beutl.ViewModels.Dialogs;

public class UpdateDialogViewModel
{
    private readonly CancellationTokenSource _cts = new();
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
        var metadata = await BeutlApiApplication.LoadMetadata();
        if (metadata == null)
        {
            ProgressText.Value = "メタデータの取得に失敗しました";
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            await InstallOnOSX(metadata);
        }
        else if (OperatingSystem.IsWindows())
        {
            await InstallOnWindows(metadata);
        }
    }

    private async Task InstallOnWindows(AssetMetadataJson metadata)
    {
        if (metadata.Type == "installer")
        {
            if (_downloadFile == null)
            {
                ProgressText.Value = "ダウンロードに失敗しました";
                return;
            }

            var psi = new ProcessStartInfo(_downloadFile) { UseShellExecute = true, Verb = "open" };
            Process.Start(psi);
            (Application.Current?.ApplicationLifetime as IControlledApplicationLifetime)?.Shutdown();
        }
        else if (metadata.Type == "zip")
        {
            var script =
                typeof(UpdateDialogViewModel).Assembly.GetManifestResourceStream("Beutl.Resources.win-update.ps1");
            if (script == null)
            {
                ProgressText.Value = "スクリプトの読み込みに失敗しました";
                return;
            }

            var directory = Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "tmp", "update",
                new DirectoryInfo(AppContext.BaseDirectory).Name);
            var target = AppContext.BaseDirectory;

            var scriptPath = Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "tmp", "update.ps1");
            await using (var fs = File.Create(scriptPath))
            {
                await script.CopyToAsync(fs);
            }

            var psi = new ProcessStartInfo("powershell")
            {
                UseShellExecute = true,
                Verb = "open",
                ArgumentList =
                {
                    "-ExecutionPolicy",
                    "Bypass",
                    "-File",
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
    }

    private async Task InstallOnOSX(AssetMetadataJson metadata)
    {
        var script = typeof(UpdateDialogViewModel).Assembly.GetManifestResourceStream("Beutl.Resources.osx-update.sh");
        if (script == null)
        {
            ProgressText.Value = "スクリプトの読み込みに失敗しました";
            return;
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

        var scriptPath = Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "tmp", "update.sh");
        await using (var fs = File.Create(scriptPath))
        {
            await script.CopyToAsync(fs);
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
            _downloadFile = await DownloadFile();
            if (_downloadFile == null) return;

            var metadata = await BeutlApiApplication.LoadMetadata();
            if (metadata == null)
            {
                ProgressText.Value = "メタデータの取得に失敗しました";
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
                var result = await ExtractIfNeeded(_downloadFile, destination);
                if (!result) return;

                ProgressText.Value = "アプリケーションの再起動が必要です。\n再起動しますか？";
                IsPrimaryButtonEnabled.Value = true;
            }

            if (metadata.Type == "installer")
            {
                ProgressText.Value = "インストーラーを起動します";
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
            ProgressText.Value = "ダウンロード中...";
            var ct = _cts.Token;

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

            file = Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "tmp", file);

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

            ProgressText.Value = "ダウンロードが完了しました";
            ProgressValue.Value = 1;
            IsIndeterminate.Value = false;

            return file;
        }
        catch (OperationCanceledException)
        {
            ProgressText.Value = "キャンセルされました";
            return null;
        }
        catch (Exception e)
        {
            ProgressText.Value = e.Message;
            return null;
        }
    }

    private async Task<bool> ExtractIfNeeded(string file, string destination)
    {
        var ct = _cts.Token;
        using var source = ZipFile.Open(file, ZipArchiveMode.Read);
        ProgressMax.Value = source.Entries.Count;
        ProgressText.Value = "展開中...";
        foreach (var entry in source.Entries)
        {
            if (entry.Length != 0)
            {
                var dst = Path.Combine(destination, string.Join(Path.DirectorySeparatorChar, entry.FullName));
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                await using var fs = File.Create(dst);
                await using var es = entry.Open();
                await es.CopyToAsync(fs, ct).ConfigureAwait(false);
            }

            ProgressValue.Value++;
        }

        ProgressText.Value = "展開が完了しました";
        ProgressValue.Value = ProgressMax.Value;
        IsIndeterminate.Value = false;
        return true;
    }

    public void Cancel()
    {
        if (_cts.IsCancellationRequested) return;
        _cts.Cancel();
    }
}
