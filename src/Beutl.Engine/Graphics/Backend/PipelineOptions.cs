namespace Beutl.Graphics.Backend;

/// <summary>
/// Options for creating a graphics pipeline.
/// </summary>
public struct PipelineOptions
{
    /// <summary>
    /// Gets or sets whether depth testing is enabled. Default is true.
    /// </summary>
    public bool DepthTestEnabled { get; set; }

    /// <summary>
    /// Gets or sets whether depth writing is enabled. Default is true.
    /// </summary>
    public bool DepthWriteEnabled { get; set; }

    /// <summary>
    /// Gets or sets the cull mode. Default is Back.
    /// </summary>
    public CullMode CullMode { get; set; }

    /// <summary>
    /// Gets the default pipeline options for 3D rendering.
    /// </summary>
    public static PipelineOptions Default => new()
    {
        DepthTestEnabled = true,
        DepthWriteEnabled = true,
        CullMode = CullMode.Back
    };

    /// <summary>
    /// Gets pipeline options for fullscreen/post-processing passes.
    /// </summary>
    public static PipelineOptions Fullscreen => new()
    {
        DepthTestEnabled = false,
        DepthWriteEnabled = false,
        CullMode = CullMode.None
    };
}

/// <summary>
/// Specifies which faces should be culled.
/// </summary>
public enum CullMode
{
    /// <summary>
    /// No culling.
    /// </summary>
    None = 0,

    /// <summary>
    /// Cull front-facing triangles.
    /// </summary>
    Front = 1,

    /// <summary>
    /// Cull back-facing triangles.
    /// </summary>
    Back = 2
}
