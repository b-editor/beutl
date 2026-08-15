using System.Reflection;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class HeadlessSessionIsolationTests
{
    [Test]
    public void Assembly_opts_out_of_per_test_isolation()
    {
        AvaloniaTestIsolationAttribute? isolation = typeof(TestAppBuilder).Assembly
            .GetCustomAttribute<AvaloniaTestIsolationAttribute>();

        Assert.That(isolation, Is.Not.Null);
        Assert.That(isolation!.IsolationLevel, Is.EqualTo(AvaloniaTestIsolationLevel.PerAssembly));
    }

    // The precondition Dispatcher.PushFrame checks; HeadlessUnitTestSession calls it for every test
    // whose body does not complete synchronously, so losing it makes async tests fail at random.
    [AvaloniaTest]
    public void UI_dispatcher_supports_run_loops()
    {
        Assert.That(Dispatcher.UIThread.SupportsRunLoops, Is.True);
    }
}
