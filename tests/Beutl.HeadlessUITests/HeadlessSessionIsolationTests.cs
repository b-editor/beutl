using System.Reflection;
using Avalonia.Headless;

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
}
