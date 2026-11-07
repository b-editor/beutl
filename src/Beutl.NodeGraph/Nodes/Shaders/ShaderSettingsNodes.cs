using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Shaders;
using Beutl.Language;
using Beutl.NodeGraph.Composition;
using Beutl.Serialization;

namespace Beutl.NodeGraph.Nodes.Shaders;

/// <summary>How a pass mixes its color outputs with what the target holds.</summary>
public partial class ShaderBlendNode : GraphNode
{
    public ShaderBlendNode()
    {
        Output = AddOutput<ShaderPassConfiguration>("Output", ShaderPortDisplays.Get("Settings"));
        SourceColor = AddBlendFactor("SourceColor", BlendFactor.One);
        DestinationColor = AddBlendFactor("DestinationColor", BlendFactor.OneMinusSrcAlpha);
        ColorOperation = AddInput<BlendOp>("ColorOperation", ShaderPortDisplays.Get("ColorOperation"));
        SourceAlpha = AddBlendFactor("SourceAlpha", BlendFactor.One);
        DestinationAlpha = AddBlendFactor("DestinationAlpha", BlendFactor.OneMinusSrcAlpha);
        AlphaOperation = AddInput<BlendOp>("AlphaOperation", ShaderPortDisplays.Get("AlphaOperation"));
    }

    private InputPort<BlendFactor> AddBlendFactor(string name, BlendFactor value)
    {
        InputPort<BlendFactor> port = AddInput<BlendFactor>(name, ShaderPortDisplays.Get(name));
        port.Property?.SetValue(value);
        return port;
    }

    public OutputPort<ShaderPassConfiguration> Output { get; }

    public InputPort<BlendFactor> SourceColor { get; }

    public InputPort<BlendFactor> DestinationColor { get; }

    public InputPort<BlendOp> ColorOperation { get; }

    public InputPort<BlendFactor> SourceAlpha { get; }

    public InputPort<BlendFactor> DestinationAlpha { get; }

    public InputPort<BlendOp> AlphaOperation { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            var state = new BlendState(true, SourceColor, DestinationColor, ColorOperation, SourceAlpha, DestinationAlpha, AlphaOperation);
            Output = new(_ => recorded => recorded.Pass.Blend(state));
        }
    }
}

/// <summary>A depth image for a pass, and how the pass tests and writes it.</summary>
public partial class ShaderDepthNode : GraphNode
{
    public ShaderDepthNode()
    {
        Output = AddOutput<ShaderPassConfiguration>("Output", ShaderPortDisplays.Get("Settings"));
        Image = AddInput<ShaderGraphValue<ShaderImage>>("Image", ShaderPortDisplays.Get("Image"));
        Load = AddInput<AttachmentLoadOp>("Load", ShaderPortDisplays.Get("Load"));
        Load.Property?.SetValue(AttachmentLoadOp.Clear);
        ClearDepth = AddInput<float>("ClearDepth", ShaderPortDisplays.Get("ClearDepth"));
        ClearDepth.Property?.SetValue(1f);
        ClearStencil = AddInput<uint>("ClearStencil", ShaderPortDisplays.Get("ClearStencil"));
        Test = AddInput<bool>("Test", ShaderPortDisplays.Get("DepthTest"));
        Test.Property?.SetValue(true);
        Write = AddInput<bool>("Write", ShaderPortDisplays.Get("DepthWrite"));
        Write.Property?.SetValue(true);
        Compare = AddInput<CompareFunction>("Compare", ShaderPortDisplays.Get("Compare"));
        StencilFront = AddInput<StencilFaceState>("StencilFront", ShaderPortDisplays.Get("StencilFront"));
        StencilBack = AddInput<StencilFaceState>("StencilBack", ShaderPortDisplays.Get("StencilBack"));
    }

    public OutputPort<ShaderPassConfiguration> Output { get; }

    public InputPort<ShaderGraphValue<ShaderImage>> Image { get; }

    public InputPort<AttachmentLoadOp> Load { get; }

    public InputPort<float> ClearDepth { get; }

    public InputPort<uint> ClearStencil { get; }

    public InputPort<bool> Test { get; }

