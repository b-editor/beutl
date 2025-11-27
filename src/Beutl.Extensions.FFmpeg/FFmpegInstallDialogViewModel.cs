using Beutl.Extensions.FFmpeg.Properties;
using Beutl.Logging;

using Microsoft.Extensions.Logging;

using Reactive.Bindings;

#if FFMPEG_BUILD_IN
namespace Beutl.Embedding.FFmpeg;
#else
namespace Beutl.Extensions.FFmpeg;
#endif

public class FFmpegInstallDialogViewModel : IDisposable
{
    private readonly ILogger _logger = Log.CreateLogger<FFmpegInstallDialogViewModel>();
    private readonly FFmpegInstallService _installService;
    private bool _disposed;

    public FFmpegInstallDialogViewModel()
    {
        _installService = new FFmpegInstallService();
        _installService.ProgressTextChanged += OnProgressTextChanged;
        _installService.ProgressChanged += OnProgressChanged;
        _installService.IndeterminateChanged += OnIndeterminateChanged;
        _installService.Completed += OnCompleted;

        InstallMethod = FFmpegInstallService.GetRecommendedMethod();
    }

    public FFmpegInstallMethod InstallMethod { get; }

    public ReactiveProperty<string> ProgressText { get; } = new(Strings.Preparing);

    public ReactiveProperty<double> ProgressValue { get; } = new(0);

    public ReactiveProperty<double> ProgressMax { get; } = new(1);

    public ReactiveProperty<bool> IsIndeterminate { get; } = new(true);

    public ReactiveProperty<bool> IsCompleted { get; } = new(false);

    public ReactiveProperty<bool> IsSuccess { get; } = new(false);

    public string InstallMethodDescription => InstallMethod switch
    {
        FFmpegInstallMethod.BtbNBuilds => Strings.Download_FFmpeg_from_BtbN,
        FFmpegInstallMethod.Homebrew => Strings.Install_FFmpeg_using_Homebrew,
        _ => Strings.Unknown_method
    };

    public void Start()
    {
        _logger.LogInformation("Starting FFmpeg installation");
        Task.Run(() => _installService.InstallAsync());
    }

    public void Cancel()
    {
        _logger.LogInformation("Canceling FFmpeg installation");
        _installService.Cancel();
    }

    private void OnProgressTextChanged(string text)
    {
        ProgressText.Value = text;
    }

    private void OnProgressChanged(double current, double max)
    {
        ProgressValue.Value = current;
        ProgressMax.Value = max;
    }

    private void OnIndeterminateChanged(bool isIndeterminate)
    {
        IsIndeterminate.Value = isIndeterminate;
    }

    private void OnCompleted(bool success)
    {
        IsCompleted.Value = true;
        IsSuccess.Value = success;
        _logger.LogInformation("FFmpeg installation completed: {Success}", success);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _installService.ProgressTextChanged -= OnProgressTextChanged;
        _installService.ProgressChanged -= OnProgressChanged;
        _installService.IndeterminateChanged -= OnIndeterminateChanged;
        _installService.Completed -= OnCompleted;

        ProgressText.Dispose();
        ProgressValue.Dispose();
        ProgressMax.Dispose();
        IsIndeterminate.Dispose();
        IsCompleted.Dispose();
        IsSuccess.Dispose();
    }
}
