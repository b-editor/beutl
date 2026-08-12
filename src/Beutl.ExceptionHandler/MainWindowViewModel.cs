using System.Diagnostics;

using Beutl.ExceptionHandler.Properties;

using Reactive.Bindings;

namespace Beutl.ExceptionHandler;

public class MainWindowViewModel
{
    internal const string FeedbackUrl = "https://beutl.beditor.net/feedback";

    private readonly string? _logFile;

    public MainWindowViewModel()
    {
        Header = Resources.ErrorOccurred;
        Content.Value = Resources.Content;

        // Crash markers intentionally contain no exception payload. Full details
        // remain in the local log opened by ShowLog.
        Footer = "See the local log for details.";

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
                Process.Start(new ProcessStartInfo(FeedbackUrl) { UseShellExecute = true });
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
