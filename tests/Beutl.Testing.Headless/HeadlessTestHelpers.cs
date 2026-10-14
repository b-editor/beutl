using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
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

    // Waits until the condition holds, settling the dispatcher between checks unless pump is false (off the UI
    // thread), and throws a TimeoutException if it still does not hold after the timeout (10 seconds unless given).
    public static async Task WaitUntilAsync(
        Func<bool> condition, TimeSpan? timeout = null, string? message = null, bool pump = true)
    {
        var elapsed = Stopwatch.StartNew();
        TimeSpan limit = timeout ?? TimeSpan.FromSeconds(10);
        while (!condition())
        {
            if (elapsed.Elapsed >= limit)
            {
                throw new TimeoutException(message ?? "The expected state was not reached.");
            }

            if (pump)
            {
                Settle();
            }

            await Task.Delay(10);
        }
    }

    // The center of the visual, in relativeTo's coordinates.
    public static Point Center(Visual visual, Visual relativeTo)
        => visual.TranslatePoint(new Point(visual.Bounds.Width / 2, visual.Bounds.Height / 2), relativeTo)
            ?? throw new InvalidOperationException("The visuals are not in the same visual tree.");

    // Clicks the control at local, its center unless given, through the top level that shows it (a popup's own
    // top level for a control in a popup), moving the pointer there first when move is true, and then renders
    // the given number of ticks (0 only settles).
    public static void Click(
        Control control, Point? local = null, MouseButton button = MouseButton.Left,
        RawInputModifiers modifiers = RawInputModifiers.None, bool move = false, int ticks = 1)
    {
        TopLevel topLevel = TopLevel.GetTopLevel(control)
            ?? throw new InvalidOperationException("The control is not shown.");
        Point point = local is { } position
            ? control.TranslatePoint(position, topLevel)
                ?? throw new InvalidOperationException("The control is not in the top level's visual tree.")
            : Center(control, topLevel);
        if (move)
        {
            topLevel.MouseMove(point, modifiers);
        }

        topLevel.MouseDown(point, button, modifiers);
        topLevel.MouseUp(point, button, modifiers);
        if (ticks > 0)
        {
            Render(ticks);
        }
        else
        {
            Settle();
        }
    }

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

    // Reads one pixel of a 32bpp RGBA or BGRA frame.
    public static (int R, int G, int B) ReadPixel(WriteableBitmap frame, PixelPoint point)
    {
        using ILockedFramebuffer buffer = frame.Lock();
        nint pixel = buffer.Address + point.Y * buffer.RowBytes + point.X * 4;
        byte first = Marshal.ReadByte(pixel);
        byte second = Marshal.ReadByte(pixel, 1);
        byte third = Marshal.ReadByte(pixel, 2);
        if (buffer.Format == PixelFormat.Rgba8888)
        {
            return (first, second, third);
        }

        if (buffer.Format == PixelFormat.Bgra8888)
        {
            return (third, second, first);
        }

        throw new InvalidOperationException($"Expected a 32bpp RGBA or BGRA frame, but it is {buffer.Format}.");
    }
}
