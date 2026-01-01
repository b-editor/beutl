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
    /// Gets or sets the front face winding order. Default is CounterClockwise.
    /// </summary>
    public FrontFace FrontFace { get; set; }

    /// <summary>
    /// Gets the default pipeline options for 3D rendering.
    /// </summary>
    public static PipelineOptions Default => new()
    {
        DepthTestEnabled = true,
        DepthWriteEnabled = true,
        CullMode = CullMode.Back,
        FrontFace = FrontFace.CounterClockwise
    };

    /// <summary>
    /// Gets pipeline options for fullscreen/post-processing passes.
    /// </summary>
    public static PipelineOptions Fullscreen => new()
    {
        DepthTestEnabled = false,
        DepthWriteEnabled = false,
        CullMode = CullMode.None,
        FrontFace = FrontFace.CounterClockwise
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

/// <summary>
/// Specifies the winding order for front-facing triangles.
/// </summary>
public enum FrontFace
{
    /// <summary>
    /// Triangles with counter-clockwise winding are front-facing.
    /// </summary>
    CounterClockwise = 0,

    /// <summary>
    /// Triangles with clockwise winding are front-facing.
    /// </summary>
    Clockwise = 1
}
