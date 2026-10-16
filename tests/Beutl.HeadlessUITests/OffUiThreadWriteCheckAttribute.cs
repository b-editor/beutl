using System.Collections.Concurrent;
using Avalonia.Threading;
using NUnit.Framework.Interfaces;

[assembly: Beutl.HeadlessUITests.OffUiThreadWriteCheck]

namespace Beutl.HeadlessUITests;

// Fails the running test when something wrote the open project off the UI thread (#2873).
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class OffUiThreadWriteCheckAttribute : Attribute, ITestAction
{
    private static readonly ConcurrentQueue<string> s_reports = new();
    private static readonly AsyncLocal<bool> s_allowed = new();

    public ActionTargets Targets => ActionTargets.Test;

    public static void Start()
    {
        DocumentWriteCheck.IsOwnerThread = static () => Dispatcher.UIThread.CheckAccess();
        DocumentWriteCheck.Report = Record;
    }

    // For a test that writes from a second thread on purpose; the flag flows into the tasks it starts.
    public static IDisposable Allow()
    {
        s_allowed.Value = true;
        return new Allowance();
    }

    public void BeforeTest(ITest test) => s_reports.Clear();

    public void AfterTest(ITest test)
    {
        var reports = new List<string>();
        while (s_reports.TryDequeue(out string? report))
            reports.Add(report);

        if (reports.Count > 0)
            Assert.Fail(string.Join(Environment.NewLine + Environment.NewLine, reports));
    }

    private static void Record(string message)
    {
        if (s_allowed.Value)
            return;

        Thread thread = Thread.CurrentThread;
        s_reports.Enqueue(
            $"{message} Thread: {thread.Name ?? thread.ManagedThreadId.ToString()}{Environment.NewLine}{Environment.StackTrace}");
    }

    private sealed class Allowance : IDisposable
    {
        public void Dispose() => s_allowed.Value = false;
    }
}
