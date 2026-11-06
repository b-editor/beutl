using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Beutl.Configuration;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Media.Source;
using BtlBitmap = Beutl.Media.Bitmap;
using BtlRect = Beutl.Graphics.Rect;
using BtlStretch = Beutl.Media.Stretch;
using PreviewGamutMapping = Beutl.Media.PreviewGamutMapping;

namespace Beutl.Controls;

/// <summary>
/// A NativeControlHost-based control that renders Beutl.Media.Bitmap directly via Vulkan,
/// bypassing Avalonia's Bgra8888 swapchain to enable HDR display.
/// </summary>
public partial class HdrBitmapView : NativeControlHost
{
    public static readonly StyledProperty<Ref<BtlBitmap>?> SourceProperty =
        AvaloniaProperty.Register<HdrBitmapView, Ref<BtlBitmap>?>(nameof(Source));

    public static readonly StyledProperty<Stretch> StretchProperty =
        AvaloniaProperty.Register<HdrBitmapView, Stretch>(nameof(Stretch), Stretch.Uniform);

    public static readonly StyledProperty<BitmapInterpolationMode> InterpolationModeProperty =
        AvaloniaProperty.Register<HdrBitmapView, BitmapInterpolationMode>(
            nameof(InterpolationMode), BitmapInterpolationMode.HighQuality);

    public static readonly StyledProperty<UIToneMappingOperator> ToneMappingProperty =
        AvaloniaProperty.Register<HdrBitmapView, UIToneMappingOperator>(nameof(ToneMapping),
            UIToneMappingOperator.None);

    public static readonly StyledProperty<float> ToneMappingExposureProperty =
        AvaloniaProperty.Register<HdrBitmapView, float>(nameof(ToneMappingExposure), 0f);

    public static readonly StyledProperty<float> DisplayPeakLuminanceProperty =
        AvaloniaProperty.Register<HdrBitmapView, float>(nameof(DisplayPeakLuminance), 0f);

    internal static readonly StyledProperty<Rect?> FrameRectProperty =
        AvaloniaProperty.Register<HdrBitmapView, Rect?>(nameof(FrameRect));

    internal static readonly StyledProperty<HdrOverlay?> OverlayProperty =
        AvaloniaProperty.Register<HdrBitmapView, HdrOverlay?>(nameof(Overlay));

    internal static readonly DirectProperty<HdrBitmapView, Size?> SourceSizeProperty =
        AvaloniaProperty.RegisterDirect<HdrBitmapView, Size?>(nameof(SourceSize), view => view.SourceSize);

    private VulkanSwapchainRenderer? _renderer;
    private Ref<BtlBitmap>? _clonedSource;
    private Size? _lastSourceSize;
    private bool _drawAtNewSizeQueued;
    private bool _overlayQueued;
    private bool _overlaySent;
    private IntPtr _windowsControl;
    private IntPtr _x11Window;
    private PixelRect? _frameShape;
    private bool _frameShapeKnown;
    private IntPtr _macOSView;
    private IntPtr _macOSScreenColorSpace;
    private PreviewGamutMapping _displayGamut;

    static HdrBitmapView()
    {
        AffectsMeasure<HdrBitmapView>(StretchProperty);
    }

    public Ref<BtlBitmap>? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public Stretch Stretch
    {
        get => GetValue(StretchProperty);
        set => SetValue(StretchProperty, value);
    }

    public BitmapInterpolationMode InterpolationMode
    {
        get => GetValue(InterpolationModeProperty);
        set => SetValue(InterpolationModeProperty, value);
    }

    public UIToneMappingOperator ToneMapping
    {
        get => GetValue(ToneMappingProperty);
        set => SetValue(ToneMappingProperty, value);
    }

    public float ToneMappingExposure
    {
        get => GetValue(ToneMappingExposureProperty);
        set => SetValue(ToneMappingExposureProperty, value);
    }

