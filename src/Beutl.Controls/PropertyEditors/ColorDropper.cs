#nullable enable

using Avalonia.Threading;

using FluentAvalonia.UI.Media;

namespace Beutl.Controls.PropertyEditors;

public partial class ColorDropper : IDisposable
{
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Normal);
    private readonly TaskCompletionSource<(Color2, int X, int Y)> _tcs;
    private readonly CancellationToken _ct;

    public ColorDropper(TaskCompletionSource<(Color2, int X, int Y)> tcs, CancellationToken ct)
    {
        _tcs = tcs;
        _ct = ct;
        _timer.Interval = TimeSpan.FromMilliseconds(10);
    }

    public static bool IsSupported =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    public static Task<(Color2, int X, int Y)> Run(CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<(Color2, int X, int Y)>();
        var dropper = new ColorDropper(tcs, ct);
        dropper.Start();
        return tcs.Task;
    }

    public void Start()
    {
        _timer.Start();
        _timer.Tick += Timer_Tick;
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_ct.IsCancellationRequested || IsEscapeDown())
        {
            _timer.Stop();
            _timer.Tick -= Timer_Tick;
            _tcs.TrySetCanceled();
            return;
        }

        if (IsClickDown())
        {
            _timer.Stop();
            _timer.Tick -= Timer_Tick;

            var (x, y) = GetCursorPosition();
            Color2 col = GetColorAtPoint(x, y);
            _tcs.TrySetResult((col, x, y));
        }
    }

    public void Dispose()
    {
    }

    private static bool IsClickDown()
    {
#if WINDOWS
        return WindowsIsClickDown();
#else
        if (OperatingSystem.IsMacOS())
            return MacOSIsClickDown();
        return false;
#endif
    }

    private static bool IsEscapeDown()
    {
#if WINDOWS
        return WindowsIsEscapeDown();
#else
        if (OperatingSystem.IsMacOS())
            return MacOSIsEscapeDown();
        return false;
#endif
    }

    private static (int X, int Y) GetCursorPosition()
    {
#if WINDOWS
        return WindowsGetCursorPosition();
#else
        if (OperatingSystem.IsMacOS())
            return MacOSGetCursorPosition();
        return (0, 0);
#endif
    }

    private static Color2 GetColorAtPoint(int x, int y)
    {
#if WINDOWS
        return WindowsGetColorAtPoint(x, y);
#else
        if (OperatingSystem.IsMacOS())
            return MacOSGetColorAtPoint(x, y);
        return new Color2(255, 0, 0, 0);
#endif
    }
}
