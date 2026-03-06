using System;

namespace Beutl.Graphics.Backend;

/// <summary>
/// Specifies the filter mode for texture sampling.
/// </summary>
public enum SamplerFilter
{
    Nearest,
    Linear
}

/// <summary>
/// Specifies how texture coordinates outside [0, 1] are handled.
/// </summary>
public enum SamplerAddressMode
{
    Repeat,
    MirroredRepeat,
    ClampToEdge,
    ClampToBorder
}

/// <summary>
/// Interface for texture sampler abstraction.
/// </summary>
public interface ISampler : IDisposable
{
    /// <summary>
    /// Gets the minification filter.
    /// </summary>
    SamplerFilter MinFilter { get; }

    /// <summary>
    /// Gets the magnification filter.
    /// </summary>
    SamplerFilter MagFilter { get; }

    /// <summary>
    /// Gets the address mode for U coordinate.
    /// </summary>
    SamplerAddressMode AddressModeU { get; }

    /// <summary>
    /// Gets the address mode for V coordinate.
    /// </summary>
    SamplerAddressMode AddressModeV { get; }
}
