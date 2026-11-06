using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Beutl.Views;

// Holds the frame's place in the preview's layout while an HdrBitmapView shows the frame over the whole viewport. It
// takes the size a BitmapView would, so the guides, the handles and the pointer's frame coordinates stay where they are.
internal sealed class PreviewFrameSlot : Control
{
    public static readonly StyledProperty<Size?> SourceSizeProperty =
        AvaloniaProperty.Register<PreviewFrameSlot, Size?>(nameof(SourceSize));

    static PreviewFrameSlot()
    {
        AffectsMeasure<PreviewFrameSlot>(SourceSizeProperty);
    }

    // The frame's size in pixels, or null while there is none.
    public Size? SourceSize
    {
        get => GetValue(SourceSizeProperty);
        set => SetValue(SourceSizeProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return SourceSize is { } size ? Stretch.Uniform.CalculateSize(availableSize, size) : default;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        return SourceSize is { } size ? Stretch.Uniform.CalculateSize(finalSize, size) : default;
    }
}
