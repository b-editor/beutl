using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;

using BeUtl.Graphics;
using BeUtl.Media.TextFormatting;
using BeUtl.Media.TextFormatting.Compat;

using Canvas = BeUtl.Graphics.Canvas;

namespace TextFormattingPlayground;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        textBox.GetObservable(TextBox.TextProperty).Subscribe(TextChanged);
#if DEBUG
        this.AttachDevTools();
#endif
    }

    private async void TextChanged(string obj)
    {
        try
        {
            await Task.Delay(500);
            Draw(BeUtl.Media.TextFormatting.Compat.FormattedText.Parse(obj ?? string.Empty, FormattedTextInfo.Default with
            {
                Size = 70
            }));
        }
        catch
        {

        }
    }

    private unsafe void Draw(BeUtl.Media.TextFormatting.Compat.FormattedText text)
    {
        text.Measure(BeUtl.Graphics.Size.Empty);
        var bounds = text.Bounds;
        var width = (int)bounds.Width;
        var height = (int)bounds.Height;

        if (width <= 0 || height <= 0)
            return;

        using var canvas = new Canvas(width, height);
        canvas.Clear(BeUtl.Media.Colors.Black);
        text.Draw(canvas);

        using var bmp = canvas.GetBitmap();

        if (image.Source is WriteableBitmap old)
        {
            image.Source = null;
            old.Dispose();
            old.PlatformImpl.Item?.Dispose();
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
