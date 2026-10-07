using Avalonia.Headless.NUnit;
using Beutl.ExceptionHandler;

namespace Beutl.HeadlessUITests;

// Switches BEUTL_HOME for the whole process while it runs.
[TestFixture, NonParallelizable]
public class CrashReportViewModelTests
{
    [AvaloniaTest]
    public void The_crash_report_opens_before_any_log_folder_exists()
    {
        string home = Path.Combine(Path.GetTempPath(), "beutl-crash-home-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(home);
        string? previous = Environment.GetEnvironmentVariable("BEUTL_HOME");
        Environment.SetEnvironmentVariable("BEUTL_HOME", home);
        try
        {
            Assert.That(Directory.Exists(Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "log")), Is.False);

            Assert.DoesNotThrow(() => _ = new MainWindowViewModel());
        }
        finally
        {
            Environment.SetEnvironmentVariable("BEUTL_HOME", previous);
            Directory.Delete(home, recursive: true);
        }
    }
}
