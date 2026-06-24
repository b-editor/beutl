using Avalonia;
using Avalonia.Headless;
using Beutl.E2ETests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Beutl.E2ETests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
