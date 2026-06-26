using Avalonia;
using Avalonia.Headless;
using Beutl.HeadlessUITests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Beutl.HeadlessUITests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