    public InputPort<bool> Write { get; }

    public InputPort<CompareFunction> Compare { get; }

    public InputPort<StencilFaceState> StencilFront { get; }

    public InputPort<StencilFaceState> StencilBack { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            (ShaderGraphValue<ShaderImage>? wire, AttachmentLoadOp load, float clearDepth, uint clearStencil)
                = (Image, Load, ClearDepth, ClearStencil);
            ShaderDepthNode node = RequireOriginal();
            bool hasFront = context.HasConnection(node.StencilFront);
            bool hasBack = context.HasConnection(node.StencilBack);
            var state = new DepthStencilState(Test, Write, Compare, hasFront || hasBack,
                hasFront ? StencilFront : StencilBack, hasBack ? StencilBack : StencilFront);
            Output = new(recording =>
            {
                ShaderImage image = recording.Require(wire, NodeGraphStrings.ShaderPort_Image);
                return recorded =>
                {
                    recorded.Pass.DepthAttachment(image, load, clearDepth, clearStencil);
                    recorded.Pass.DepthStencil(state);
                    recorded.DepthAttachment = image;
                };
            });
        }
    }
}

/// <summary>Stencil operations for one face orientation.</summary>
public partial class ShaderStencilNode : GraphNode
{
    public ShaderStencilNode()
    {
        Output = AddOutput<StencilFaceState>("Output", NodePortDisplays.Output);
        Compare = AddInput<CompareFunction>("Compare", ShaderPortDisplays.Get("Compare"));
        Compare.Property?.SetValue(CompareFunction.Always);
        Fail = AddInput<StencilOperation>("Fail", ShaderPortDisplays.Get("StencilFail"));
        DepthFail = AddInput<StencilOperation>("DepthFail", ShaderPortDisplays.Get("StencilDepthFail"));
        Pass = AddInput<StencilOperation>("Pass", ShaderPortDisplays.Get("StencilPass"));
        CompareMask = AddInput<uint>("CompareMask", ShaderPortDisplays.Get("CompareMask"));
        CompareMask.Property?.SetValue(uint.MaxValue);
        WriteMask = AddInput<uint>("WriteMask", ShaderPortDisplays.Get("WriteMask"));
        WriteMask.Property?.SetValue(uint.MaxValue);
        Reference = AddInput<uint>("Reference", ShaderPortDisplays.Get("Reference"));
    }

    public OutputPort<StencilFaceState> Output { get; }

    public InputPort<CompareFunction> Compare { get; }

    public InputPort<StencilOperation> Fail { get; }

    public InputPort<StencilOperation> DepthFail { get; }

    public InputPort<StencilOperation> Pass { get; }

    public InputPort<uint> CompareMask { get; }

    public InputPort<uint> WriteMask { get; }

    public InputPort<uint> Reference { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            Output = new StencilFaceState(Compare, Fail, DepthFail, Pass, CompareMask, WriteMask, Reference);
        }
    }
}

/// <summary>How a pass assembles primitives and which of them it keeps.</summary>
public partial class ShaderRasterizerNode : GraphNode
{
    public ShaderRasterizerNode()
    {
        Output = AddOutput<ShaderPassConfiguration>("Output", ShaderPortDisplays.Get("Settings"));
        Topology = AddInput<PrimitiveTopology>("Topology", ShaderPortDisplays.Get("Topology"));
        Cull = AddInput<CullMode>("Cull", ShaderPortDisplays.Get("Cull"));
        Front = AddInput<FrontFace>("Front", ShaderPortDisplays.Get("Front"));
        Front.Property?.SetValue(FrontFace.CounterClockwise);
        PatchControlPoints = AddInput<int>("PatchControlPoints", ShaderPortDisplays.Get("PatchControlPoints"));
        PatchControlPoints.Property?.SetValue(3);
    }

    public OutputPort<ShaderPassConfiguration> Output { get; }

    public InputPort<PrimitiveTopology> Topology { get; }

    public InputPort<CullMode> Cull { get; }

    public InputPort<FrontFace> Front { get; }