    // The peak in nit FitToDisplay rolls highlights off into on a Windows HDR display in place of the one it reports,
    // or 0 to use that. Core Animation tone maps on macOS, and elsewhere the display's white is not known.
    public float DisplayPeakLuminance
    {
        get => GetValue(DisplayPeakLuminanceProperty);
        set => SetValue(DisplayPeakLuminanceProperty, value);
    }

    // Where the frame goes in this view, or null to place it by Stretch. It can reach past the view, as a zoomed-in
    // frame does. A view given one fills its layout slot and draws nothing around the frame, where the UI under it
    // shows: its layer is not opaque on macOS, and its window takes the frame's shape on Windows and X11.
    internal Rect? FrameRect
    {
        get => GetValue(FrameRectProperty);
        set => SetValue(FrameRectProperty, value);
    }

    // A copy of what the UI draws over the frame, which the view draws over it in turn.
    internal HdrOverlay? Overlay
    {
        get => GetValue(OverlayProperty);
        set => SetValue(OverlayProperty, value);
    }

    // The size of the frame in pixels, or null while there is none.
    internal Size? SourceSize => _lastSourceSize;

    // The part of the view the frame takes.
    internal Rect FrameRegion => FrameRect is { } frame ? frame.Intersect(new Rect(Bounds.Size)) : new Rect(Bounds.Size);

    protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        if (e.Root is TopLevel topLevel)
        {
            topLevel.ScalingChanged += OnTopLevelScalingChanged;
        }

        if (e.Root is WindowBase window)
        {
            window.PositionChanged += OnWindowPositionChanged;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        VulkanSwapchainRenderer? previous = _renderer;
        base.OnAttachedToVisualTree(e);

        // A native view that moved here from another window keeps its layer, at that window's scale.
        UpdateContentsScale();

        // A Windows control that moved to another window can have a new holder window, which takes the mouse and the
        // shape anew.
        AdoptWindowsHolder();
        UpdateFrameShape();

        // A view attached again keeps its bounds, so OnSizeChanged does not draw on its new native control. Only now
        // does that control have a window, whose screen gives the gamut, and the size the renderer follows. A new view
        // has no size yet and draws from OnSizeChanged.
        if (_renderer != previous)
            RequestRender();
    }

    protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromLogicalTree(e);
        if (e.Root is TopLevel topLevel)
        {
            topLevel.ScalingChanged -= OnTopLevelScalingChanged;
        }

