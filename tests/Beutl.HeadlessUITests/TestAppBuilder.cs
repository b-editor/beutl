using Avalonia;
using Avalonia.Headless;
using Beutl.HeadlessUITests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

// Required, not a preference. The default PerTest isolation nulls the static Dispatcher.UIThread
// after every test with no IDispatcherImpl registered, and this suite deliberately keeps editor
// state alive across tests (see TestReset), so a service left running from the previous test pins
// the singleton to the run-loop-less NullDispatcherImpl and the next test dies in PushFrame.
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerAssembly)]

namespace Beutl.HeadlessUITests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
