using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;
using Beutl.Media.Source;
using Beutl.Operation;

namespace Beutl.Operators.Source;

public sealed class SourceImageOperator : PublishOperator<SourceImage>
{
    private Uri? _uri;

    protected override void FillProperties()
    {
        AddProperty(Value.Source);
        AddProperty(Value.Transform, new TransformGroup());
        AddProperty(Value.AlignmentX);
        AddProperty(Value.AlignmentY);
        AddProperty(Value.TransformOrigin);
        AddProperty(Value.FilterEffect, new FilterEffectGroup());
        AddProperty(Value.BlendMode);
        AddProperty(Value.Opacity);
    }

    protected override void OnDetachedFromHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnDetachedFromHierarchy(args);
        if (Value is not { Source.CurrentValue: { Uri: { } uri } source } value) return;

        _uri = uri;
        value.Source.CurrentValue = null;
        source.Dispose();
    }

    protected override void OnAttachedToHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnAttachedToHierarchy(args);
        if (_uri is null) return;
        if (Value is not { } value) return;

        if (BitmapSource.TryOpen(_uri, out BitmapSource? imageSource))
        {
            value.Source.CurrentValue = imageSource;
        }
    }
}
