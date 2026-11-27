using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Beutl.Views;

// Holds the frame's place in the preview's layout while an HdrBitmapView shows the frame over the whole viewport. It
// takes the size a BitmapView would, so the guides, the handles and the pointer's frame coordinates stay where they are.
internal sealed partial class PreviewFrameSlot : Control
{
    static PreviewFrameSlot()
    {
        AffectsMeasure<PreviewFrameSlot>(SourceSizeProperty);
    }

    // The frame's size in pixels, or null while there is none.
    [GeneratedStyledProperty]
    public partial Size? SourceSize { get; set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        return SourceSize is { } size ? Stretch.Uniform.CalculateSize(availableSize, size) : default;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        return SourceSize is { } size ? Stretch.Uniform.CalculateSize(finalSize, size) : default;
    }
}
