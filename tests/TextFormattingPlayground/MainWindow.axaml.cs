using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

using Canvas = Beutl.Graphics.ImmediateCanvas;

namespace TextFormattingPlayground;

public partial class MainWindow : Window
{
    private readonly Beutl.Graphics.Shapes.TextBlock _text;

    public MainWindow()
    {
        InitializeComponent();
        _text = new Beutl.Graphics.Shapes.TextBlock()
        {
            Size = 70
        };

        textBox.GetObservable(TextBox.TextProperty).Subscribe(TextChanged);
#if DEBUG
        this.AttachDevTools();
#endif
    }

    private async void TextChanged(string? obj)
    {
        try
        {
            await Task.Delay(500);
            _text.Text = obj ?? string.Empty;
            Draw();
        }
        catch
        {

        }
    }

    private unsafe void Draw()
    {
        _text.Measure(Beutl.Graphics.Size.Infinity);
        var bounds = _text.Bounds;
        var width = (int)bounds.Width;
        var height = (int)bounds.Height;

        if (width <= 0 || height <= 0)
            return;

        using var canvas = new Canvas(width, height);
        canvas.Clear(Beutl.Media.Colors.Black);
        canvas.DrawDrawable(_text);

        using var bmp = canvas.GetBitmap();

        if (image.Source is WriteableBitmap old)
        {
            image.Source = null;
            old.Dispose();
        }

        var wbmp = new WriteableBitmap(
            new PixelSize(canvas.Size.Width, canvas.Size.Height),
            Avalonia.Skia.SkiaPlatform.DefaultDpi,
            Avalonia.Platform.PixelFormat.Bgra8888,
            Avalonia.Platform.AlphaFormat.Premul);

        using (var buf = wbmp.Lock())
        {
            Buffer.MemoryCopy((void*)bmp.Data, (void*)buf.Address, bmp.ByteCount, bmp.ByteCount);
        }

        image.Source = wbmp;
        image.InvalidateVisual();
    }
}
