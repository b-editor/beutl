using Beutl.Audio;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Operation;
using Beutl.Styling;

namespace Beutl.Operators.Source;

public sealed class SourceImageOperator() : PublishOperator<SourceImage>(
[
    SourceImage.SourceProperty,
    (Drawable.TransformProperty, () => new TransformGroup()),
    Drawable.AlignmentXProperty,
    Drawable.AlignmentYProperty,
    Drawable.TransformOriginProperty,
    (Drawable.FilterEffectProperty, () => new FilterEffectGroup()),
    Drawable.BlendModeProperty,
    Drawable.OpacityProperty
])
{
    private string? _sourceName;

    protected override void OnDetachedFromHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnDetachedFromHierarchy(args);
        if (Value is not { Source: { Name: { } name } source } value) return;

        _sourceName = name;
        value.Source = null;
        source.Dispose();
    }

    protected override void OnAttachedToHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnAttachedToHierarchy(args);
        if (_sourceName is null) return;
        if (Value is not { } value) return;

        if (BitmapSource.TryOpen(_sourceName, out BitmapSource? imageSource))
        {
            value.Source = imageSource;
        }
    }
}
