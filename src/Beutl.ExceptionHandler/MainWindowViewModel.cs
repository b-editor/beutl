using System.Diagnostics;

using Beutl.ExceptionHandler.Properties;

using Reactive.Bindings;

namespace Beutl.ExceptionHandler;

public class MainWindowViewModel
{
    private readonly string? _logFile;
    private readonly string? _sessionId;

    public MainWindowViewModel(string? sessionId = null)
    {
        _sessionId = sessionId;
        Header = Resources.ErrorOccurred;
        Content.Value = Resources.Content;

        string path = Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "last-unhandled-exeption");
        if (File.Exists(path))
        {
            Footer = File.ReadAllText(path);
        }
        else
        {
            Footer = "Nothing";
        }

        string logFolder = Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "log");

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

        SendFeedback.Subscribe(() =>
        {
            try
            {
                string url = "https://beutl.beditor.net/feedback";
                if (!string.IsNullOrEmpty(_sessionId))
                {
                    url = $"{url}?traceId={_sessionId}";
                }
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
            }
        });
    }

    public string Header { get; }

    public ReactiveProperty<string> Content { get; } = new();

    public string Footer { get; }

    public ReactiveCommand ShowLog { get; } = new();

    public ReactiveCommand SendFeedback { get; } = new();

    public ReactiveCommand Cancel { get; } = new();
}
