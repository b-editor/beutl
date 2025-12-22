namespace Beutl.Graphics.Backend;

public record GpuDeviceInfo(string Name, string DeviceType);

public record GpuMemoryInfo(ulong DeviceLocalMemory, ulong HostVisibleMemory);

public record GpuInfo(
    IReadOnlyList<GpuDeviceInfo> AvailableGpus,
    GpuDeviceInfo? SelectedGpu,
    IReadOnlyList<string> EnabledExtensions,
    string ApiVersion,
    GpuMemoryInfo? Memory);
