#nullable enable

using System.Runtime.InteropServices;
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

namespace Beutl.Controls;

/// <summary>
/// A NativeControlHost-based control that renders Beutl.Media.Bitmap directly via Vulkan,
/// bypassing Avalonia's Bgra8888 swapchain to enable HDR display.
/// </summary>
public class HdrBitmapView : NativeControlHost
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

    public static readonly DirectProperty<HdrBitmapView, bool> IsHdrActiveProperty =
        AvaloniaProperty.RegisterDirect<HdrBitmapView, bool>(nameof(IsHdrActive), o => o.IsHdrActive);

    private VulkanSwapchainRenderer? _renderer;
    private Ref<BtlBitmap>? _clonedSource;
    private Size? _lastSourceSize;
    private bool _isHdrActive;

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

    public bool IsHdrActive
    {
        get => _isHdrActive;
        private set => SetAndRaise(IsHdrActiveProperty, ref _isHdrActive, value);
    }

    protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        if (e.Root is TopLevel topLevel)
        {
            topLevel.ScalingChanged += OnTopLevelScalingChanged;
        }
    }

    protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromLogicalTree(e);
        if (e.Root is TopLevel topLevel)
        {
            topLevel.ScalingChanged -= OnTopLevelScalingChanged;
        }
    }

    private void OnTopLevelScalingChanged(object? sender, EventArgs e)
    {
        if (_renderer != null && Bounds.Width > 0 && Bounds.Height > 0)
        {
            var scaling = VisualRoot?.RenderScaling ?? 1.0;
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
            _clonedSource?.Dispose();
            _clonedSource = null;

            if (Source != null)
                _clonedSource = Source.TryClone();

            var oldSize = _lastSourceSize;
            _lastSourceSize = GetSourceSize();
            if (oldSize != _lastSourceSize)
                InvalidateMeasure();

            RequestRender();
        }
        else if (change.Property == ToneMappingProperty ||
                 change.Property == ToneMappingExposureProperty ||
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
            var scaling = VisualRoot?.RenderScaling ?? 1.0;
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

            var scaling = VisualRoot?.RenderScaling ?? 1.0;
            _renderer.Initialize(
                handle.Handle,
                handle.HandleDescriptor ?? "",
                (uint)Math.Max(1, Bounds.Width * scaling),
                (uint)Math.Max(1, Bounds.Height * scaling));

            IsHdrActive = _renderer.IsHdrActive;
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

        return handle;
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
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

        var renderParams = new RenderParams(
            SourceWidth: bitmap.Width,
            SourceHeight: bitmap.Height,
            DestWidth: (float)Bounds.Width,
            DestHeight: (float)Bounds.Height,
            Stretch: (BtlStretch)(int)Stretch,
            ToneMapping: ToneMapping,
            Exposure: ToneMappingExposure,
            IsSourceLinear: isLinear);

        _renderer.RequestRender(bitmapRef, renderParams);
    }

    private Size? GetSourceSize()
    {
        var source = Source;
        return source?.Value is { IsDisposed: false, Width: var width, Height: var height }
            ? new Size(width, height)
            : null;
    }

    #region Platform-specific native window creation

    // Windows
    private static IPlatformHandle CreateWindowsControl(IPlatformHandle parent)
    {
        var hwnd = CreateWindowExW(
            0x00000000, // dwExStyle
            "Static", // lpClassName
            "", // lpWindowName
            0x40000000 | 0x10000000, // WS_CHILD | WS_VISIBLE
            0, 0, 1, 1,
            parent.Handle,
            IntPtr.Zero,
            GetModuleHandleW(null),
            IntPtr.Zero);

        if (hwnd == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create child HWND");

        return new PlatformHandle(hwnd, "HWND");
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    // macOS
    private static IPlatformHandle CreateMacOSControl(IPlatformHandle parent)
    {
        // [[NSView alloc] initWithFrame:NSMakeRect(0, 0, 1, 1)]
        var nsViewClass = objc_getClass("NSView");
        var allocSel = sel_getUid("alloc");
        var initWithFrameSel = sel_getUid("initWithFrame:");

        var nsView = objc_msgSend_IntPtr(nsViewClass, allocSel);
        nsView = objc_msgSend_NSRect(nsView, initWithFrameSel, 0, 0, 1, 1);

        if (nsView == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create NSView");

        return new PlatformHandle(nsView, "NSView");
    }

    private static void ReleaseMacOSView(IntPtr nsView)
    {
        if (nsView != IntPtr.Zero)
        {
            var releaseSel = sel_getUid("release");
            objc_msgSend_void(nsView, releaseSel);
        }
    }

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_getClass(string className);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr sel_getUid(string selector);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_void(IntPtr receiver, IntPtr selector);

    // initWithFrame: takes an NSRect (CGRect) which is { x, y, width, height } as doubles
    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_NSRect(
        IntPtr receiver, IntPtr selector,
        double x, double y, double width, double height);

    // Linux
    private static IPlatformHandle CreateLinuxControl(IPlatformHandle parent)
    {
        // For X11, we return the parent handle and let the surface helper handle it
        // In a full implementation, we'd create a child X window
        return parent;
    }

    #endregion

    private sealed class PlatformHandle(IntPtr handle, string descriptor) : IPlatformHandle
    {
        public IntPtr Handle { get; } = handle;
        public string HandleDescriptor { get; } = descriptor;
    }
}
