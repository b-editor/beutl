using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Beutl.Editor.Components.AgentTab.ViewModels;
using Beutl.Logging;
using Beutl.Utilities;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.AgentTab.Views;

internal sealed partial class AgentImageLightboxWindow : Window
{
    private static readonly ILogger s_logger = Log.CreateLogger<AgentImageLightboxWindow>();
    private Bitmap? _image;
    private string? _path;
    private double _zoom = 1;
    private bool _fit = true;
    private bool _imageOnClipboard;

    public AgentImageLightboxWindow() => InitializeComponent();

    public static Task<bool> OpenAsync(Control source, AgentImagePreview preview) =>
        OpenAsync(source, preview.Caption, preview.Path, preview.Load);

    public static async Task<bool> OpenAsync(Control source, string caption, string? path, Func<Bitmap?> load)
    {
        if (GetTopLevel(source) is not Window owner)
            return true;
        var image = await Task.Run(load);
        if (image is null)
            return false;
        if (!owner.IsVisible || GetTopLevel(source) != owner || owner.OwnedWindows.Any(window => window is AgentImageLightboxWindow))
        {
            image.Dispose();
            return true;
        }

        double width = Math.Clamp(owner.ClientSize.Width, 320, 1100);
        double fitHeight = width * image.PixelSize.Height / image.PixelSize.Width;
        var window = new AgentImageLightboxWindow
        {
            Width = width,
            Height = Math.Clamp(fitHeight + 172, 360, Math.Clamp(owner.ClientSize.Height, 360, 800)),
            _image = image,
            _path = path
        };
        try
        {
            window.ImageCaption.Text = caption;
            ToolTip.SetTip(window.ImageCaption, path ?? caption);
            window.ImageDimensions.Text = string.Format(CultureInfo.CurrentCulture, AgentStrings.ImageDimensions,
                image.PixelSize.Width, image.PixelSize.Height);
            window.CopyImagePathButton.IsVisible = path is not null;
            window.FullImage.Source = image;
            window.ApplyZoom();
            await window.ShowDialog(owner);
        }
        finally
        {
            window.FullImage.Source = null;
            if (!window._imageOnClipboard)
                image.Dispose();
        }
        return true;
    }

    private void OnImageScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_fit && e.ViewportDelta != default)
            ApplyZoom();
    }

    private void OnZoomIn(object? sender, RoutedEventArgs e) => SetZoom(_zoom * 1.25);
    private void OnZoomOut(object? sender, RoutedEventArgs e) => SetZoom(_zoom / 1.25);
    private void OnActualSize(object? sender, RoutedEventArgs e) => SetZoom(1);

    private void OnFit(object? sender, RoutedEventArgs e)
    {
        _fit = true;
        ApplyZoom();
        ImageScroll.Offset = default;
    }

    private void SetZoom(double zoom)
    {
        double previousZoom = _zoom;
        var viewport = ImageScroll.Viewport;
        var offset = ImageScroll.Offset;
        _fit = false;
        _zoom = Math.Clamp(zoom, 0.01, 16);
        ApplyZoom();

        var center = new Vector(viewport.Width / 2, viewport.Height / 2);
        var centeredOffset = (offset + center) * (_zoom / previousZoom) - center;
        Dispatcher.UIThread.Post(() => ImageScroll.Offset = centeredOffset, DispatcherPriority.Loaded);
    }

    private void ApplyZoom()
    {
        if (_image is not { } image)
            return;
        var scrollBars = _fit ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        ImageScroll.HorizontalScrollBarVisibility = scrollBars;
        ImageScroll.VerticalScrollBarVisibility = scrollBars;
        var size = image.PixelSize;
        double width = size.Width / RenderScaling;
        double height = size.Height / RenderScaling;
        var viewport = ImageScroll.Viewport;
        if (_fit && viewport.Width > 0 && viewport.Height > 0)
            _zoom = Math.Min(viewport.Width / width, viewport.Height / height);
        FullImage.Width = width * _zoom;
        FullImage.Height = height * _zoom;
        ZoomLabel.Text = _zoom.ToString("P0", CultureInfo.CurrentCulture);
    }

    private async void OnCopyImage(object? sender, RoutedEventArgs e)
    {
        if (Clipboard is not { } clipboard || _image is not { } image)
            return;
        try
        {
            // Clipboard formats can be requested by another application after this window closes.
            _imageOnClipboard = true;
            await clipboard.SetBitmapAsync(image);
            await clipboard.FlushAsync();
        }
        catch (Exception ex) when (!ExceptionHelpers.ContainsFatalFailure(ex))
        {
            s_logger.LogWarning(ex, "Could not copy the agent image.");
        }
    }

    private async void OnCopyPath(object? sender, RoutedEventArgs e)
    {
        if (Clipboard is not { } clipboard || _path is not { } path)
            return;
        try
        {
            await clipboard.SetTextAsync(path);
        }
        catch (Exception ex) when (!ExceptionHelpers.ContainsFatalFailure(ex))
        {
            s_logger.LogWarning(ex, "Could not copy the agent image path.");
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private void OnBackdropReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left)
            Close();
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: Close(); break;
            case Key.Add or Key.OemPlus: SetZoom(_zoom * 1.25); break;
            case Key.Subtract or Key.OemMinus: SetZoom(_zoom / 1.25); break;
            default: return;
        }
        e.Handled = true;
    }
}
