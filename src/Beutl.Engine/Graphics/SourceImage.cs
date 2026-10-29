using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.Graphics;

[Display(Name = nameof(GraphicsStrings.SourceImage), ResourceType = typeof(GraphicsStrings))]
public partial class SourceImage : Drawable
{
    public SourceImage()
    {
        ScanProperties<SourceImage>();
    }

    [Display(Name = nameof(GraphicsStrings.Source), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<ImageSource?> Source { get; } = Property.CreateExpression<ImageSource?>();

    public new partial class Resource
    {
        protected override Size MeasureCore(Size availableSize)
        {
            if (Source != null)
            {
                return Source.FrameSize.ToSize(1);
            }
            else
            {
                return default;
            }
        }

        protected override void OnDraw(GraphicsContext2D context)
        {
            if (Source != null)
            {
                context.DrawImageSource(Source, Brushes.Resource.White, null);
            }
        }
    }
}