    public InputPort<int> PatchControlPoints { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            (PrimitiveTopology topology, CullMode cull, FrontFace front, int controlPoints) = (Topology, Cull, Front, PatchControlPoints);
            Output = new(_ => recorded =>
            {
                recorded.Pass.Topology(topology);
                recorded.Pass.Cull(cull, front);
                if (topology == PrimitiveTopology.PatchList) recorded.Pass.PatchControlPoints(controlPoints);
            });
        }
    }
}

/// <summary>One attribute a vertex shader reads from a vertex buffer.</summary>
public partial class ShaderVertexAttributeNode : GraphNode
{
    public ShaderVertexAttributeNode()
    {
        Output = AddOutput<VertexAttributeDescription>("Output", NodePortDisplays.Output);
        Location = AddInput<int>("Location", ShaderPortDisplays.Get("Location"));
        Format = AddInput<VertexFormat>("Format", ShaderPortDisplays.Get("Format"));
        Offset = AddInput<int>("Offset", ShaderPortDisplays.Get("Offset"));
    }

    public OutputPort<VertexAttributeDescription> Output { get; }

    public InputPort<int> Location { get; }

    public InputPort<VertexFormat> Format { get; }

    public InputPort<int> Offset { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            Output = new VertexAttributeDescription { Location = (uint)Location, Format = Format, Offset = (uint)Offset };
        }
    }
}

/// <summary>A buffer a pass reads vertices or instances from, with the attributes laid out in it.</summary>
public partial class ShaderVertexBufferNode : GraphNode
{
    public ShaderVertexBufferNode()
    {
        Output = AddOutput<ShaderPassConfiguration>("Output", ShaderPortDisplays.Get("Settings"));
        Buffer = AddInput<ShaderGraphValue<ShaderBuffer>>("Buffer", ShaderPortDisplays.Get("Buffer"));
        Binding = AddInput<int>("Binding", ShaderPortDisplays.Get("Binding"));
        Offset = AddInput<int>("Offset", ShaderPortDisplays.Get("Offset"));
        Stride = AddInput<int>("Stride", ShaderPortDisplays.Get("Stride"));
        Rate = AddInput<VertexInputRate>("Rate", ShaderPortDisplays.Get("Rate"));
        Attributes = AddListInput<VertexAttributeDescription>("Attributes", ShaderPortDisplays.Get("Attributes"));
    }

    public OutputPort<ShaderPassConfiguration> Output { get; }

    public InputPort<ShaderGraphValue<ShaderBuffer>> Buffer { get; }

    public InputPort<int> Binding { get; }

    public InputPort<int> Offset { get; }

    public InputPort<int> Stride { get; }

    public InputPort<VertexInputRate> Rate { get; }

    public ListInputPort<VertexAttributeDescription> Attributes { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            (ShaderGraphValue<ShaderBuffer>? wire, int binding, int offset, int stride, VertexInputRate rate)
                = (Buffer, Binding, Offset, Stride, Rate);
            List<VertexAttributeDescription> attributes = context.CollectListInputValues(RequireOriginal().Attributes);
            Output = new(recording =>
            {
                ShaderBuffer buffer = recording.Require(wire, NodeGraphStrings.ShaderPort_Buffer);
                return recorded =>
                {
                    recorded.Pass.VertexBuffer(binding, buffer, offset);
                    recorded.VertexBindings.Add(new VertexBindingDescription { Binding = (uint)binding, Stride = (uint)stride, InputRate = rate });
                    foreach (VertexAttributeDescription attribute in attributes)
                    {
                        VertexAttributeDescription bound = attribute;
                        bound.Binding = (uint)binding;
                        recorded.VertexAttributes.Add(bound);
                    }
                };
            });
        }
    }
}

/// <summary>A buffer a pass reads vertex indices from.</summary>
public partial class ShaderIndexBufferNode : GraphNode
{
    public ShaderIndexBufferNode()
    {
        Output = AddOutput<ShaderPassConfiguration>("Output", ShaderPortDisplays.Get("Settings"));
        Buffer = AddInput<ShaderGraphValue<ShaderBuffer>>("Buffer", ShaderPortDisplays.Get("Buffer"));
        Format = AddInput<IndexFormat>("Format", ShaderPortDisplays.Get("Format"));
        Format.Property?.SetValue(IndexFormat.UInt32);
        Offset = AddInput<int>("Offset", ShaderPortDisplays.Get("Offset"));
    }

