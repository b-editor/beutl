using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Beutl.Editor.Components.AgentTab.ViewModels;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.AgentTab.Views;

internal sealed partial class AgentImageLightbox : UserControl
{
    private const double ZoomStep = 1.25;
    private static readonly ILogger s_logger = Log.CreateLogger<AgentImageLightbox>();
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Bitmap? _image;
    private string? _path;
    private TopLevel? _topLevel;
    private OverlayLayer? _overlayLayer;
    private IInputElement? _previousFocus;
    private double _zoom = 1;
    private bool _fit = true;
    private bool _imageOnClipboard;
    private bool _isClosed;

    public AgentImageLightbox() => InitializeComponent();

    public static Task<bool> OpenAsync(Control source, AgentImagePreview preview) =>
        OpenAsync(source, preview.Caption, preview.Path, preview.Load);

    public static async Task<bool> OpenAsync(Control source, string caption, string? path, Func<Bitmap?> load)
    {
        ToolTip.SetIsOpen(source, false);
        if (TopLevel.GetTopLevel(source) is not { } topLevel || OverlayLayer.GetOverlayLayer(topLevel) is null)
            return true;
        var image = await Task.Run(load);
        if (image is null)
            return false;
        if (topLevel is Window { IsVisible: false }
            || TopLevel.GetTopLevel(source) != topLevel
            || OverlayLayer.GetOverlayLayer(topLevel) is not { } overlayLayer
            || overlayLayer.Children.OfType<AgentImageLightbox>().Any())
        {
            image.Dispose();
            return true;
        }

        var lightbox = new AgentImageLightbox
        {
            _image = image,
            _path = path
        };
        try
        {
            lightbox.ImageCaption.Text = caption;
            ToolTip.SetTip(lightbox.ImageCaption, path ?? caption);
            lightbox.ImageDimensions.Text = string.Format(CultureInfo.CurrentCulture, AgentStrings.ImageDimensions,
                image.PixelSize.Width, image.PixelSize.Height);
            lightbox.CopyImagePathButton.IsVisible = path is not null;
            lightbox.FullImage.Source = image;
            lightbox.Attach(topLevel, overlayLayer);
            await lightbox._closed.Task;
        }
        finally
        {
            lightbox.Close();
            lightbox.FullImage.Source = null;
            if (!lightbox._imageOnClipboard)
                image.Dispose();
        }
        return true;
    }

    internal void Close()
    {
        if (_isClosed)
            return;
        _isClosed = true;
        if (_topLevel is not null)
        {
            _topLevel.RemoveHandler(KeyDownEvent, OnTopLevelKeyDown);
            _topLevel.PropertyChanged -= OnHostPropertyChanged;
            if (_topLevel is Window ownerWindow)
                ownerWindow.Closed -= OnOwnerClosed;
        }
        if (_overlayLayer is not null)
        {
            _overlayLayer.PropertyChanged -= OnHostPropertyChanged;
            _overlayLayer.Children.Remove(this);
        }
        if (_previousFocus is Visual previousVisual && previousVisual.IsAttachedToVisualTree() && _previousFocus.Focusable)
            _previousFocus.Focus();
        _topLevel = null;
        _overlayLayer = null;
        _previousFocus = null;
        _closed.TrySetResult();
    }

    private void Attach(TopLevel topLevel, OverlayLayer overlayLayer)
    {
        _topLevel = topLevel;
        _overlayLayer = overlayLayer;
        _previousFocus = topLevel.FocusManager?.GetFocusedElement();
        Canvas.SetLeft(this, 0);
        Canvas.SetTop(this, 0);
        topLevel.PropertyChanged += OnHostPropertyChanged;
        overlayLayer.PropertyChanged += OnHostPropertyChanged;
        topLevel.AddHandler(KeyDownEvent, OnTopLevelKeyDown, RoutingStrategies.Tunnel);
        if (topLevel is Window ownerWindow)
            ownerWindow.Closed += OnOwnerClosed;
        SyncHostSize();
        overlayLayer.Children.Add(this);
        ApplyZoom();
        Focus();
    }

