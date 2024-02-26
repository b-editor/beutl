#if WINDOWS
#nullable enable

using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

using Avalonia.Threading;

using FluentAvalonia.UI.Media;


namespace Beutl.Controls.PropertyEditors;

public class ColorDropper : IDisposable
{
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Normal);
    private readonly TaskCompletionSource<(Color2, int X, int Y)> _tcs;
    private readonly CancellationToken _ct;

    public ColorDropper(TaskCompletionSource<(Color2, int X, int Y)> tcs, CancellationToken ct)
    {
        _tcs = tcs;
        _ct = ct;
        _timer.Interval = new TimeSpan(0, 0, 0, 0, 10);
    }

    private static bool IsClickDown => User32.GetKeyState(User32.VK_LBUTTON) < 0;

    private static bool IsEscapeDown => User32.GetKeyState(User32.VK_ESCAPE) < 0;

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
        if (_ct.IsCancellationRequested || IsEscapeDown)
        {
            _timer.Stop();
            _timer.Tick -= Timer_Tick;
            _tcs.SetCanceled();
        }

        if (IsClickDown)
        {
            _timer.Stop();
            _timer.Tick -= Timer_Tick;

            Point pos = Cursor.Position;
            Color2 col = GetColor(pos.X, pos.Y);

            _tcs.SetResult((col, pos.X, pos.Y));
        }
    }

    private static Color2 GetColor(int x, int y)
    {
        Screen screen = Screen.PrimaryScreen!;
        using var bitmap = new Bitmap(screen.Bounds.Width, screen.Bounds.Height, PixelFormat.Format32bppArgb);

        using (var bmpGraphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            bmpGraphics.CopyFromScreen(0, 0, 0, 0, bitmap.Size);
        }

        Color c = bitmap.GetPixel(x, y);
        return Color2.FromARGB(c.A, c.R, c.G, c.B);
    }

    public void Dispose()
    {

    }
}

#endif
