using SDL;

namespace Beutl.Graphics3D;

public readonly struct BlitRegion
{
    public BlitRegion(Texture texture)
    {
        Texture = texture;
        Width = texture.Width;
        Height = texture.Height;
    }

    public Texture Texture { get; init; }

    public uint MipLevel { get; init; }

    public uint LayerOrDepthPlane { get; init; }

    public uint X { get; init; }

    public uint Y { get; init; }

    public uint Width { get; init; }

    public uint Height { get; init; }

    internal unsafe SDL_GPUBlitRegion ToNative()
    {
        return new SDL_GPUBlitRegion
        {
            texture = Texture == null ? null : Texture.Handle,
            mip_level = MipLevel,
            layer_or_depth_plane = LayerOrDepthPlane,
            x = X,
            y = Y,
            w = Width,
            h = Height
        };
    }
}

public struct BufferLocation
{
    public IntPtr Buffer;
    public uint Offset;
}

public struct IndirectDrawCommand
{
    public uint NumVertices;
    public uint NumInstances;
    public uint FirstVertex;
    public uint FirstIndex;
}

public struct IndexedIndirectDrawCommand
{
    public uint NumIndices;
    public uint NumInstances;
    public uint FirstIndex;
    public int VertexOffset;
    public uint FirstInstance;
}

public struct IndirectDispatchCommand
{
    public uint GroupCountX;
    public uint GroupCountY;
    public uint GroupCountZ;
}

public struct VertexBufferDescription
{
    public uint Slot;
    public uint Pitch;
    public VertexInputRate InputRate;
    public uint InstanceStepRate;

    public static VertexBufferDescription Create<T>(
        uint slot = 0,
        VertexInputRate inputRate = VertexInputRate.Vertex,
        uint stepRate = 0
    ) where T : unmanaged
    {
        return new VertexBufferDescription
        {
            Slot = slot, Pitch = (uint)Marshal.SizeOf<T>(), InputRate = inputRate, InstanceStepRate = stepRate
        };
    }
}

public struct VertexAttribute
{
    public uint Location;
    public uint BufferSlot;
    public VertexElementFormat Format;
    public uint Offset;
}

public unsafe struct INTERNAL_VertexInputState
{
    public VertexBufferDescription* VertexBufferDescriptions;
    public uint NumVertexBuffers;
    public VertexAttribute* VertexAttributes;
    public uint NumVertexAttributes;
}

public struct StencilOpState
{
    public StencilOp FailOp;
    public StencilOp PassOp;
    public StencilOp DepthFailOp;
    public CompareOp CompareOp;
}

public struct ColorTargetBlendState
{
    public BlendFactor SrcColorBlendFactor;
    public BlendFactor DstColorBlendFactor;
    public BlendOp ColorBlendOp;
    public BlendFactor SrcAlphaBlendFactor;
    public BlendFactor DstAlphaBlendFactor;
    public BlendOp AlphaBlendOp;
    public ColorComponentFlags ColorWriteMask;
    public SDLBool EnableBlend;
    public SDLBool EnableColorWriteMask;
    public byte Padding2;
    public byte Padding3;

    public static ColorTargetBlendState NoWrite = new ColorTargetBlendState
    {
        EnableColorWriteMask = true, ColorWriteMask = ColorComponentFlags.None
    };

    public static ColorTargetBlendState NoBlend = new ColorTargetBlendState
    {
        EnableColorWriteMask = true, ColorWriteMask = ColorComponentFlags.RGBA
    };

    public static ColorTargetBlendState Opaque = new ColorTargetBlendState
    {
        EnableBlend = true,
        AlphaBlendOp = BlendOp.Add,
        ColorBlendOp = BlendOp.Add,
        SrcColorBlendFactor = BlendFactor.One,
        SrcAlphaBlendFactor = BlendFactor.One,
        DstColorBlendFactor = BlendFactor.Zero,
        DstAlphaBlendFactor = BlendFactor.Zero
    };

    public static ColorTargetBlendState Additive = new ColorTargetBlendState
    {
        EnableBlend = true,
        AlphaBlendOp = BlendOp.Add,
        ColorBlendOp = BlendOp.Add,
        SrcColorBlendFactor = BlendFactor.SrcAlpha,
        SrcAlphaBlendFactor = BlendFactor.SrcAlpha,
        DstColorBlendFactor = BlendFactor.One,
        DstAlphaBlendFactor = BlendFactor.One
    };

