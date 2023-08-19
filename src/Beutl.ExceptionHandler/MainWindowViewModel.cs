using System.Diagnostics;

using Beutl.Api;
using Beutl.ExceptionHandler.Properties;

using DeviceId;

using Reactive.Bindings;

namespace Beutl.ExceptionHandler;

public class MainWindowViewModel
{
    private readonly AppClient _app;
    private readonly string? _logFile;
    private CancellationTokenSource? _cts;

    public MainWindowViewModel()
    {
        _app = new AppClient(new HttpClient())
        {
            //BaseUrl = "https://localhost:7278/"
            BaseUrl = "https://beutl.beditor.net"
        };

        Header = Resources.ErrorOccurred;
        Content.Value = Resources.Content;

        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".beutl", "last-unhandled-exeption");
        if (File.Exists(path))
        {
            Footer = File.ReadAllText(path);
        }
        else
        {
            Footer = "Nothing";
        }

        string logFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".beutl", "log");

        _logFile = Directory.GetFiles(logFolder)
            .OrderDescending()
            .FirstOrDefault();

        ShowLog.Subscribe(() =>
        {
            try
            {
                if (_logFile == null || !File.Exists(_logFile))
                    return;
                Process.Start(new ProcessStartInfo(_logFile)
                {
                    Verb = "open",
                    UseShellExecute = true
                });
            }
            catch
            {
            }
        });

        SendLog.Subscribe(async () =>
        {
            if (Sent.Value)
                return;

            try
            {
                if (_logFile == null || !File.Exists(_logFile))
                    throw new Exception(Resources.LogFileNotFound);

                IsBusy.Value = true;
                _cts?.Cancel();
                _cts = new CancellationTokenSource();
                using FileStream stream = File.OpenRead(_logFile);
                _ = RunProgressReporter(stream, _cts.Token);

                await SendCore(stream, _cts.Token);

                Sent.Value = true;
                Content.Value = Resources.SentTheLogThankYou;
            }
            catch (Exception ex)
            {
                Content.Value = $"""
                {Resources.CouldNotSendLog}
                {ex}
                """;
            }
            finally
            {
                _cts?.Cancel();
                _cts = null;
                IsBusy.Value = false;
            }
        });

        Cancel.Subscribe(() => _cts?.Cancel());
    }

    private async Task SendCore(FileStream stream, CancellationToken cancellationToken)
    {
        string deviceId = new DeviceIdBuilder()
            .AddMacAddress()
            .AddMachineName()
            .AddOsVersion()
            .ToString();

        await _app.SendLogAsync(deviceId, new FileParameter(stream, Path.GetFileName(_logFile!), "text/plain"), cancellationToken);
    }

    private async Task RunProgressReporter(Stream stream, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false)
           && stream.CanRead)
        {
            Progress.Value = stream.Position / (double)stream.Length * 100;
        }
    }

    public string Header { get; }

    public ReactiveProperty<string> Content { get; } = new();

    public string Footer { get; }

    public ReactiveProperty<double> Progress { get; } = new();

    public ReactiveCommand ShowLog { get; } = new();

    public AsyncReactiveCommand SendLog { get; } = new();

    public ReactiveCommand Cancel { get; } = new();

    public ReactiveProperty<bool> IsBusy { get; } = new();

    public ReactiveProperty<bool> Sent { get; } = new();
}
