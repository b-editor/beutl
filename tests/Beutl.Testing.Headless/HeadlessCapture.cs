using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;

namespace Beutl.Testing.Headless;

// Captures for manual design review: when BEUTL_HEADLESS_CAPTURE_DIR is set, the frame of a visual's top level
// is saved as <fixture>/<name>.png under it. Without the variable nothing is captured.
public static class HeadlessCapture
{
    private const string DirectoryVariable = "BEUTL_HEADLESS_CAPTURE_DIR";

    public static bool IsRequested => Environment.GetEnvironmentVariable(DirectoryVariable) is { Length: > 0 };

    public static void SaveIfRequested(Visual visual, string fixture, string name)
    {
        if (Environment.GetEnvironmentVariable(DirectoryVariable) is not { Length: > 0 } root
            || TopLevel.GetTopLevel(visual) is not { } topLevel)
        {
            return;
        }

        string directory = Path.Combine(root, fixture);
        Directory.CreateDirectory(directory);
        using WriteableBitmap? frame = topLevel.CaptureRenderedFrame();
        frame?.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
    }
}