    public OutputPort<ShaderPassConfiguration> Output { get; }

    public InputPort<ShaderGraphValue<ShaderBuffer>> Buffer { get; }

    public InputPort<IndexFormat> Format { get; }

    public InputPort<int> Offset { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            (ShaderGraphValue<ShaderBuffer>? wire, IndexFormat format, int offset) = (Buffer, Format, Offset);
            Output = new(recording =>
            {
                ShaderBuffer buffer = recording.Require(wire, NodeGraphStrings.ShaderPort_Buffer);
                return recorded => recorded.Pass.IndexBuffer(buffer, format, offset);
            });
        }
    }
}

/// <summary>What a pass's color outputs start from, instead of keeping a connected target and clearing a new image.</summary>
public partial class ShaderLoadNode : GraphNode
{
    public ShaderLoadNode()
    {
        Output = AddOutput<ShaderPassConfiguration>("Output", ShaderPortDisplays.Get("Settings"));
        Load = AddInput<AttachmentLoadOp>("Load", ShaderPortDisplays.Get("Load"));
        Load.Property?.SetValue(AttachmentLoadOp.Clear);
    }

    public OutputPort<ShaderPassConfiguration> Output { get; }

    public InputPort<AttachmentLoadOp> Load { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            AttachmentLoadOp load = Load;
            Output = new(_ => recorded => recorded.Load = load);
        }
    }
}

/// <summary>Lets a pass render only the part of its output something needs.</summary>
public partial class ShaderPartialRenderingNode : GraphNode
{
    public ShaderPartialRenderingNode()
    {
        Output = AddOutput<ShaderPassConfiguration>("Output", ShaderPortDisplays.Get("Settings"));
    }

    public OutputPort<ShaderPassConfiguration> Output { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            Output = new(_ => static recorded => recorded.Pass.PartialRendering());
        }
    }
}

/// <summary>
/// Lets the images a pass uses hold only the part of their bounds something needs, so that a picture far larger than the
/// frame keeps its resolution. The pass also renders only that part of its output, as with partial rendering.
/// </summary>
/// <remarks>
/// The pass has to find every pixel through the images' parameters, since their raster bounds then describe the part they
/// hold and the UVs cover the part its target holds. A target that shares its source's frame, as a pass's own target does
/// when it has no padding, reads its source at the UVs as before. An input region says what part of an input each output
/// pixel reads; without one the input stays whole.
/// </remarks>
public partial class ShaderPartialImagesNode : GraphNode
{
    public ShaderPartialImagesNode()
    {
        Output = AddOutput<ShaderPassConfiguration>("Output", ShaderPortDisplays.Get("Settings"));
    }

    public OutputPort<ShaderPassConfiguration> Output { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            Output = new(_ => static recorded => recorded.Pass.PartialImages());
        }
    }
}

public enum ShaderDrawMode
{
    [Display(Name = nameof(NodeGraphStrings.ShaderDrawMode_Vertices), ResourceType = typeof(NodeGraphStrings))]
    Vertices,

    [Display(Name = nameof(NodeGraphStrings.ShaderDrawMode_Indexed), ResourceType = typeof(NodeGraphStrings))]
    Indexed,

    [Display(Name = nameof(NodeGraphStrings.ShaderDrawMode_Indirect), ResourceType = typeof(NodeGraphStrings))]
    Indirect,

    [Display(Name = nameof(NodeGraphStrings.ShaderDrawMode_IndexedIndirect), ResourceType = typeof(NodeGraphStrings))]
    IndexedIndirect,
}

/// <summary>What a render pass draws. Without this node it draws three vertices.</summary>
public partial class ShaderDrawNode : ShaderDynamicNode
{
    public ShaderDrawNode()
    {
        ScanProperties<ShaderDrawNode>();
        Output = AddOutput<ShaderPassConfiguration>("Output", ShaderPortDisplays.Get("Settings"));
        InputPort<ShaderDrawMode> mode = AddInput(this, Mode);
        SealFixedPorts(mode);
        Mode.ValueChanged += (_, _) => SyncPorts(CreatePortSpecs(Mode.Value));
        SyncPorts(CreatePortSpecs(Mode.Value));
    }

