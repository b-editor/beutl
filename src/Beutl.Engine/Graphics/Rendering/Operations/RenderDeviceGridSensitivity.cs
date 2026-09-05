namespace Beutl.Graphics.Rendering;

/// <summary>
/// Declares whether an opaque description's pixels depend on where the composition-device pixel grid falls.
/// </summary>
/// <remarks>
/// The renderer reuses a cached output only when every value that shaped its pixels is part of the cache
/// identity. Device-grid phase — the sub-pixel offset between the description's own coordinate space and the
/// pixel centres it writes to — is one such value, and no bounds, density, or author-supplied field carries it.
/// </remarks>
public enum RenderDeviceGridSensitivity : byte
{
    /// <summary>
    /// The output is unchanged by a sub-pixel shift of the device grid, so it may be cached and reused across
    /// device-grid phase changes and across a remapping replay.
    /// </summary>
    Insensitive,

    /// <summary>
    /// The output is a function of the device-grid phase, so a sub-pixel phase change or a remapping replay
    /// ancestor produces different pixels than the cached output.
    /// </summary>
    /// <remarks>
    /// Declare this for anything computed from where the pixel centres fall rather than resampled from a
    /// stored raster. Analytic anti-aliased coverage — glyph rasterization, signed-distance-field text — is
    /// one such source, and so are screen-space dithering, ordered noise, and pixel-grid overlays, which
    /// compute no coverage at all yet still change with the phase.
    /// </remarks>
    PhaseDependent,
}
