namespace Beutl.Graphics.Backend;

/// <summary>
/// Provides information about a graphics device.
/// </summary>
/// <param name="Name">The name of the graphics device.</param>
/// <param name="DeviceType">The type of the graphics device.</param>
/// <param name="ApiVersion">The supported API version string.</param>
/// <param name="TotalMemoryMB">The total device memory in megabytes.</param>
public record GraphicsDeviceInfo(
    string Name,
    GraphicsDeviceType DeviceType,
    string ApiVersion,
    ulong TotalMemoryMB)
{
    /// <summary>
    /// Gets a value indicating whether this device is running on MoltenVK (Apple Silicon).
    /// </summary>
    public bool IsMoltenVK => Name.Contains("Apple");
}