    [Display(Name = nameof(NodeGraphStrings.ShaderPort_Mode), ResourceType = typeof(NodeGraphStrings))]
    public IProperty<ShaderDrawMode> Mode { get; } = new ValueOnlyProperty<ShaderDrawMode>(ShaderDrawMode.Vertices);

    public OutputPort<ShaderPassConfiguration> Output { get; }

    private static IReadOnlyList<ShaderPortSpec> CreatePortSpecs(ShaderDrawMode mode) => mode switch
    {
        ShaderDrawMode.Vertices =>
        [
            Spec<int>("VertexCount", 3), Spec<int>("InstanceCount", 1), Spec<int>("FirstVertex"), Spec<int>("FirstInstance")
        ],
        ShaderDrawMode.Indexed =>
        [
            Spec<int>("IndexCount", 3), Spec<int>("InstanceCount", 1), Spec<int>("FirstIndex"),
            Spec<int>("VertexOffset"), Spec<int>("FirstInstance")
        ],
        _ =>
        [
            Spec<ShaderGraphValue<ShaderBuffer>>("Commands"), Spec<int>("Offset"), Spec<int>("DrawCount", 1), Spec<int>("Stride")
        ],
    };

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            ShaderDrawMode mode = RequireOriginal().Mode.Value;
            (int count, int instances, int first, int vertexOffset, int firstInstance, int offset, int draws, int stride)
                = (Get<int>(mode == ShaderDrawMode.Indexed ? "IndexCount" : "VertexCount"), Get<int>("InstanceCount"),
                    Get<int>(mode == ShaderDrawMode.Indexed ? "FirstIndex" : "FirstVertex"), Get<int>("VertexOffset"),
                    Get<int>("FirstInstance"), Get<int>("Offset"), Get<int>("DrawCount"), Get<int>("Stride"));
            ShaderGraphValue<ShaderBuffer>? commands = Get<ShaderGraphValue<ShaderBuffer>>("Commands");
            Output = new(recording =>
            {
                ShaderBuffer? buffer = mode is ShaderDrawMode.Indirect or ShaderDrawMode.IndexedIndirect
                    ? recording.Require(commands, NodeGraphStrings.ShaderPort_Commands) : null;
                return recorded =>
                {
                    switch (mode)
                    {
                        case ShaderDrawMode.Vertices:
                            recorded.Pass.Draw(count, instances, first, firstInstance);
                            break;
                        case ShaderDrawMode.Indexed:
                            recorded.Pass.DrawIndexed(count, instances, first, vertexOffset, firstInstance);
                            break;
                        case ShaderDrawMode.Indirect:
                            recorded.Pass.DrawIndirect(buffer!, offset, draws, stride > 0 ? stride : 16);
                            break;
                        case ShaderDrawMode.IndexedIndirect:
                            recorded.Pass.DrawIndexedIndirect(buffer!, offset, draws, stride > 0 ? stride : 20);
                            break;
                    }

                    recorded.HasWork = true;
                };
            });
        }
    }
}

public enum ShaderDispatchMode
{
    [Display(Name = nameof(NodeGraphStrings.ShaderDispatchMode_Image), ResourceType = typeof(NodeGraphStrings))]
    Image,

    [Display(Name = nameof(NodeGraphStrings.ShaderDispatchMode_Workgroups), ResourceType = typeof(NodeGraphStrings))]
    Workgroups,

    [Display(Name = nameof(NodeGraphStrings.ShaderDispatchMode_Invocations), ResourceType = typeof(NodeGraphStrings))]
    Invocations,

    [Display(Name = nameof(NodeGraphStrings.ShaderDispatchMode_Indirect), ResourceType = typeof(NodeGraphStrings))]
    Indirect,
}

