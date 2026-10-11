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
    /// Gets a value indicating whether this device is running on MoltenVK, the Vulkan driver every Mac GPU runs on,
    /// Apple silicon, Intel and AMD alike.
    /// </summary>
    /// <remarks>
    /// The engine reads this from the Vulkan driver ID rather than from the device name. An instance created outside
    /// the engine reports <see langword="false"/> unless its creator sets it.
    /// </remarks>
    public bool IsMoltenVK { get; init; }
}