    private void OnHostPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TopLevel.ClientSizeProperty || e.Property == BoundsProperty)
            SyncHostSize();
    }

    private void SyncHostSize()
    {
        if (_overlayLayer is null || _topLevel is null)
            return;
        var size = _overlayLayer.Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0)
            size = _overlayLayer.AvailableSize;
        if (size.Width <= 0 || double.IsInfinity(size.Width) || size.Height <= 0 || double.IsInfinity(size.Height))
            size = _topLevel.ClientSize;
        if (size.Width <= 0 || size.Height <= 0)
            return;

        Width = size.Width;
        Height = size.Height;
        UpdateCardBounds(size);
        if (_fit)
            ApplyZoom();
    }

    private void UpdateCardBounds(Size hostSize)
    {
        if (_image is not { } image || hostSize.Width <= 0 || hostSize.Height <= 0)
            return;
        double marginX = hostSize.Width >= 520 ? 48 : 24;
        double marginY = hostSize.Height >= 520 ? 48 : 24;
        double maxWidth = Math.Clamp(hostSize.Width - marginX, 240, 1100);
        double maxHeight = Math.Clamp(hostSize.Height - marginY, 240, 800);
        double fitHeight = maxWidth * image.PixelSize.Height / image.PixelSize.Width;
        LightboxCard.Width = maxWidth;
        LightboxCard.Height = Math.Clamp(fitHeight + 116, Math.Min(360, maxHeight), maxHeight);
    }

    private void OnOwnerClosed(object? sender, EventArgs e) => Close();

    private void OnImageScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_fit && e.ViewportDelta != default)
            ApplyZoom();
    }

    private void OnZoomIn(object? sender, RoutedEventArgs e) => SetZoom(_zoom * ZoomStep);
    private void OnZoomOut(object? sender, RoutedEventArgs e) => SetZoom(_zoom / ZoomStep);
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
        double scaling = _topLevel?.RenderScaling ?? TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        double width = size.Width / scaling;
        double height = size.Height / scaling;
        var viewport = ImageScroll.Viewport;
        if (_fit && viewport.Width > 0 && viewport.Height > 0)
            _zoom = Math.Min(viewport.Width / width, viewport.Height / height);
        FullImage.Width = width * _zoom;
        FullImage.Height = height * _zoom;
        ZoomLabel.Text = _zoom.ToString("P0", CultureInfo.CurrentCulture);
    }

    private async void OnCopyImage(object? sender, RoutedEventArgs e)
    {
        if (_image is not { } image)
            return;
        await AgentClipboardHelper.TryCopyAsync(_topLevel ?? TopLevel.GetTopLevel(this), async clipboard =>
        {
            // Clipboard formats can be requested by another application after this overlay closes.
            _imageOnClipboard = true;
            await clipboard.SetBitmapAsync(image);
            await clipboard.FlushAsync();
        }, s_logger, "Could not copy the agent image.");
    }

    private async void OnCopyPath(object? sender, RoutedEventArgs e)
    {
        if (_path is not { } path)
            return;
        await AgentClipboardHelper.TryCopyTextAsync(_topLevel ?? TopLevel.GetTopLevel(this), path, s_logger, "Could not copy the agent image path.");
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private void OnBackdropReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left)
        {
            e.Handled = true;
            Close();
        }
    }

    private void OnTopLevelKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                e.Handled = true;
                return;
            case Key.Add or Key.OemPlus:
                SetZoom(_zoom * ZoomStep);
                e.Handled = true;
                return;
            case Key.Subtract or Key.OemMinus:
                SetZoom(_zoom / ZoomStep);
                e.Handled = true;
                return;
        }

        if (e.Source is Visual source && !ReferenceEquals(source, this) && !this.IsVisualAncestorOf(source))
            e.Handled = true;
    }
}
