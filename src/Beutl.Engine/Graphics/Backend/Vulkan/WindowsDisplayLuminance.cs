using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Beutl.Graphics.Backend.Vulkan;

/// <summary>
/// Reads the peak luminance and SDR white level of the display a window is on, from DXGI and the display
/// configuration, for an scRGB swapchain to match.
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
    private const int DisplayConfigDeviceInfoGetSdrWhiteLevel = 11;

    private nint _factory;
    private nint _monitor;

    /// <summary>The display's luminance as of the last <see cref="Refresh"/>.</summary>
    public PresentLuminance Current { get; private set; } = PresentLuminance.Unknown;

    /// <summary>Reads the display again if the window moved to another one or the displays changed.</summary>
    /// <returns>Whether <see cref="Current"/> changed.</returns>
    public bool Refresh()
    {
        nint monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        bool stale = _factory != 0 && !IsCurrent(_factory);
        // A factory DXGI failed to create is tried again on the next monitor, not on every frame.
        if (monitor == _monitor && !stale)
            return false;

        _monitor = monitor;
        if (_factory == 0 || stale)
        {
            ReleaseFactory();
            Guid iid = s_iidFactory1;
            nint factory;
            _factory = CreateDXGIFactory1(&iid, &factory) >= 0 ? factory : 0;
        }

        PresentLuminance luminance = _factory != 0 && TryGetOutputDesc(monitor, out DxgiOutputDesc1 desc)
            ? Read(desc)
            : PresentLuminance.Unknown;
        bool changed = luminance != Current;
        Current = luminance;
        return changed;
    }

    public void Dispose()
    {
        ReleaseFactory();
    }

    // DXGI describes a display in HDR mode as HDR10, and any other, wide-gamut SDR included, as sRGB.
    private static PresentLuminance Read(DxgiOutputDesc1 desc)
    {
        if (desc.ColorSpace != DxgiColorSpaceRgbFullG2084NoneP2020)
            return PresentLuminance.Sdr;

        float sdrWhiteNits = GetSdrWhiteLevelNits(TrimNull(new ReadOnlySpan<char>(desc.DeviceName, 32)));
        return PresentLuminance.ForScRgbHdr(sdrWhiteNits, desc.MaxLuminance);
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
        DisplayConfigPathInfo[] paths;
        uint pathCount, modeCount;
        int result;
        do
        {
            if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, &pathCount, &modeCount) != 0)
                return 0;

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
            return 0;

        for (uint i = 0; i < pathCount; i++)
        {
            DisplayConfigPathInfo path = paths[i];
            var source = new DisplayConfigSourceDeviceName
            {
                Header = new DisplayConfigDeviceInfoHeader(
                    DisplayConfigDeviceInfoGetSourceName, sizeof(DisplayConfigSourceDeviceName),
                    path.SourceAdapterId, path.SourceId)
            };
            if (DisplayConfigGetDeviceInfo(&source.Header) != 0
                || !TrimNull(new ReadOnlySpan<char>(source.ViewGdiDeviceName, 32)).SequenceEqual(deviceName))
            {
                continue;
            }

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

        return 0;
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
}
