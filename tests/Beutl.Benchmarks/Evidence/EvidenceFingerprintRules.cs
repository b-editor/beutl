namespace Beutl.Evidence;

/// <summary>
/// The one place that decides which evidence-fingerprint fields may be blank, shared by the capture-time
/// gate and by any archived-fingerprint gate, so a run cannot be accepted by one and rejected by the other.
/// </summary>
public static class EvidenceFingerprintRules
{
    private const string CpuDeviceType = "Cpu";

    private const string DriverInfoField = "VulkanDriverInfo";

    public static bool IsCpuDevice(string? deviceType)
        => string.Equals(deviceType, CpuDeviceType, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a blank field is valid for the reported device type.</summary>
    /// <remarks>
    /// Only a CPU device may omit Vulkan driver info. A literal "unknown" remains invalid.
    /// </remarks>
    public static bool AllowsBlankValue(string fieldName, string? deviceType)
    {
        ArgumentNullException.ThrowIfNull(fieldName);
        return IsCpuDevice(deviceType)
               && string.Equals(fieldName, DriverInfoField, StringComparison.OrdinalIgnoreCase);
    }
}