        if (e.Root is WindowBase window)
        {
            window.PositionChanged -= OnWindowPositionChanged;
        }
    }

    // A paused preview gets no new frame when its window moves to another screen, so the move presents it again if
    // that screen's gamut differs.
    private void OnWindowPositionChanged(object? sender, PixelPointEventArgs e)
    {
        if (UpdateDisplayGamut())
            RequestRender();
    }

    private void OnTopLevelScalingChanged(object? sender, EventArgs e)
    {
        UpdateContentsScale();
        UpdateFrameShape();
        QueueOverlay();

        if (_renderer != null && Bounds.Width > 0 && Bounds.Height > 0)
            ResizeRendererAndDraw(_renderer);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SourceProperty)
        {
            Ref<BtlBitmap>? source = Source;
            Ref<BtlBitmap>? clone = source?.TryClone();
            // A queued binding may carry a ref playback already disposed. Retain the last
            // valid image and size until a live frame arrives, while null still clears them.
            if (source != null && clone == null)
                return;

            Ref<BtlBitmap>? previous = _clonedSource;
            _clonedSource = clone;
            previous?.Dispose();

            if (SetAndRaise(SourceSizeProperty, ref _lastSourceSize, GetSourceSize()))
                InvalidateMeasure();

            RequestRender();
        }
        else if (change.Property == ToneMappingProperty)
        {
            UpdateEdrMetadata();
            RequestRender();
        }
        else if (change.Property == FrameRectProperty)
        {
            // A view that holds a frame rectangle fills its slot instead of taking the frame's size.
            if (change.GetOldValue<Rect?>().HasValue != change.GetNewValue<Rect?>().HasValue)
                InvalidateMeasure();

            UpdateFrameShape();
            // Zooming and panning move the frame many times a second, so it is not uploaded again for each.
            RequestRedraw();
            QueueOverlay();
        }
        else if (change.Property == OverlayProperty)
        {
            if (change.GetOldValue<HdrOverlay?>() is { } oldOverlay)
                oldOverlay.Invalidated -= OnOverlayInvalidated;
            if (change.GetNewValue<HdrOverlay?>() is { } overlay)
                overlay.Invalidated += OnOverlayInvalidated;

            QueueOverlay();
        }
        else if (change.Property == ToneMappingExposureProperty ||
                 change.Property == DisplayPeakLuminanceProperty ||
                 change.Property == StretchProperty ||
                 change.Property == InterpolationModeProperty)
        {
            // Presents the frame again, so a paused preview follows too.
            RequestRender();
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        if (_renderer != null && e.NewSize.Width > 0 && e.NewSize.Height > 0 && !_drawAtNewSizeQueued)
        {
            // A frame drawn before the native view takes this size would be stretched over it, and that has to wait
            // for layout to end: ancestors still being arranged have their old bounds, so its place is not known yet.
            _drawAtNewSizeQueued = true;
            Dispatcher.UIThread.Post(DrawAtNewSize, DispatcherPriority.Loaded);
        }

        // Raises SizeChanged, whose handlers may set a frame that has to wait like any other.
        base.OnSizeChanged(e);
    }

    private void DrawAtNewSize()
    {
        _drawAtNewSizeQueued = false;
        if (_renderer == null || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        // NativeControlHost has moved the native view by now, unless layout ran from the updateLayer of Avalonia's view
        // on macOS, which puts the move off with dispatch_async. This priority never runs there, so it applies at once.
        TryUpdateNativeControlPosition();

        UpdateFrameShape();
        ResizeRendererAndDraw(_renderer);
        QueueOverlay();
    }

    private void ResizeRendererAndDraw(VulkanSwapchainRenderer renderer)
    {
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        renderer.Resize(
            (uint)(Bounds.Width * scaling),
            (uint)(Bounds.Height * scaling));

        RequestRender();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_lastSourceSize == null || FrameRect != null)
            return default;

        return Stretch.CalculateSize(availableSize, _lastSourceSize.Value);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (FrameRect != null)
            return finalSize;

        if (_lastSourceSize == null)
            return default;

        return Stretch.CalculateSize(finalSize, _lastSourceSize.Value);
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        IPlatformHandle handle;

        if (OperatingSystem.IsWindows())
        {
            handle = CreateWindowsControl(parent);
            _windowsControl = handle.Handle;
        }
        else if (OperatingSystem.IsMacOS())
        {
            handle = CreateMacOSControl(parent);
        }
        else if (OperatingSystem.IsLinux())
        {
            handle = CreateLinuxControl(parent);
            _x11Window = handle.Handle;
        }
        else
        {
            throw new PlatformNotSupportedException();
        }

        try
        {
            _renderer = new VulkanSwapchainRenderer();

            var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
            _renderer.Initialize(
                handle.Handle,
                (uint)Math.Max(1, Bounds.Width * scaling),
                (uint)Math.Max(1, Bounds.Height * scaling));
        }
        catch (Exception)
        {
            _renderer?.Dispose();
            _renderer = null;

            if (OperatingSystem.IsWindows())
                DestroyWindow(handle.Handle);
            else if (OperatingSystem.IsMacOS())
                ReleaseMacOSObject(handle.Handle);

            _windowsControl = IntPtr.Zero;
            _x11Window = IntPtr.Zero;
            throw;
        }

        if (OperatingSystem.IsMacOS())
        {
            _macOSView = handle.Handle;
            _displayGamut = PreviewGamutMapping.Srgb;
            UpdateContentsScale();
            UpdateEdrMetadata();
        }

        // DestroyNativeControlCore released the frame, and a view attached again sees no change of Source to bring it
        // back. A ref playback already disposed leaves nothing to show until a live frame arrives, as in
        // OnPropertyChanged.
        _clonedSource ??= Source?.TryClone();

        // On Windows the parent is Avalonia's holder window, which is sized to this view and shown in its place.
        if (OperatingSystem.IsWindows())
            AdoptWindowsHolder(parent.Handle);

        _overlaySent = false;
        _frameShapeKnown = false;
        UpdateFrameShape();
        QueueOverlay();

        return handle;
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        _macOSView = IntPtr.Zero;
        ReleaseMacOSObject(_macOSScreenColorSpace);
        _macOSScreenColorSpace = IntPtr.Zero;
        _windowsControl = IntPtr.Zero;
        _x11Window = IntPtr.Zero;

        _renderer?.Dispose();
        _renderer = null;

        _clonedSource?.Dispose();
        _clonedSource = null;

        if (OperatingSystem.IsWindows())
        {
            DestroyWindow(control.Handle);
        }
        else if (OperatingSystem.IsMacOS())
        {
            ReleaseMacOSObject(control.Handle);
        }
    }

    private void RequestRender()
    {
        // Until the native view has the view's size, a frame would only be stretched over it. DrawAtNewSize draws then.
        if (_renderer == null || _clonedSource == null || _drawAtNewSizeQueued
            || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var bitmapRef = _clonedSource.TryClone();
        if (bitmapRef == null)
            return;

        var bitmap = bitmapRef.Value;
        if (bitmap.IsDisposed)
        {
            bitmapRef.Dispose();
            return;
        }

        UpdateDisplayGamut();
        _renderer.RequestRender(bitmapRef, CreateRenderParams(bitmap));
    }

    // Draws the frame uploaded last again with the view's parameters now.
    private void RequestRedraw()
    {
        if (_renderer == null || _drawAtNewSizeQueued || Bounds.Width <= 0 || Bounds.Height <= 0
            || _clonedSource?.Value is not { IsDisposed: false } bitmap)
        {
            return;
        }

        UpdateDisplayGamut();
        _renderer.RequestRedraw(CreateRenderParams(bitmap));
    }

    private RenderParams CreateRenderParams(BtlBitmap bitmap)
    {
        return new RenderParams(
            SourceWidth: bitmap.Width,
            SourceHeight: bitmap.Height,
            Stretch: (BtlStretch)(int)Stretch,
            ToneMapping: ToneMapping,
            Exposure: ToneMappingExposure,
            IsSourceLinear: bitmap.ColorSpace?.GammaIsLinear == true,
            DisplayGamut: _displayGamut,
            DisplayPeakLuminance: DisplayPeakLuminance,
            Destination: GetDestination());
    }

    // FrameRect in fractions of the view's size.
    private BtlRect? GetDestination()
    {
        if (FrameRect is not { } frame || !(Bounds.Width > 0 && Bounds.Height > 0))
            return null;

        return new BtlRect(
            (float)(frame.X / Bounds.Width),
            (float)(frame.Y / Bounds.Height),
            (float)(frame.Width / Bounds.Width),
            (float)(frame.Height / Bounds.Height));
    }

    private void OnOverlayInvalidated(object? sender, EventArgs e) => QueueOverlay();

    // Records the overlay once layout settles, so any number of changes make one copy, at the size the view has then.
    private void QueueOverlay()
    {
        if (_overlayQueued || _renderer == null)
            return;

        _overlayQueued = true;
        Dispatcher.UIThread.Post(UpdateOverlay, DispatcherPriority.Loaded);
    }

    private void UpdateOverlay()
    {
        _overlayQueued = false;
        if (_renderer is not { } renderer)
            return;

        if (Overlay is not { } overlay || !(Bounds.Width > 0 && Bounds.Height > 0))
        {
            if (_overlaySent)
                renderer.SetOverlay(null, 0, 0);

            _overlaySent = false;
            return;
        }

        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        renderer.SetOverlay(overlay.Record(Bounds.Size, FrameRegion, scaling), (float)Bounds.Width, (float)Bounds.Height);
        _overlaySent = true;
    }

    // The frame's region in device pixels, which a Windows or X11 window takes as its shape, or null for no shape.
    private PixelRect? GetFrameShape()
    {
        if (FrameRect == null)
            return null;

        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        Rect region = FrameRegion;
        int left = (int)Math.Round(region.X * scaling);
        int top = (int)Math.Round(region.Y * scaling);
        int right = (int)Math.Round(region.Right * scaling);
        int bottom = (int)Math.Round(region.Bottom * scaling);
        return new PixelRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    // A child window on Windows or X11 cannot be blended with the UI under it, unlike the layer on macOS, so it takes the
    // frame's shape to leave the UI around the frame in sight.
    private void UpdateFrameShape()
    {
        if (_windowsControl == IntPtr.Zero && _x11Window == IntPtr.Zero)
            return;

        PixelRect? shape = GetFrameShape();
        if (_frameShapeKnown && shape == _frameShape)
            return;

        _frameShape = shape;
        _frameShapeKnown = true;
        if (_windowsControl != IntPtr.Zero)
            SetWindowsShape(GetParent(_windowsControl), shape);
        else
            SetX11Shape(_x11Window, shape);
    }

    // Lets the mouse through Avalonia's holder window to the UI, as on macOS, and gives a new holder the frame's shape.
    private void AdoptWindowsHolder(IntPtr holder = default)
    {
        if (_windowsControl == IntPtr.Zero)
            return;

        if (holder == IntPtr.Zero)
            holder = GetParent(_windowsControl);

        if (PassMouseThrough(holder))
            _frameShapeKnown = false;
    }

    // AppKit leaves a layer it did not create at a scale of 1. The swapchain would then have as many pixels as the view
    // has points, and Core Animation would stretch it on a 2x screen.
    private void UpdateContentsScale()
    {
        if (_macOSView != IntPtr.Zero && TopLevel.GetTopLevel(this) is { } topLevel)
            SetMacOSContentsScale(GetMacOSLayer(_macOSView), topLevel.RenderScaling);
    }

    // On macOS, FitToDisplay hands tone mapping to Core Animation, which fits the frame into the screen's current
    // headroom the way QuickTime plays HDR10 and follows it as brightness changes. The present shader then passes
    // values through.
    private void UpdateEdrMetadata()
    {
        if (_macOSView == IntPtr.Zero)
            return;

        SetMacOSEdrMetadata(GetMacOSLayer(_macOSView), ToneMapping == UIToneMappingOperator.FitToDisplay);
    }

    // Follows the gamut FitToDisplay maps colors into on an HDR swapchain to the screen the view is on, and returns
    // whether it changed. Core Animation applies the EDR metadata only after clipping the layer to its color space,
    // which holds every screen's colors when the swapchain is in BT.2020; with an extended linear sRGB one, colors have
    // to fit sRGB as well as the screen. On Windows the renderer reads the display's gamut itself, and elsewhere it is
    // not known.
    private bool UpdateDisplayGamut()
    {
        if (_macOSView == IntPtr.Zero)
            return false;

        // The cached NSColorSpace stays retained, so a new one cannot reuse its address.
        IntPtr colorSpace = GetMacOSScreenColorSpace(_macOSView);
        if (colorSpace == IntPtr.Zero || colorSpace == _macOSScreenColorSpace)
            return false;

        ReleaseMacOSObject(_macOSScreenColorSpace);
        _macOSScreenColorSpace = RetainMacOSObject(colorSpace);

        PreviewGamutMapping gamut = GetMacOSPrimaries(colorSpace) is { } primaries
            ? PreviewGamutMapping.For(primaries, withinSrgb: _renderer?.IsBt2020Active != true)
            : PreviewGamutMapping.None;
        if (!gamut.IsEnabled)
            gamut = PreviewGamutMapping.Srgb;

        bool changed = gamut != _displayGamut;
        _displayGamut = gamut;
        return changed;
    }

    private Size? GetSourceSize()
    {
        var source = _clonedSource;
        return source?.Value is { IsDisposed: false, Width: var width, Height: var height }
            ? new Size(width, height)
            : null;
    }
}