/// <summary>How many times a compute pass runs. Without this node it runs over the first image it writes.</summary>
public partial class ShaderDispatchNode : ShaderDynamicNode
{
    public ShaderDispatchNode()
    {
        ScanProperties<ShaderDispatchNode>();
        Output = AddOutput<ShaderPassConfiguration>("Output", ShaderPortDisplays.Get("Settings"));
        InputPort<ShaderDispatchMode> mode = AddInput(this, Mode);
        SealFixedPorts(mode);
        Mode.ValueChanged += (_, _) => SyncPorts(CreatePortSpecs(Mode.Value));
        SyncPorts(CreatePortSpecs(Mode.Value));
    }

    [Display(Name = nameof(NodeGraphStrings.ShaderPort_Mode), ResourceType = typeof(NodeGraphStrings))]
    public IProperty<ShaderDispatchMode> Mode { get; } = new ValueOnlyProperty<ShaderDispatchMode>(ShaderDispatchMode.Image);

    public OutputPort<ShaderPassConfiguration> Output { get; }

    private static IReadOnlyList<ShaderPortSpec> CreatePortSpecs(ShaderDispatchMode mode) => mode switch
    {
        ShaderDispatchMode.Image => [Spec<ShaderGraphValue<ShaderImage>>("Image")],
        ShaderDispatchMode.Indirect => [Spec<ShaderGraphValue<ShaderBuffer>>("Commands"), Spec<int>("Offset")],
        _ => [Spec<int>("X", 1), Spec<int>("Y", 1), Spec<int>("Z", 1)],
    };

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            ShaderDispatchMode mode = RequireOriginal().Mode.Value;
            (int x, int y, int z, int offset) = (Get<int>("X"), Get<int>("Y"), Get<int>("Z"), Get<int>("Offset"));
            ShaderGraphValue<ShaderImage>? image = Get<ShaderGraphValue<ShaderImage>>("Image");
            ShaderGraphValue<ShaderBuffer>? commands = Get<ShaderGraphValue<ShaderBuffer>>("Commands");
            Output = new(recording =>
            {
                ShaderImage? target = mode == ShaderDispatchMode.Image
                    ? recording.Require(image, NodeGraphStrings.ShaderPort_Image) : null;
                ShaderBuffer? buffer = mode == ShaderDispatchMode.Indirect
                    ? recording.Require(commands, NodeGraphStrings.ShaderPort_Commands) : null;
                return recorded =>
                {
                    switch (mode)
                    {
                        case ShaderDispatchMode.Image:
                            recorded.Pass.DispatchImage(target!);
                            break;
                        case ShaderDispatchMode.Workgroups:
                            recorded.Pass.Dispatch(x, y, z);
                            break;
                        case ShaderDispatchMode.Invocations:
                            recorded.Pass.DispatchThreads(x, y, z);
                            break;
                        case ShaderDispatchMode.Indirect:
                            recorded.Pass.DispatchIndirect(buffer!, offset);
                            break;
                    }

                    recorded.HasWork = true;
                };
            });
        }
    }
}

/// <summary>How a pass filters and addresses the image coming through this wire.</summary>
public partial class ShaderSamplerNode : GraphNode
{
    public ShaderSamplerNode()
    {
        Output = AddOutput<ShaderGraphValue<ShaderImage>>("Output", NodePortDisplays.Output);
        Image = AddInput<ShaderGraphValue<ShaderImage>>("Image", ShaderPortDisplays.Get("Image"));
        MinFilter = AddInput<SamplerFilter>("MinFilter", ShaderPortDisplays.Get("MinFilter"));
        MinFilter.Property?.SetValue(SamplerFilter.Linear);
        MagFilter = AddInput<SamplerFilter>("MagFilter", ShaderPortDisplays.Get("MagFilter"));
        MagFilter.Property?.SetValue(SamplerFilter.Linear);
        MipmapFilter = AddInput<SamplerFilter>("MipmapFilter", ShaderPortDisplays.Get("MipmapFilter"));
        MipmapFilter.Property?.SetValue(SamplerFilter.Linear);
        AddressU = AddInput<SamplerAddressMode>("AddressU", ShaderPortDisplays.Get("AddressU"));
        AddressU.Property?.SetValue(SamplerAddressMode.ClampToBorder);
        AddressV = AddInput<SamplerAddressMode>("AddressV", ShaderPortDisplays.Get("AddressV"));
        AddressV.Property?.SetValue(SamplerAddressMode.ClampToBorder);
        AddressW = AddInput<SamplerAddressMode>("AddressW", ShaderPortDisplays.Get("AddressW"));
        AddressW.Property?.SetValue(SamplerAddressMode.ClampToBorder);
        MinLod = AddInput<float>("MinLod", ShaderPortDisplays.Get("MinLod"));
        MaxLod = AddInput<float>("MaxLod", ShaderPortDisplays.Get("MaxLod"));
        MaxLod.Property?.SetValue(1000f);
        UseCompare = AddInput<bool>("UseCompare", ShaderPortDisplays.Get("UseCompare"));
        Compare = AddInput<CompareFunction>("Compare", ShaderPortDisplays.Get("Compare"));
    }

