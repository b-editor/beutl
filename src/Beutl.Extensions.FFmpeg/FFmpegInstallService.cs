using System.Text.Json.Serialization;
using Beutl.Extensions.FFmpeg.Properties;
using Beutl.FFmpegIpc;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Extensions.FFmpeg;

internal sealed class GitHubRelease
{
    [JsonPropertyName("assets")] public GitHubAsset[]? Assets { get; set; }
}

internal sealed class GitHubAsset
{
    [JsonPropertyName("name")] public string? Name { get; set; }

    [JsonPropertyName("browser_download_url")]
    public string? BrowserDownloadUrl { get; set; }
}

public enum FFmpegInstallMethod
{
    BtbNBuilds, // Windows, Linux: Download from BtbN/FFmpeg-Builds
    Homebrew // macOS: brew install ffmpeg@8
}

public partial class FFmpegInstallService
{
    private readonly ILogger _logger = Log.CreateLogger<FFmpegInstallService>();
    private readonly CancellationTokenSource _cts = new();
    private const string GitHubReleasesApiUrl = "https://api.github.com/repos/BtbN/FFmpeg-Builds/releases";

    public event Action<string>? ProgressTextChanged;
    public event Action<double, double>? ProgressChanged;
    public event Action<bool>? IndeterminateChanged;
    public event Action<bool>? Completed;

    public static FFmpegInstallMethod GetRecommendedMethod()
    {
        if (OperatingSystem.IsMacOS())
            return FFmpegInstallMethod.Homebrew;
        return FFmpegInstallMethod.BtbNBuilds;
    }

    public static string GetFFmpegInstallPath()
    {
        return Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "ffmpeg");
    }

    public async Task InstallAsync()
    {
        try
        {
            using var client = new HttpClient();
            if (OperatingSystem.IsMacOS())
            {
                await InstallWithHomebrewAsync(client);
            }
            else
            {
                await InstallFromBtbNBuildsAsync(client);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("FFmpeg installation canceled");
            ProgressTextChanged?.Invoke(Language.MessageStrings.Canceled);
            Completed?.Invoke(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FFmpeg installation failed");
            ProgressTextChanged?.Invoke(ex.Message);
            Completed?.Invoke(false);
        }
    }

    private async Task VerifyAndCompleteAsync()
    {
        ProgressTextChanged?.Invoke(Strings.Verifying_FFmpeg_installation);
        IndeterminateChanged?.Invoke(true);

        // Clear the missing flag so verification can start the worker, but do not resume queued proxy jobs yet.
        FFmpegInstallNotifier.MarkVerificationStarted();

        bool verified = false;
        // Workerプロセスを起動してFFmpegの初期化が成功するか確認
        try
        {
            var connection = await FFmpegWorkerProcess.DecodingInstance.EnsureStartedAsync();
            verified = connection.IsConnected;
            _logger.LogInformation("FFmpeg verification via worker process: {Result}", verified ? "success" : "failed");
        }
        catch (Exception ex)
        {
            if (ex is not FFmpegLibrariesNotFoundException)
                _logger.LogError(ex, "FFmpeg verification failed");
        }

        IndeterminateChanged?.Invoke(false);

        if (verified)
        {
            FFmpegInstallNotifier.MarkInstalled();
            ProgressTextChanged?.Invoke(Strings.FFmpeg_installation_successful);
            Completed?.Invoke(true);
        }
        else
        {
            // 検証に失敗したので missing フラグを立て直し、無駄な再起動を防ぐ
            FFmpegInstallNotifier.MarkMissing();
            ProgressTextChanged?.Invoke(Strings.FFmpeg_verification_failed);
            Completed?.Invoke(false);
        }
    }

    public void Cancel()
    {
        if (_cts.IsCancellationRequested) return;
        _logger.LogInformation("Canceling FFmpeg installation");
        _cts.Cancel();
    }
}
