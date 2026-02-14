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
    /// Gets or sets whether blending is enabled. Default is false.
    /// </summary>
    public bool BlendEnabled { get; set; }

    /// <summary>
    /// Gets or sets the source color blend factor. Default is One.
    /// </summary>
    public BlendFactor SrcColorBlendFactor { get; set; }

    /// <summary>
    /// Gets or sets the destination color blend factor. Default is Zero.
    /// </summary>
    public BlendFactor DstColorBlendFactor { get; set; }

    /// <summary>
    /// Gets or sets the source alpha blend factor. Default is One.
    /// </summary>
    public BlendFactor SrcAlphaBlendFactor { get; set; }

    /// <summary>
    /// Gets or sets the destination alpha blend factor. Default is Zero.
    /// </summary>
    public BlendFactor DstAlphaBlendFactor { get; set; }

    /// <summary>
    /// Gets or sets the color blend operation. Default is Add.
    /// </summary>
    public BlendOp ColorBlendOp { get; set; }

    /// <summary>
    /// Gets or sets the alpha blend operation. Default is Add.
    /// </summary>
    public BlendOp AlphaBlendOp { get; set; }

    /// <summary>
    /// Gets the default pipeline options for 3D rendering.
    /// </summary>
    public static PipelineOptions Default => new()
    {
        DepthTestEnabled = true,
        DepthWriteEnabled = true,
        CullMode = CullMode.Back,
        FrontFace = FrontFace.CounterClockwise,
        BlendEnabled = false,
        SrcColorBlendFactor = BlendFactor.One,
        DstColorBlendFactor = BlendFactor.Zero,
        SrcAlphaBlendFactor = BlendFactor.One,
        DstAlphaBlendFactor = BlendFactor.Zero,
        ColorBlendOp = BlendOp.Add,
        AlphaBlendOp = BlendOp.Add
    };

    /// <summary>
    /// Gets pipeline options for fullscreen/post-processing passes.
    /// </summary>
    public static PipelineOptions Fullscreen => new()
    {
        DepthTestEnabled = false,
        DepthWriteEnabled = false,
        CullMode = CullMode.None,
        FrontFace = FrontFace.CounterClockwise,
        BlendEnabled = false,
        SrcColorBlendFactor = BlendFactor.One,
        DstColorBlendFactor = BlendFactor.Zero,
        SrcAlphaBlendFactor = BlendFactor.One,
        DstAlphaBlendFactor = BlendFactor.Zero,
        ColorBlendOp = BlendOp.Add,
        AlphaBlendOp = BlendOp.Add
    };

    /// <summary>
    /// Gets pipeline options for transparent object rendering.
    /// Depth test enabled but depth write disabled, standard alpha blending.
    /// </summary>
    public static PipelineOptions Transparent => new()
    {
        DepthTestEnabled = true,
        DepthWriteEnabled = false,
        CullMode = CullMode.Back,
        FrontFace = FrontFace.CounterClockwise,
        BlendEnabled = true,
        SrcColorBlendFactor = BlendFactor.SrcAlpha,
        DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha,
        SrcAlphaBlendFactor = BlendFactor.One,
        DstAlphaBlendFactor = BlendFactor.OneMinusSrcAlpha,
        ColorBlendOp = BlendOp.Add,
        AlphaBlendOp = BlendOp.Add
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

/// <summary>
/// Specifies blend factors for color blending operations.
/// </summary>
public enum BlendFactor
{
    /// <summary>
    /// Factor is (0, 0, 0, 0).
    /// </summary>
    Zero = 0,

    /// <summary>
    /// Factor is (1, 1, 1, 1).
    /// </summary>
    One = 1,

    /// <summary>
    /// Factor is (Rs, Gs, Bs, As) - source color.
    /// </summary>
    SrcColor = 2,

    /// <summary>
    /// Factor is (1-Rs, 1-Gs, 1-Bs, 1-As) - one minus source color.
    /// </summary>
    OneMinusSrcColor = 3,

    /// <summary>
    /// Factor is (Rd, Gd, Bd, Ad) - destination color.
    /// </summary>
    DstColor = 4,

    /// <summary>
    /// Factor is (1-Rd, 1-Gd, 1-Bd, 1-Ad) - one minus destination color.
    /// </summary>
    OneMinusDstColor = 5,

    /// <summary>
    /// Factor is (As, As, As, As) - source alpha.
    /// </summary>
    SrcAlpha = 6,

    /// <summary>
    /// Factor is (1-As, 1-As, 1-As, 1-As) - one minus source alpha.
    /// </summary>
    OneMinusSrcAlpha = 7,

    /// <summary>
    /// Factor is (Ad, Ad, Ad, Ad) - destination alpha.
    /// </summary>
    DstAlpha = 8,

    /// <summary>
    /// Factor is (1-Ad, 1-Ad, 1-Ad, 1-Ad) - one minus destination alpha.
    /// </summary>
    OneMinusDstAlpha = 9
}

/// <summary>
/// Specifies blend operations.
/// </summary>
public enum BlendOp
{
    /// <summary>
    /// Result = Source + Destination.
    /// </summary>
    Add = 0,

    /// <summary>
    /// Result = Source - Destination.
    /// </summary>
    Subtract = 1,

    /// <summary>
    /// Result = Destination - Source.
    /// </summary>
    ReverseSubtract = 2,

    /// <summary>
    /// Result = min(Source, Destination).
    /// </summary>
    Min = 3,

    /// <summary>
    /// Result = max(Source, Destination).
    /// </summary>
    Max = 4
}
