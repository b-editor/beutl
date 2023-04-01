using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.Graphics;

public class SourceImage : Drawable
{
    public static readonly CoreProperty<IImageSource?> SourceProperty;
    private IImageSource? _source;

    static SourceImage()
    {
        SourceProperty = ConfigureProperty<IImageSource?, SourceImage>(nameof(Source))
            .Accessor(o => o.Source, (o, v) => o.Source = v)
            .DefaultValue(null)
            .Register();

        AffectsRender<SourceImage>(SourceProperty);
    }

    public IImageSource? Source
    {
        get => _source;
        set => SetAndRaise(SourceProperty, ref _source, value);
    }

    protected override Size MeasureCore(Size availableSize)
    {
        if (_source != null)
        {
            return _source.FrameSize.ToSize(1);
        }
        else
        {
            return default;
        }
    }

    protected override void OnDraw(ICanvas canvas)
    {
        if (_source?.Read(out IBitmap? bitmap) == true)
        {
            using (bitmap)
            {
                canvas.DrawBitmap(bitmap);
            }
        }
    }
}
