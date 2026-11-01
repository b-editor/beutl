using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
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

    private VulkanSwapchainRenderer? _renderer;
    private Ref<BtlBitmap>? _clonedSource;
    private Size? _lastSourceSize;
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
        if (_renderer != null && Bounds.Width > 0 && Bounds.Height > 0)
        {
            var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
            _renderer.Resize(
                (uint)(Bounds.Width * scaling),
                (uint)(Bounds.Height * scaling));

            RequestRender();
        }
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
                 change.Property == StretchProperty ||
                 change.Property == InterpolationModeProperty)
        {
            RequestRender();
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);

        if (_renderer != null && e.NewSize.Width > 0 && e.NewSize.Height > 0)
        {
            var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
            _renderer.Resize(
                (uint)(e.NewSize.Width * scaling),
                (uint)(e.NewSize.Height * scaling));

            RequestRender();
        }
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
                ReleaseMacOSView(handle.Handle);

            throw;
        }

        if (OperatingSystem.IsMacOS())
        {
            _macOSView = handle.Handle;
            _displayGamut = PreviewGamutMapping.Srgb;
            UpdateEdrMetadata();
            UpdateDisplayGamut();
        }

        return handle;
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        _macOSView = IntPtr.Zero;
        ReleaseMacOSObject(_macOSScreenColorSpace);
        _macOSScreenColorSpace = IntPtr.Zero;
        _displayGamut = PreviewGamutMapping.None;

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
            ReleaseMacOSView(control.Handle);
        }
    }

    private void RequestRender()
    {
        if (_renderer == null || _clonedSource == null)
            return;

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
            DisplayGamut: _displayGamut);

        _renderer.RequestRender(bitmapRef, renderParams);
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
    // whether it changed. Core Animation applies the EDR metadata only after clipping the layer to its extended linear
    // sRGB, so on macOS colors have to fit sRGB as well as the screen. Elsewhere the display's gamut is not known.
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
            ? PreviewGamutMapping.For(primaries, withinSrgb: true)
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
