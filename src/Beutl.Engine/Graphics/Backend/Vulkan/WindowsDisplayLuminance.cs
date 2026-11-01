using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Beutl.Media;

namespace Beutl.Graphics.Backend.Vulkan;

/// <summary>
/// Reads the peak luminance, SDR white level, primaries and color mode of the display a window is on, from DXGI and the
/// display configuration, for an scRGB swapchain to match.
/// </summary>
/// <remarks>
/// Windows sends no message when a display's HDR settings change, and the window's moves reach Avalonia, not the
/// renderer. <see cref="Refresh"/> compares the window's monitor and asks DXGI whether its view of the displays is
/// current, which turns false when HDR is switched on or off or the SDR white level changes, so it is cheap enough to
/// call every frame.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed unsafe partial class WindowsDisplayLuminance(nint hwnd) : IDisposable
{
    private static readonly Guid s_iidFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");
    private static readonly Guid s_iidOutput6 = new("068346e8-aaec-4b84-add7-137f513f77a1");

    // Vtable slots, IUnknown's three methods included.
    private const int FactoryEnumAdapters1 = 12;
    private const int FactoryIsCurrent = 13;
    private const int AdapterEnumOutputs = 7;
    private const int Output6GetDesc1 = 27;

    private const uint MonitorDefaultToNearest = 2;
    private const int DxgiColorSpaceRgbFullG2084NoneP2020 = 12;
    private const uint QdcOnlyActivePaths = 2;
    private const int ErrorInsufficientBuffer = 122;
    private const int DisplayConfigDeviceInfoGetSourceName = 1;
    private const int DisplayConfigDeviceInfoGetAdvancedColorInfo = 9;
    private const int DisplayConfigDeviceInfoGetSdrWhiteLevel = 11;
    private const int DisplayConfigDeviceInfoGetAdvancedColorInfo2 = 15;

    private nint _factory;
    private nint _monitor;
    private float _peakLuminanceOverride;

    /// <summary>The display's luminance as of the last <see cref="Refresh"/>.</summary>
    public PresentLuminance Current { get; private set; } = PresentLuminance.Unknown;

    /// <summary>The gamut FitToDisplay fits colors into, as of the last <see cref="Refresh"/>.</summary>
    public PreviewGamutMapping Gamut { get; private set; }

    /// <summary>
    /// What the display reported at the last <see cref="Refresh"/>, or null when it is not in HDR mode or could not be
    /// read.
    /// </summary>
    public HdrDisplayReport? Report { get; private set; }

    /// <summary>
    /// Reads the display again if the window moved to another one, the displays changed or the override did.
    /// </summary>
    /// <param name="peakLuminanceOverride">The peak in nit to roll highlights off into in place of the one an HDR
    /// display reports, or 0 to use that.</param>
    /// <returns>Whether <see cref="Current"/>, <see cref="Gamut"/> or <see cref="Report"/> changed.</returns>
    public bool Refresh(float peakLuminanceOverride = 0f)
    {
        if (!(peakLuminanceOverride > 0f))
            peakLuminanceOverride = 0f;

        nint monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        bool stale = _factory != 0 && !IsCurrent(_factory);
        // A factory DXGI failed to create is tried again on the next monitor, not on every frame.
        if (monitor == _monitor && !stale && peakLuminanceOverride == _peakLuminanceOverride)
            return false;

        _monitor = monitor;
        _peakLuminanceOverride = peakLuminanceOverride;
        if (_factory == 0 || stale)
        {
            ReleaseFactory();
            Guid iid = s_iidFactory1;
            nint factory;
            _factory = CreateDXGIFactory1(&iid, &factory) >= 0 ? factory : 0;
        }

        (PresentLuminance luminance, PreviewGamutMapping gamut, HdrDisplayReport? report) =
            _factory != 0 && TryGetOutputDesc(monitor, out DxgiOutputDesc1 desc)
                ? Read(desc, peakLuminanceOverride)
                : (PresentLuminance.Unknown, PreviewGamutMapping.None, null);
        bool changed = luminance != Current || gamut != Gamut || report != Report;
        Current = luminance;
        Gamut = gamut;
        Report = report;
        return changed;
    }

    public void Dispose()
    {
        ReleaseFactory();
    }

    // DXGI describes a display in HDR mode as HDR10, and any other, wide-gamut SDR included, as sRGB. In HDR mode
    // Windows passes colors outside sRGB on for the display to clip at its primaries, so they are fitted to those
    // first. In SDR mode DXGI does not tell whether Windows manages colors into the primaries or clips them to sRGB, so
    // the display configuration is asked.
    private static (PresentLuminance Luminance, PreviewGamutMapping Gamut, HdrDisplayReport? Report) Read(
        DxgiOutputDesc1 desc, float peakLuminanceOverride)
    {
        ReadOnlySpan<char> deviceName = TrimNull(new ReadOnlySpan<char>(desc.DeviceName, 32));
        float* xy = desc.Primaries;
        var primaries = new DisplayPrimaries(xy[0], xy[1], xy[2], xy[3], xy[4], xy[5]);
        if (desc.ColorSpace != DxgiColorSpaceRgbFullG2084NoneP2020)
            return (PresentLuminance.Sdr, GamutInSdrMode(GetColorMode(deviceName), primaries), null);

        var report = new HdrDisplayReport(GetSdrWhiteLevelNits(deviceName), desc.MaxLuminance);
        return (PresentLuminance.ForScRgbHdr(report, peakLuminanceOverride),
            PreviewGamutMapping.For(primaries, withinSrgb: false), report);
    }

    // In SDR mode Windows clips colors to sRGB, unless auto color management has the display in WCG mode, where Windows
    // converts them to the display's primaries and clips them there. A mode that cannot be read, or HDR while DXGI
    // still describes the display as SDR, leaves them to Windows.
    internal static PreviewGamutMapping GamutInSdrMode(DisplayConfigAdvancedColorMode? mode, DisplayPrimaries primaries)
    {
        return mode switch
        {
            DisplayConfigAdvancedColorMode.Sdr => PreviewGamutMapping.Srgb,
            DisplayConfigAdvancedColorMode.Wcg => PreviewGamutMapping.For(primaries, withinSrgb: false),
            _ => PreviewGamutMapping.None
        };
    }

    // The output that shows the monitor, on whichever adapter drives it.
    private bool TryGetOutputDesc(nint monitor, out DxgiOutputDesc1 desc)
    {
        nint adapter;
        for (uint i = 0; EnumAdapters1(_factory, i, &adapter) >= 0; i++)
        {
            try
            {
                nint output;
                for (uint j = 0; EnumOutputs(adapter, j, &output) >= 0; j++)
                {
                    try
                    {
                        // IDXGIOutput6 is missing before Windows 10 version 1803.
                        if (Marshal.QueryInterface(output, in s_iidOutput6, out nint output6) < 0)
                            continue;

                        try
                        {
                            DxgiOutputDesc1 candidate;
                            if (GetDesc1(output6, &candidate) >= 0 && candidate.Monitor == monitor)
                            {
                                desc = candidate;
                                return true;
                            }
                        }
                        finally
                        {
                            Marshal.Release(output6);
                        }
                    }
                    finally
                    {
                        Marshal.Release(output);
                    }
                }
            }
            finally
            {
                Marshal.Release(adapter);
            }
        }

        desc = default;
        return false;
    }

    // The SDR white level of the display named deviceName (\\.\DISPLAY1), or 0 when it cannot be read.
    private static float GetSdrWhiteLevelNits(ReadOnlySpan<char> deviceName)
    {
        if (!TryGetPath(deviceName, out DisplayConfigPathInfo path))
            return 0;

        var white = new DisplayConfigSdrWhiteLevel
        {
            Header = new DisplayConfigDeviceInfoHeader(
                DisplayConfigDeviceInfoGetSdrWhiteLevel, sizeof(DisplayConfigSdrWhiteLevel),
                path.TargetAdapterId, path.TargetId)
        };
        // SDRWhiteLevel counts in thousandths of 80 nit.
        return DisplayConfigGetDeviceInfo(&white.Header) == 0
            ? white.SdrWhiteLevel / 1000f * PresentLuminance.ScRgbWhiteNits
            : 0;
    }

    // The color mode of the display named deviceName, or null when it cannot be read. Windows 11 24H2 and later name
    // it; earlier versions only flag it in the older query.
    private static DisplayConfigAdvancedColorMode? GetColorMode(ReadOnlySpan<char> deviceName)
    {
        if (!TryGetPath(deviceName, out DisplayConfigPathInfo path))
            return null;

        var info2 = new DisplayConfigGetAdvancedColorInfo2
        {
            Header = new DisplayConfigDeviceInfoHeader(
                DisplayConfigDeviceInfoGetAdvancedColorInfo2, sizeof(DisplayConfigGetAdvancedColorInfo2),
                path.TargetAdapterId, path.TargetId)
        };
        if (DisplayConfigGetDeviceInfo(&info2.Header) == 0)
            return info2.ActiveColorMode;

        var info = new DisplayConfigGetAdvancedColorInfo
        {
            Header = new DisplayConfigDeviceInfoHeader(
                DisplayConfigDeviceInfoGetAdvancedColorInfo, sizeof(DisplayConfigGetAdvancedColorInfo),
                path.TargetAdapterId, path.TargetId)
        };
        return DisplayConfigGetDeviceInfo(&info.Header) == 0 ? info.ColorMode : null;
    }

    // The active path that shows the display named deviceName.
    private static bool TryGetPath(ReadOnlySpan<char> deviceName, out DisplayConfigPathInfo path)
    {
        path = default;
        DisplayConfigPathInfo[] paths;
        uint pathCount, modeCount;
        int result;
        do
        {
            if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, &pathCount, &modeCount) != 0)
                return false;

            paths = new DisplayConfigPathInfo[pathCount];
            // Nothing here reads the modes, but QueryDisplayConfig fills them.
            var modes = new DisplayConfigModeInfo[modeCount];
            fixed (DisplayConfigPathInfo* pPaths = paths)
            fixed (DisplayConfigModeInfo* pModes = modes)
            {
                result = QueryDisplayConfig(QdcOnlyActivePaths, &pathCount, pPaths, &modeCount, pModes, null);
            }
        }
        // The configuration changed between the two calls.
        while (result == ErrorInsufficientBuffer);

        if (result != 0)
            return false;

        for (uint i = 0; i < pathCount; i++)
        {
            var source = new DisplayConfigSourceDeviceName
            {
                Header = new DisplayConfigDeviceInfoHeader(
                    DisplayConfigDeviceInfoGetSourceName, sizeof(DisplayConfigSourceDeviceName),
                    paths[i].SourceAdapterId, paths[i].SourceId)
            };
            if (DisplayConfigGetDeviceInfo(&source.Header) == 0
                && TrimNull(new ReadOnlySpan<char>(source.ViewGdiDeviceName, 32)).SequenceEqual(deviceName))
            {
                path = paths[i];
                return true;
            }
        }

        return false;
    }

    private static ReadOnlySpan<char> TrimNull(ReadOnlySpan<char> name)
    {
        int end = name.IndexOf('\0');
        return end < 0 ? name : name[..end];
    }

    private void ReleaseFactory()
    {
        if (_factory != 0)
        {
            Marshal.Release(_factory);
            _factory = 0;
        }
    }

    private static nint Method(nint instance, int slot) => (*(nint**)instance)[slot];

    private static int EnumAdapters1(nint factory, uint index, nint* adapter)
    {
        return ((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Method(factory, FactoryEnumAdapters1))(
            factory, index, adapter);
    }

    private static bool IsCurrent(nint factory)
    {
        return ((delegate* unmanaged[Stdcall]<nint, int>)Method(factory, FactoryIsCurrent))(factory) != 0;
    }

    private static int EnumOutputs(nint adapter, uint index, nint* output)
    {
        return ((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Method(adapter, AdapterEnumOutputs))(
            adapter, index, output);
    }

    private static int GetDesc1(nint output6, DxgiOutputDesc1* desc)
    {
        return ((delegate* unmanaged[Stdcall]<nint, DxgiOutputDesc1*, int>)Method(output6, Output6GetDesc1))(
            output6, desc);
    }

    [LibraryImport("dxgi.dll")]
    private static partial int CreateDXGIFactory1(Guid* riid, nint* factory);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromWindow(nint hwnd, uint flags);

    [LibraryImport("user32.dll")]
    private static partial int GetDisplayConfigBufferSizes(uint flags, uint* pathCount, uint* modeCount);

    [LibraryImport("user32.dll")]
    private static partial int QueryDisplayConfig(
        uint flags, uint* pathCount, DisplayConfigPathInfo* paths, uint* modeCount, DisplayConfigModeInfo* modes,
        void* currentTopologyId);

    [LibraryImport("user32.dll")]
    private static partial int DisplayConfigGetDeviceInfo(DisplayConfigDeviceInfoHeader* request);

    // DXGI_OUTPUT_DESC1
    [StructLayout(LayoutKind.Sequential)]
    internal struct DxgiOutputDesc1
    {
        public fixed char DeviceName[32];
        public int DesktopLeft;
        public int DesktopTop;
        public int DesktopRight;
        public int DesktopBottom;
        public int AttachedToDesktop;
        public int Rotation;
        public nint Monitor;
        public uint BitsPerColor;
        public int ColorSpace;
        // Red, green, blue and white, x then y.
        public fixed float Primaries[8];
        public float MinLuminance;
        public float MaxLuminance;
        public float MaxFullFrameLuminance;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    // DISPLAYCONFIG_PATH_INFO, with its source and target infos inlined.
    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigPathInfo
    {
        public Luid SourceAdapterId;
        public uint SourceId;
        public uint SourceModeInfoIdx;
        public uint SourceStatusFlags;
        public Luid TargetAdapterId;
        public uint TargetId;
        public uint TargetModeInfoIdx;
        public int OutputTechnology;
        public int Rotation;
        public int Scaling;
        public uint RefreshRateNumerator;
        public uint RefreshRateDenominator;
        public int ScanLineOrdering;
        public int TargetAvailable;
        public uint TargetStatusFlags;
        public uint Flags;
    }

    // DISPLAYCONFIG_MODE_INFO, whose union of mode infos nothing here reads.
    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigModeInfo
    {
        public int InfoType;
        public uint Id;
        public Luid AdapterId;
        public fixed ulong Mode[6];
    }

    // DISPLAYCONFIG_DEVICE_INFO_HEADER
    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigDeviceInfoHeader(int type, int size, Luid adapterId, uint id)
    {
        public int Type = type;
        public int Size = size;
        public Luid AdapterId = adapterId;
        public uint Id = id;
    }

    // DISPLAYCONFIG_SOURCE_DEVICE_NAME
    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigSourceDeviceName
    {
        public DisplayConfigDeviceInfoHeader Header;
        public fixed char ViewGdiDeviceName[32];
    }

    // DISPLAYCONFIG_SDR_WHITE_LEVEL
    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigSdrWhiteLevel
    {
        public DisplayConfigDeviceInfoHeader Header;
        public uint SdrWhiteLevel;
    }

    // DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigGetAdvancedColorInfo
    {
        public DisplayConfigDeviceInfoHeader Header;
        // advancedColorSupported, advancedColorEnabled, wideColorEnforced and advancedColorForceDisabled, from bit 0.
        public uint Value;
        public int ColorEncoding;
        public uint BitsPerColorChannel;

        // Windows 11 22H2 and 23H2 enforce wide color on the SDR displays auto color management can take on, so enabled
        // advanced color is WCG there and HDR on any other display.
        public readonly DisplayConfigAdvancedColorMode ColorMode =>
            (Value & 0b10) == 0 ? DisplayConfigAdvancedColorMode.Sdr
            : (Value & 0b100) != 0 ? DisplayConfigAdvancedColorMode.Wcg
            : DisplayConfigAdvancedColorMode.Hdr;
    }

    // DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2, from Windows 11 24H2
    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigGetAdvancedColorInfo2
    {
        public DisplayConfigDeviceInfoHeader Header;
        // What the display supports and the user enabled, which the active mode already settles.
        public uint Value;
        public int ColorEncoding;
        public uint BitsPerColorChannel;
        public DisplayConfigAdvancedColorMode ActiveColorMode;
    }

    // DISPLAYCONFIG_ADVANCED_COLOR_MODE
    internal enum DisplayConfigAdvancedColorMode
    {
        Sdr,
        Wcg,
        Hdr
    }
}
