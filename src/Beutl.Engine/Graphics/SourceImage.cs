using System.ComponentModel.DataAnnotations;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.Graphics;

[Display(Name = nameof(Strings.Image), ResourceType = typeof(Strings))]
public partial class SourceImage : Drawable
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

    protected override void OnDraw(GraphicsContext2D context)
    {
        if (_source != null)
        {
            context.DrawImageSource(_source, Brushes.White, null);
        }
    }
}
