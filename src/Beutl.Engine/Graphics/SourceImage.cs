using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.Graphics;

[Display(Name = nameof(Strings.Image), ResourceType = typeof(Strings))]
public partial class SourceImage : Drawable
{
    public SourceImage()
    {
        ScanProperties<SourceImage>();
    }

    [Display(Name = nameof(Strings.Source), ResourceType = typeof(Strings))]
    public IProperty<ImageSource?> Source { get; } = Property.Create<ImageSource?>();

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource)
    {
        var r = (Resource)resource;
        if (r.Source != null)
        {
            return r.Source.FrameSize.ToSize(1);
        }
        else
        {
            return default;
        }
    }

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
        var r = (Resource)resource;
        if (r.Source != null)
        {
            context.DrawImageSource(r.Source, Brushes.Resource.White, null);
        }
    }
}
