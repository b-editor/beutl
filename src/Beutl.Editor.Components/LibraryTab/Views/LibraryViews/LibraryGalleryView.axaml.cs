using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Beutl.Editor.Components.LibraryTab.ViewModels;
using Beutl.Services;

namespace Beutl.Editor.Components.LibraryTab.Views.LibraryViews;

// The library's effects or transitions as tiles that play their thumbnails under the pointer. A tile is dragged
// onto the timeline as the same item is from the library's tree.
public partial class LibraryGalleryView : UserControl
{
    private readonly string _format;
    private IDisposable? _preview;

    public LibraryGalleryView()
        : this(KnownLibraryItemFormats.FilterEffect)
    {
    }

    public LibraryGalleryView(string format)
    {
        _format = format;
        InitializeComponent();
        Repeater.AddHandler(PointerPressedEvent, OnTilePointerPressed, RoutingStrategies.Tunnel);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Repeater.ItemsSource = DataContext is LibraryTabViewModel viewModel
            ? _format == KnownLibraryItemFormats.ClipTransition ? viewModel.Transitions : viewModel.Effects
            : null;
        if (VisualRoot != null)
            HoldPreview();
    }

    // The preview draws the clean frame the tiles show only while a gallery is on screen.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        HoldPreview();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _preview?.Dispose();
        _preview = null;
    }

    private void HoldPreview()
    {
        _preview?.Dispose();
        _preview = (DataContext as LibraryTabViewModel)?.AcquirePreview();
    }

    private async void OnTilePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(Repeater).Properties.IsLeftButtonPressed
            || (e.Source as StyledElement)?.DataContext is not LibraryGalleryItemViewModel tile)
        {
            return;
        }

        await LibraryTreeDragHelper.StartDragAsync(e, tile.Item);
    }
}
