using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Beutl.Helpers;
using Beutl.Testing.Headless;
using FluentAvalonia.UI.Windowing;

namespace Beutl.HeadlessUITests;

internal static class NativeWindowFrameTestHost
{
    internal static int Run(string[] args)
    {
        BeutlHomeIsolation.Begin("beutl-native-window-frames");
        OpenALPreload.EnsureLoaded();
        try
        {
            return AppBuilder.Configure<TestApp>().UsePlatformDetect()
                .AfterSetup(_ => Dispatcher.UIThread.Post(CheckMaximizedClientArea))
                .StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        }
        finally
        {
            BeutlHomeIsolation.End();
        }
    }

    private static async void CheckMaximizedClientArea()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
        FAAppWindow? window = null;
        int exitCode = 1;
        try
        {
            window = new FAAppWindow
            {
                Width = 640,
                Height = 480,
                Title = "Beutl auto-hide taskbar regression",
                ShowActivated = false,
                ShowInTaskbar = false
            };
            window.TitleBar.ExtendsContentIntoTitleBar = true;
            window.TitleBar.Height = 40;
            window.ExtendClientAreaTitleBarHeightHint = window.TitleBar.Height;
            window.Show();
            IntPtr hwnd = window.TryGetPlatformHandle()!.Handle;
            AutoHideEdges edges = AutoHideEdges.Bottom;
            // Use the work area so the test reaches the edge without changing the user's taskbar settings.
            WindowsAutoHideTaskbar? helper = WindowsAutoHideTaskbar.TryAttach(window,
                handle => (ReadMonitor(handle).Work.ToPixelRect(), edges));
            Assert.That(helper, Is.Not.Null);

            window.WindowState = WindowState.Maximized;
            PixelRect client = await ReadSettledClientAsync(hwnd);
            PixelRect workArea = ReadMonitor(hwnd).Work.ToPixelRect();
            var withGap = new PixelRect(workArea.X, workArea.Y, workArea.Width, workArea.Height - 2);
            Assert.That(client, Is.EqualTo(withGap), "A maximized FAAppWindow must leave a 2 px gap for the auto-hide taskbar.");
            Console.WriteLine($"Maximized with an auto-hide bottom edge: {client}; work area: {workArea}.");

            edges = AutoHideEdges.None;
            Assert.That(SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x0020 | 0x0002 | 0x0001 | 0x0004 | 0x0010), Is.True);
            client = await ReadSettledClientAsync(hwnd);
            Assert.That(client, Is.EqualTo(ReadMonitor(hwnd).Work.ToPixelRect()), "Without an auto-hide edge, the client must fill the work area.");
            Console.WriteLine($"Maximized without an auto-hide edge: {client}.");

            edges = AutoHideEdges.Bottom;
            window.WindowState = WindowState.FullScreen;
            client = await ReadSettledClientAsync(hwnd);
            Assert.That(client, Is.EqualTo(ReadMonitor(hwnd).Monitor.ToPixelRect()), "Full screen must cover the whole monitor without a gap.");
            Console.WriteLine($"Full-screen with an auto-hide bottom edge: {client}.");
            exitCode = 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
        }
        finally
        {
            window?.Close();
            lifetime.Shutdown(exitCode);
        }
    }

    private static async Task<PixelRect> ReadSettledClientAsync(IntPtr hwnd)
    {
        var elapsed = Stopwatch.StartNew();
        PixelRect previous = ReadClient(hwnd);
        int stableSamples = 0;
        while (elapsed.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(50);
            PixelRect current = ReadClient(hwnd);
            stableSamples = current == previous ? stableSamples + 1 : 0;
            if (stableSamples >= 5)
                return current;
            previous = current;
        }

        throw new TimeoutException("The native client rectangle did not settle.");
    }

    private static PixelRect ReadClient(IntPtr hwnd)
    {
        Assert.That(GetClientRect(hwnd, out NativeRect rect), Is.True);
        var origin = new NativePoint { X = rect.Left, Y = rect.Top };
        Assert.That(ClientToScreen(hwnd, ref origin), Is.True);
        return new PixelRect(origin.X, origin.Y, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    private static MonitorInfo ReadMonitor(IntPtr hwnd)
    {
        var monitor = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfoW(MonitorFromWindow(hwnd, 2), ref monitor))
            throw new InvalidOperationException("Could not read the native window's monitor.");
        return monitor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly PixelRect ToPixelRect() => new(Left, Top, Right - Left, Bottom - Top);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr hwnd, ref NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}

[TestFixture]
public class NativeWindowFrameTests
{
    [TestCase("--native-maximized-auto-hide-taskbar", TestName = "AMaximizedWindowLeavesRoomForAnAutoHideTaskbar", IncludePlatform = "Win")]
    public async Task NativeClientArea(string scenario)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(typeof(NativeWindowFrameTestHost).Assembly.Location);
        start.ArgumentList.Add(scenario);
        using Process process = Process.Start(start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        Assert.That(process.ExitCode, Is.Zero, await output + "\n" + await error);
    }
}
