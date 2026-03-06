namespace Beutl.Graphics.Backend;

/// <summary>
/// Specifies the type of graphics device.
/// </summary>
public enum GraphicsDeviceType
{
    /// <summary>
    /// The device type is unknown or other.
    /// </summary>
    Other = 0,

    /// <summary>
    /// The device is an integrated GPU.
    /// </summary>
    Integrated = 1,

    /// <summary>
    /// The device is a discrete GPU.
    /// </summary>
    Discrete = 2,

    /// <summary>
    /// The device is a virtual GPU.
    /// </summary>
    Virtual = 3,

    /// <summary>
    /// The device is a CPU-based renderer.
    /// </summary>
    Cpu = 4,
}
