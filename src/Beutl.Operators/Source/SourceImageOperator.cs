using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Styling;

namespace Beutl.Operators.Source;

public sealed class SourceImageOperator : DrawablePublishOperator<SourceImage>
{
    private string? _sourceName;

    public Setter<IImageSource?> Source { get; set; } = new(SourceImage.SourceProperty, null);

    public Setter<ITransform?> Transform { get; set; } = new(Drawable.TransformProperty, new TransformGroup());

    public Setter<AlignmentX> AlignmentX { get; set; } = new(Drawable.AlignmentXProperty, Media.AlignmentX.Center);

    public Setter<AlignmentY> AlignmentY { get; set; } = new(Drawable.AlignmentYProperty, Media.AlignmentY.Center);

    public Setter<RelativePoint> TransformOrigin { get; set; } = new(Drawable.TransformOriginProperty, RelativePoint.Center);

    public Setter<IBrush?> Fill { get; set; } = new(Drawable.FillProperty, new SolidColorBrush(Colors.White));

    public Setter<FilterEffect?> FilterEffect { get; set; } = new(Drawable.FilterEffectProperty, new FilterEffectGroup());

    public Setter<BlendMode> BlendMode { get; set; } = new Setter<BlendMode>(Drawable.BlendModeProperty, Graphics.BlendMode.SrcOver);

    protected override void OnDetachedFromHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnDetachedFromHierarchy(args);
        if (Source is { Value: { Name: string name } value } setter)
        {
            _sourceName = name;
            setter.Value = null;
            value.Dispose();
        }
    }

    protected override void OnAttachedToHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnAttachedToHierarchy(args);
        if (Source is { Value: null } setter
            && _sourceName != null
            && BitmapSource.TryOpen(_sourceName, out BitmapSource? imageSource))
        {
            setter.Value = imageSource;
        }
    }
}
