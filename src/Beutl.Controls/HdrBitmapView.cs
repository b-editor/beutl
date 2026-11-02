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

    private VulkanSwapchainRenderer? _renderer;
    private Ref<BtlBitmap>? _clonedSource;
    private Size? _lastSourceSize;
    private bool _drawAtNewSizeQueued;
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

            var oldSize = _lastSourceSize;
            _lastSourceSize = GetSourceSize();
            if (oldSize != _lastSourceSize)
                InvalidateMeasure();

            RequestRender();
        }
        else if (change.Property == ToneMappingProperty)
        {
            UpdateEdrMetadata();
            RequestRender();
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

        ResizeRendererAndDraw(_renderer);
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
        if (_lastSourceSize == null)
            return default;

        return Stretch.CalculateSize(availableSize, _lastSourceSize.Value);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
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
        }
        else if (OperatingSystem.IsMacOS())
        {
            handle = CreateMacOSControl(parent);
        }
        else if (OperatingSystem.IsLinux())
        {
            handle = CreateLinuxControl(parent);
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

        return handle;
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        _macOSView = IntPtr.Zero;
        ReleaseMacOSObject(_macOSScreenColorSpace);
        _macOSScreenColorSpace = IntPtr.Zero;

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

        bool isLinear = bitmap.ColorSpace?.GammaIsLinear == true;
        UpdateDisplayGamut();

        var renderParams = new RenderParams(
            SourceWidth: bitmap.Width,
            SourceHeight: bitmap.Height,
            Stretch: (BtlStretch)(int)Stretch,
            ToneMapping: ToneMapping,
            Exposure: ToneMappingExposure,
            IsSourceLinear: isLinear,
            DisplayGamut: _displayGamut,
            DisplayPeakLuminance: DisplayPeakLuminance);

        _renderer.RequestRender(bitmapRef, renderParams);
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
