using Beutl.Graphics;
using Beutl.Media.Source;
using Beutl.Operation;
using Beutl.Styling;

namespace Beutl.Operators.Source;

public sealed class SourceImageOperator : DrawablePublishOperator<SourceImage>
{
    private string? _sourceName;

    protected override void OnInitializeSetters(IList<ISetter> initializing)
    {
        initializing.Add(new Setter<IImageSource?>(SourceImage.SourceProperty, null));
    }

    protected override void OnDetachedFromHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnDetachedFromHierarchy(args);
        if (GetImageSourceSetter() is Setter<IImageSource> { Value: { Name: string name } value } setter)
        {
            _sourceName = name;
            setter.Value = null;
            value.Dispose();
        }
    }

    protected override void OnAttachedToHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnAttachedToHierarchy(args);
        if (GetImageSourceSetter() is Setter<IImageSource> { Value: null } setter
            && _sourceName != null
            && MediaSourceManager.Shared.OpenImageSource(_sourceName, out IImageSource? imageSource))
        {
            setter.Value = imageSource;
        }
    }

    private Setter<IImageSource>? GetImageSourceSetter()
    {
        return Style.Setters.FirstOrDefault(x => x.Property == SourceImage.SourceProperty) as Setter<IImageSource>;
    }
}
