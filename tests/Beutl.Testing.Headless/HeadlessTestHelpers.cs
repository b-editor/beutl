using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Beutl.Testing.Headless;

public static class HeadlessTestHelpers
{
    public static void Settle(int rounds = 2)
    {
        for (int i = 0; i < rounds; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    public static void Render(int ticks = 1)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(ticks);
        Settle();
    }

    // Settles the dispatcher until the condition holds, and throws a TimeoutException if it still
    // does not hold after the timeout (5 seconds unless given).
    public static async Task WaitUntilAsync(Func<bool> condition, TimeSpan? timeout = null, string? message = null)
    {
        var elapsed = Stopwatch.StartNew();
        TimeSpan limit = timeout ?? TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (elapsed.Elapsed >= limit)
            {
                throw new TimeoutException(message ?? "The expected state was not reached.");
            }

            Settle();
            await Task.Delay(10);
        }
    }

    // The center of the visual, in relativeTo's coordinates.
    public static Point Center(Visual visual, Visual relativeTo)
        => visual.TranslatePoint(new Point(visual.Bounds.Width / 2, visual.Bounds.Height / 2), relativeTo)
            ?? throw new InvalidOperationException("The visuals are not in the same visual tree.");

    // Reads the frame the top level rendered last, as 32bpp RGBA or BGRA rows.
    public static (byte[] Pixels, int RowBytes, PixelSize Size) CaptureFramePixels(TopLevel topLevel)
    {
        using WriteableBitmap frame = topLevel.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("The top level has not rendered a frame.");
        using ILockedFramebuffer buffer = frame.Lock();
        if (buffer.Format != PixelFormat.Rgba8888 && buffer.Format != PixelFormat.Bgra8888)
        {
            throw new InvalidOperationException($"Expected a 32bpp RGBA or BGRA frame, but it is {buffer.Format}.");
        }

        var pixels = new byte[buffer.RowBytes * buffer.Size.Height];
        Marshal.Copy(buffer.Address, pixels, 0, pixels.Length);
        return (pixels, buffer.RowBytes, buffer.Size);
    }
}