    public OutputPort<ShaderGraphValue<ShaderImage>> Output { get; }

    public InputPort<ShaderGraphValue<ShaderImage>> Image { get; }

    public InputPort<SamplerFilter> MinFilter { get; }

    public InputPort<SamplerFilter> MagFilter { get; }

    public InputPort<SamplerFilter> MipmapFilter { get; }

    public InputPort<SamplerAddressMode> AddressU { get; }

    public InputPort<SamplerAddressMode> AddressV { get; }

    public InputPort<SamplerAddressMode> AddressW { get; }

    public InputPort<float> MinLod { get; }

    public InputPort<float> MaxLod { get; }

    public InputPort<bool> UseCompare { get; }

    public InputPort<CompareFunction> Compare { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            var sampler = new SamplerState(MinFilter, MagFilter, AddressU, AddressV)
            {
                AddressW = AddressW,
                MipmapFilter = MipmapFilter,
                MinLod = MinLod,
                MaxLod = MaxLod,
                Compare = UseCompare ? Compare : null
            };
            ShaderGraphValue<ShaderImage> image = Image ?? ShaderGraphRecordingContext.SourceImage;
            Output = new ShaderSampledImageValue(image, sampler, (image as ShaderSampledImageValue)?.Region);
        }
    }
}

/// <summary>Declares what part of the image coming through this wire each output pixel reads, so only that part is needed.</summary>
public partial class ShaderInputRegionNode : GraphNode
{
    public ShaderInputRegionNode()
    {
        Output = AddOutput<ShaderGraphValue<ShaderImage>>("Output", NodePortDisplays.Output);
        Image = AddInput<ShaderGraphValue<ShaderImage>>("Image", ShaderPortDisplays.Get("Image"));
        Transform = AddInput<Matrix>("Transform", ShaderPortDisplays.Get("Transform"));
        Transform.Property?.SetValue(Matrix.Identity);
        Padding = AddInput<float>("Padding", ShaderPortDisplays.Get("Padding"));
    }

    public OutputPort<ShaderGraphValue<ShaderImage>> Output { get; }

    public InputPort<ShaderGraphValue<ShaderImage>> Image { get; }

    public InputPort<Matrix> Transform { get; }

    public InputPort<float> Padding { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            ShaderGraphValue<ShaderImage> image = Image ?? ShaderGraphRecordingContext.SourceImage;
            Output = new ShaderSampledImageValue(image, (image as ShaderSampledImageValue)?.Sampler, new ShaderInputRegion(Transform, Padding));
        }
    }
}

/// <summary>Fills a uniform block with an image's bounds and pixel size: three vec4 values.</summary>
public partial class ShaderImageParametersNode : GraphNode
{
    public ShaderImageParametersNode()
    {
        Output = AddOutput<ShaderGraphValue<ShaderBuffer>>("Output", NodePortDisplays.Output);
        Image = AddInput<ShaderGraphValue<ShaderImage>>("Image", ShaderPortDisplays.Get("Image"));
    }

    public OutputPort<ShaderGraphValue<ShaderBuffer>> Output { get; }

    public InputPort<ShaderGraphValue<ShaderImage>> Image { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            Output = new ShaderImageParametersValue(Image ?? ShaderGraphRecordingContext.SourceImage);
        }
    }
}
