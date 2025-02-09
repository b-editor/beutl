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
            var downloadFile = Path.GetTempFileName();
            var result = await DownloadFile(downloadFile);
            if (!result) return;

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
                result = await ExtractIfNeeded(downloadFile, destination);
                if (!result) return;

                IsPrimaryButtonEnabled.Value = true;
            }
        });
    }

    private async Task<bool> DownloadFile(string file)
    {
        try
        {
            ProgressValue.Value = 0;
            ProgressMax.Value = 1;
            IsIndeterminate.Value = false;
            ProgressText.Value = "ダウンロード中...";
            var ct = _cts.Token;

            await using var destination = File.Create(file);
            using var client = new HttpClient();
            using var response =
                await client.GetAsync(Update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            long? contentLength = response.Content.Headers.ContentLength;
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

            return true;
        }
        catch (OperationCanceledException)
        {
            ProgressText.Value = "キャンセルされました";
            return false;
        }
        catch (Exception e)
        {
            ProgressText.Value = e.Message;
            return false;
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