    public static ColorTargetBlendState PremultipliedAlphaBlend = new ColorTargetBlendState
    {
        EnableBlend = true,
        AlphaBlendOp = BlendOp.Add,
        ColorBlendOp = BlendOp.Add,
        SrcColorBlendFactor = BlendFactor.One,
        SrcAlphaBlendFactor = BlendFactor.One,
        DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha,
        DstAlphaBlendFactor = BlendFactor.OneMinusSrcAlpha
    };

    public static ColorTargetBlendState NonPremultipliedAlphaBlend = new ColorTargetBlendState
    {
        EnableBlend = true,
        AlphaBlendOp = BlendOp.Add,
        ColorBlendOp = BlendOp.Add,
        SrcColorBlendFactor = BlendFactor.SrcAlpha,
        SrcAlphaBlendFactor = BlendFactor.SrcAlpha,
        DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha,
        DstAlphaBlendFactor = BlendFactor.OneMinusSrcAlpha
    };
}

public struct RasterizerState
{
    public FillMode FillMode;
    public CullMode CullMode;
    public FrontFace FrontFace;
    public float DepthBiasConstantFactor;
    public float DepthBiasClamp;
    public float DepthBiasSlopFactor;
    public SDLBool EnableDepthBias;
    public SDLBool EnableDepthClip;
    public byte Padding1;
    public byte Padding2;

    public static RasterizerState CW_CullFront = new RasterizerState
    {
        CullMode = CullMode.Front, FrontFace = FrontFace.Clockwise, FillMode = FillMode.Fill
    };

    public static RasterizerState CW_CullBack = new RasterizerState
    {
        CullMode = CullMode.Back, FrontFace = FrontFace.Clockwise, FillMode = FillMode.Fill
    };

    public static RasterizerState CW_CullNone = new RasterizerState
    {
        CullMode = CullMode.None, FrontFace = FrontFace.Clockwise, FillMode = FillMode.Fill
    };

    public static RasterizerState CW_Wireframe = new RasterizerState
    {
        CullMode = CullMode.None, FrontFace = FrontFace.Clockwise, FillMode = FillMode.Line
    };

    public static RasterizerState CCW_CullFront = new RasterizerState
    {
        CullMode = CullMode.Front, FrontFace = FrontFace.CounterClockwise, FillMode = FillMode.Fill
    };

    public static readonly RasterizerState CCW_CullBack = new RasterizerState
    {
        CullMode = CullMode.Back, FrontFace = FrontFace.CounterClockwise, FillMode = FillMode.Fill
    };

    public static readonly RasterizerState CCW_CullNone = new RasterizerState
    {
        CullMode = CullMode.None, FrontFace = FrontFace.CounterClockwise, FillMode = FillMode.Fill
    };

    public static readonly RasterizerState CCW_Wireframe = new RasterizerState
    {
        CullMode = CullMode.None, FrontFace = FrontFace.CounterClockwise, FillMode = FillMode.Line
    };
}

[StructLayout(LayoutKind.Sequential)]
public struct MultisampleState
{
    public SampleCount SampleCount;
    public uint SampleMask;
    public SDLBool EnableMask;
    public byte Padding1;
    public byte Padding2;
    public byte Padding3;

    public static MultisampleState None = new MultisampleState { SampleCount = SampleCount.One };
}

[StructLayout(LayoutKind.Sequential)]
public struct DepthStencilState
{
    public CompareOp CompareOp;
    public StencilOpState BackStencilState;
    public StencilOpState FrontStencilState;
    public byte CompareMask;
    public byte WriteMask;
    public SDLBool EnableDepthTest;
    public SDLBool EnableDepthWrite;
    public SDLBool EnableStencilTest;
    public byte Padding1;
    public byte Padding2;
    public byte Padding3;

    public static DepthStencilState Disable = new DepthStencilState
    {
        EnableDepthTest = false, EnableDepthWrite = false, EnableStencilTest = false
    };
}

[StructLayout(LayoutKind.Sequential)]
public struct ColorTargetDescription
{
    public TextureFormat Format;
    public ColorTargetBlendState BlendState;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct INTERNAL_GraphicsPipelineTargetInfo
{
    public ColorTargetDescription* ColorTargetDescriptions;
    public uint NumColorTargets;
    public TextureFormat DepthStencilFormat;
    public SDLBool HasDepthStencilTarget;
    public byte Padding1;
    public byte Padding2;
    public byte Padding3;
}

public struct VertexInputState
{
    public VertexBufferDescription[] VertexBufferDescriptions;
    public VertexAttribute[] VertexAttributes;

