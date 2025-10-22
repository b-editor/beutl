using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Beutl.Graphics.Rendering;
using Brushes = Beutl.Media.Brushes;
using TextBlock = Beutl.Graphics.Shapes.TextBlock;

namespace TextFormattingPlayground;

public partial class MainWindow : Window
{
    private readonly TextBlock _text;
    private readonly TextBlock.Resource _resource;
    private readonly DrawableRenderNode _node;

    public MainWindow()
    {
        InitializeComponent();
        _text = new TextBlock()
        {
            Size = { CurrentValue = 70 }, Fill = { CurrentValue = Brushes.White }
        };

        _resource = _text.ToResource(RenderContext.Default);
        _node = new DrawableRenderNode(_resource);

        textBox.GetObservable(TextBox.TextProperty).Subscribe(TextChanged);
#if DEBUG
        this.AttachDevTools();
#endif
    }

    private void TextChanged(string? obj)
    {
        try
        {
            _text.Text.CurrentValue = obj ?? string.Empty;
            Draw();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }

    private unsafe void Draw()
    {
        var updateOnly = false;
        var oldVersion = _resource.Version;
        _resource.Update(_text, RenderContext.Default, ref updateOnly);
        if (_resource.Version != oldVersion) return;

        using (var context = new GraphicsContext2D(_node,
                   new Beutl.Media.PixelSize((int)image.Bounds.Width, (int)image.Bounds.Height)))
        {
            context.Clear(Beutl.Media.Colors.Black);
            _text.Render(context, _resource);
        }

        var processor = new RenderNodeProcessor(_node, false);
        using var bmp = processor.RasterizeAndConcat();

        if (image.Source is WriteableBitmap old)
        {
            image.Source = null;
            old.Dispose();
        }

        var wbmp = new WriteableBitmap(
            new PixelSize(bmp.Width, bmp.Height),
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
