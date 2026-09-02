using System.Collections.Immutable;

namespace Beutl.Graphics.Backend;

/// <summary>
/// Options for creating a graphics pipeline.
/// </summary>
public struct PipelineOptions
{
    /// <summary>
    /// Gets or sets the immutable specialization constants applied when the pipeline is created.
    /// A default or empty array applies no specialization.
    /// </summary>
    /// <remarks>
    /// Specialization constants are part of pipeline identity. Pipeline caches must compare their stage,
    /// constant ID, scalar size, and value rather than the array instance or insertion order.
    /// </remarks>
    public ImmutableArray<SpecializationConstant> SpecializationConstants { get; set; }

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
        SpecializationConstants = [],
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
        SpecializationConstants = [],
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
        SpecializationConstants = [],
        DepthTestEnabled = true,
        DepthWriteEnabled = false,
        CullMode = CullMode.Back,
        FrontFace = FrontFace.CounterClockwise,
        BlendEnabled = true,
        SrcColorBlendFactor = BlendFactor.One,
        DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha,
        SrcAlphaBlendFactor = BlendFactor.One,
        DstAlphaBlendFactor = BlendFactor.OneMinusSrcAlpha,
        ColorBlendOp = BlendOp.Add,
        AlphaBlendOp = BlendOp.Add
    };
}