    public static VertexInputState Empty = new VertexInputState
    {
        VertexBufferDescriptions = [], VertexAttributes = []
    };

    public static VertexInputState CreateSingleBinding<T>(uint slot = 0,
        VertexInputRate inputRate = VertexInputRate.Vertex, uint stepRate = 0, uint locationOffset = 0)
        where T : unmanaged, IVertexType
    {
        var description = VertexBufferDescription.Create<T>(slot, inputRate, stepRate);
        var attributes = new VertexAttribute[T.Formats.Length];

        for (uint i = 0; i < T.Formats.Length; i += 1)
        {
            var format = T.Formats[i];
            var offset = T.Offsets[i];

            attributes[i] = new VertexAttribute
            {
                BufferSlot = slot, Location = locationOffset + i, Format = format, Offset = offset
            };
        }

        return new VertexInputState { VertexBufferDescriptions = [description], VertexAttributes = attributes };
    }
}

public struct GraphicsPipelineTargetInfo
{
    public ColorTargetDescription[] ColorTargetDescriptions;
    public TextureFormat DepthStencilFormat;
    public SDLBool HasDepthStencilTarget;
    public byte Padding1;
    public byte Padding2;
    public byte Padding3;
}

public struct GraphicsPipelineCreateInfo
{
    public Shader VertexShader;
    public Shader FragmentShader;
    public VertexInputState VertexInputState;
    public PrimitiveType PrimitiveType;
    public RasterizerState RasterizerState;
    public MultisampleState MultisampleState;
    public DepthStencilState DepthStencilState;
    public GraphicsPipelineTargetInfo TargetInfo;
    public uint Props;
}

[StructLayout(LayoutKind.Sequential)]
public struct INTERNAL_GraphicsPipelineCreateInfo
{
    public IntPtr VertexShader;
    public IntPtr FragmentShader;
    public INTERNAL_VertexInputState VertexInputState;
    public PrimitiveType PrimitiveType;
    public RasterizerState RasterizerState;
    public MultisampleState MultisampleState;
    public DepthStencilState DepthStencilState;
    public INTERNAL_GraphicsPipelineTargetInfo TargetInfo;
    public uint Props;
}

public struct ComputePipelineCreateInfo
{
    public ShaderFormat Format;
    public uint NumSamplers;
    public uint NumReadonlyStorageTextures;
    public uint NumReadonlyStorageBuffers;
    public uint NumReadWriteStorageTextures;
    public uint NumReadWriteStorageBuffers;
    public uint NumUniformBuffers;
    public uint ThreadCountX;
    public uint ThreadCountY;
    public uint ThreadCountZ;
    public uint Props;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct INTERNAL_ComputePipelineCreateInfo
{
    public UIntPtr CodeSize;
    public byte* Code;
    public byte* EntryPoint;
    public ShaderFormat Format;
    public uint NumSamplers;
    public uint NumReadonlyStorageTextures;
    public uint NumReadonlyStorageBuffers;
    public uint NumReadWriteStorageTextures;
    public uint NumReadWriteStorageBuffers;
    public uint NumUniformBuffers;
    public uint ThreadCountX;
    public uint ThreadCountY;
    public uint ThreadCountZ;
    public uint Props;
}

public struct BlitInfo
{
    public BlitRegion Source;
    public BlitRegion Destination;
    public LoadOp LoadOp;
    public FColor ClearColor;
    public FlipMode FlipMode;
    public Filter Filter;
    public SDLBool Cycle;
    public byte Padding1;
    public byte Padding2;
    public byte Padding3;
}

[StructLayout(LayoutKind.Sequential)]
public struct StorageBufferReadWriteBinding
{
    public IntPtr Buffer;
    public SDLBool Cycle;
    public byte Padding1;
    public byte Padding2;
    public byte Padding3;

    public StorageBufferReadWriteBinding(Buffer buffer, bool cycle = false)
    {
        Buffer = buffer;
        Cycle = cycle;
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct StorageTextureReadWriteBinding
{
    public Texture Texture;
    public uint MipLevel;
    public uint Layer;
    public SDLBool Cycle;
    public byte Padding1;
    public byte Padding2;
    public byte Padding3;

    public StorageTextureReadWriteBinding(Texture texture, bool cycle = false)
    {
        Texture = texture;
        Cycle = cycle;
    }

    public StorageTextureReadWriteBinding(Texture texture, uint mipLevel, uint layer, bool cycle = false)
    {
        Texture = texture;
        MipLevel = mipLevel;
        Layer = layer;
        Cycle = cycle;
    }
}
