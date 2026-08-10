namespace Beutl.Benchmarks.Rendering;

/// <summary>
/// The one place that decides which evidence-fingerprint fields may be blank, shared by the capture-time
/// gate and the archived-counter gate so a run cannot be accepted by one and rejected by the other.
/// </summary>
internal static class EvidenceFingerprintRules
{
    private const string CpuDeviceType = "Cpu";

    private const string DriverInfoField = "VulkanDriverInfo";

    private const string DeviceTypeField = "VulkanDeviceType";

    public static bool IsDeviceTypeField(string fieldName)
    {
        ArgumentNullException.ThrowIfNull(fieldName);
        return string.Equals(fieldName, DeviceTypeField, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsCpuDevice(string? deviceType)
        => string.Equals(deviceType, CpuDeviceType, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a blank <paramref name="fieldName"/> is expected rather than a broken capture.
    /// </summary>
    /// <remarks>
    /// The gate exists to stop a hardware driver from hiding behind a blank identity. A software
    /// rasterizer has no driver build to report and leaves VK_KHR_driver_properties' driverInfo empty,
    /// which is the device identifying itself honestly, so tolerate that one field on a CPU device only.
    /// A literal "unknown" is still a broken capture everywhere and stays rejected.
    /// </remarks>
    public static bool AllowsBlankValue(string fieldName, string? deviceType)
    {
        ArgumentNullException.ThrowIfNull(fieldName);
        return IsCpuDevice(deviceType)
               && string.Equals(fieldName, DriverInfoField, StringComparison.OrdinalIgnoreCase);
    }
}
