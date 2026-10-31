using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;

namespace Beutl.Graphics.Shaders;

/// <summary>Declares bindings, attachments, parameter bytes and one draw or dispatch.</summary>
/// <remarks>Bindings refer to set zero in the shader. The compiled interface is validated before execution.</remarks>
public sealed partial class ShaderPassBuilder
{
    private readonly ShaderGraphBuilder _graph;
    private readonly string _name;
    private readonly ShaderProgram _program;
    private readonly Dictionary<int, ShaderPassBinding> _bindings = [];
    private readonly Dictionary<int, ShaderColorAttachment> _attachments = [];
    private ImmutableArray<byte> _constants = [];
    private readonly Dictionary<ShaderRuntimeConstantKind, ShaderRuntimeConstant> _runtimeConstants = [];
    private BlendState _blend = BlendState.Replace;
    private (int X, int Y, int Z)? _work;
    private bool _threads;
    private bool _sealed;
    private ImmutableArray<VertexBindingDescription> _vertexBindings = [];
    private ImmutableArray<VertexAttributeDescription> _vertexAttributes = [];
    private readonly Dictionary<int, ShaderVertexBuffer> _vertices = [];
    private ShaderIndexBuffer? _indices;
    private PrimitiveTopology _topology;
    private bool _indexed;
    private int _firstElement;
    private int _vertexOffset;
    private int _firstInstance;
    private bool _partialRendering;
    private readonly Dictionary<int, ShaderInputRegion> _inputRegions = [];

    internal ShaderPassBuilder(ShaderGraphBuilder graph, string name, ShaderProgram program, bool fullscreen)
    {
        _graph = graph;
        _name = name;
        _program = program;
        if (fullscreen)
            _work = (3, 1, 1);
    }

    public void SampledImage(int binding, ShaderImage image, SamplerState? sampler = null)
    {
        SamplerState state = sampler ?? SamplerState.LinearTransparent;
        if (!Enum.IsDefined(state.MinFilter) || !Enum.IsDefined(state.MagFilter)
            || !Enum.IsDefined(state.AddressU) || !Enum.IsDefined(state.AddressV) || !Enum.IsDefined(state.AddressW)
            || !Enum.IsDefined(state.MipmapFilter) || state.Compare is { } comparison && !Enum.IsDefined(comparison)
            || !float.IsFinite(state.MinLod) || !float.IsFinite(state.MaxLod) || state.MinLod < 0 || state.MaxLod < state.MinLod)
            throw new ArgumentOutOfRangeException(nameof(sampler));
        AddBinding(binding, image, DescriptorType.CombinedImageSampler, ResourceAccess.Read, state);
    }

    /// <summary>Declares that color outputs can be evaluated independently for a requested rectangle.</summary>
    /// <remarks>Undeclared input footprints remain full. Compute, depth, subresource views and storage writes
    /// keep full execution. The viewport, UVs and image metadata always retain their complete-image coordinates.</remarks>
    public void PartialRendering()
    {
        EnsureGraphics();
        _partialRendering = true;
    }

    /// <summary>Declares the complete sampling footprint of a sampled-image binding and enables partial rendering.</summary>
    /// <remarks>Output coordinates refer to the first color attachment's composition frame.</remarks>
    public void InputRegion(int binding, ShaderInputRegion region)
    {
        PartialRendering();
        ArgumentOutOfRangeException.ThrowIfNegative(binding);
        region.Validate();
        _inputRegions[binding] = region;
    }

    public void StorageBuffer(int binding, ShaderBuffer buffer, ResourceAccess access)
        => AddBinding(binding, buffer, DescriptorType.StorageBuffer, access, default);

    /// <summary>Binds a typed 2D image for shader image loads and stores.</summary>
    /// <remarks>
    /// The shader's image format must match the image format. ReadWrite requires an earlier write;
    /// Write declares that this pass initializes the image contents used by subsequent passes.
    /// The source image can only be read. Graphics-stage writes require device support.
    /// </remarks>
    public void StorageImage(int binding, ShaderImage image, ResourceAccess access)
    {
        if (ReferenceEquals(image.Root, _graph.Source) && (access & ResourceAccess.Write) != 0)
            throw new ArgumentException("The source image is read-only.", nameof(image));
        if (image.MipLevels != 1 || image.Format.IsDepthFormat())
            throw new ArgumentException("Storage image bindings select one color mip level.", nameof(image));
        AddBinding(binding, image, DescriptorType.StorageImage, access, default);
    }

    public void UniformBuffer(int binding, ShaderBuffer buffer)
        => AddBinding(binding, buffer, DescriptorType.UniformBuffer, ResourceAccess.Read, default);

    /// <summary>
    /// Binds three std140 vec4 values: logical bounds (x,y,width,height), raster bounds in the same coordinates,
    /// and (pixel width, pixel height, raster scale, 0). These are resolved for the current request.
    /// </summary>
    /// <remarks>This reads metadata only and may refer to an attachment written by this pass.</remarks>
    public void ImageParameters(int binding, ShaderImage image)
        => AddBinding(binding, image, DescriptorType.UniformBuffer, ResourceAccess.Read, default, metadata: true);

    // Preserve the legacy script ABI without retaining a render-time author callback.
    internal void ImageSizeConstants(ShaderImage image, int offset)
    {
        EnsureRecording();
        _graph.ValidateResource(image);
        _runtimeConstants[ShaderRuntimeConstantKind.ImageSize] = new(ShaderRuntimeConstantKind.ImageSize, offset, image);
    }

    internal void BoundsOriginConstants(RelativePoint origin, int offset)
    {
        EnsureRecording();
        _runtimeConstants[ShaderRuntimeConstantKind.BoundsOrigin] = new(ShaderRuntimeConstantKind.BoundsOrigin, offset, Origin: origin);
    }

    private void AddBinding(int binding, ShaderResource resource, DescriptorType type,
        ResourceAccess access, SamplerState sampler, bool metadata = false)
    {
        EnsureRecording();
        _graph.ValidateResource(resource);
        ArgumentOutOfRangeException.ThrowIfNegative(binding);
        if (access is not (ResourceAccess.Read or ResourceAccess.Write or ResourceAccess.ReadWrite))
            throw new ArgumentOutOfRangeException(nameof(access));
        if (!_bindings.TryAdd(binding, new(binding, resource, type, access, sampler, metadata)))
            throw new ArgumentException($"Pass '{_name}' already binds slot {binding}.", nameof(binding));
    }

    /// <summary>Declares a color output. All attachments in a pass must have the same footprint and load operation.</summary>
    public void ColorAttachment(int location, ShaderImage image, AttachmentLoadOp load = AttachmentLoadOp.Clear)
    {
        EnsureGraphics();
        _graph.ValidateResource(image);
        ArgumentOutOfRangeException.ThrowIfNegative(location);
        if (!Enum.IsDefined(load))
            throw new ArgumentOutOfRangeException(nameof(load));
        if (ReferenceEquals(image.Root, _graph.Source))
            throw new ArgumentException("The source image is read-only.", nameof(image));
        if (!image.IsAttachment || image.Format.IsDepthFormat())
            throw new ArgumentException("A color attachment selects one 2D color subresource.", nameof(image));
        if (!_attachments.TryAdd(location, new(location, image, load)))
            throw new ArgumentException($"Pass '{_name}' already writes location {location}.", nameof(location));
    }

    /// <summary>Copies parameter bytes. Use an explicitly laid out unmanaged struct matching the shader block.</summary>
    /// <remarks>The current backend accepts up to 128 bytes, in multiples of four. Values are not pipeline cache keys.</remarks>
    public void PushConstants<T>(T value) where T : unmanaged
        => PushConstants(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref value, 1)));

    /// <summary>Copies packed push-constant bytes without requiring a CLR struct for their layout.</summary>
    public void PushConstants(ReadOnlySpan<byte> bytes)
    {
        EnsureRecording();
        if (bytes.Length > VulkanPipeline3D.MaxPushConstantsSize || bytes.Length % 4 != 0)
            throw new ArgumentException("Push constants must contain at most 128 bytes in multiples of four.", nameof(bytes));
        _constants = [.. bytes];
    }

    public void Blend(BlendState state)
    {
        EnsureGraphics();
        if (!Enum.IsDefined(state.SourceColor) || !Enum.IsDefined(state.DestinationColor)
            || !Enum.IsDefined(state.SourceAlpha) || !Enum.IsDefined(state.DestinationAlpha)
            || !Enum.IsDefined(state.ColorOperation) || !Enum.IsDefined(state.AlphaOperation))
            throw new ArgumentOutOfRangeException(nameof(state));
        _blend = state;
    }

    /// <summary>Copies the vertex layout. Vertex bindings are independent of descriptor bindings.</summary>
    public void VertexInput(VertexInputDescription description)
    {
        EnsureGraphics();
        _vertexBindings = [.. description.Bindings ?? []];
        _vertexAttributes = [.. description.Attributes ?? []];
    }

    /// <summary>Binds a graph buffer as vertex or instance data, at a byte offset.</summary>
    public void VertexBuffer(int binding, ShaderBuffer buffer, long offset = 0)
    {
        EnsureGraphics();
        _graph.ValidateResource(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(binding);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (offset >= buffer.ByteLength)
            throw new ArgumentOutOfRangeException(nameof(offset));
        if (!_vertices.TryAdd(binding, new(binding, buffer, offset)))
            throw new ArgumentException($"Pass '{_name}' already binds vertex slot {binding}.", nameof(binding));
    }

    /// <summary>Binds tightly packed unsigned indices at a byte offset, independently of the buffer's element type.</summary>
    /// <remarks>Index values, including compute output, must address valid vertices after applying vertexOffset. No host readback is performed.</remarks>
    public void IndexBuffer(ShaderBuffer buffer, IndexFormat format = IndexFormat.UInt32, long offset = 0)
    {
        EnsureGraphics();
        _graph.ValidateResource(buffer);
        if (!Enum.IsDefined(format))
            throw new ArgumentOutOfRangeException(nameof(format));
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        int size = format == IndexFormat.UInt16 ? 2 : 4;
        if (offset >= buffer.ByteLength || offset % size != 0)
            throw new ArgumentOutOfRangeException(nameof(offset));
        _indices = new(buffer, format, offset);
    }

    /// <summary>Selects primitive assembly, without primitive restart. The default is TriangleList.</summary>
    public void Topology(PrimitiveTopology topology)
    {
        EnsureGraphics();
        if (!Enum.IsDefined(topology))
            throw new ArgumentOutOfRangeException(nameof(topology));
        _topology = topology;
    }

    /// <summary>Draws vertices, optionally instanced, using bound attributes and/or vertex indices.</summary>
    public void Draw(int vertexCount, int instanceCount = 1)
        => Draw(vertexCount, instanceCount, 0, 0);

    /// <summary>Draws a vertex and instance range. Start values also affect gl_VertexIndex and gl_InstanceIndex.</summary>
    public void Draw(int vertexCount, int instanceCount, int firstVertex, int firstInstance)
    {
        EnsureGraphics();
        SetDrawRange(false, firstVertex, 0, firstInstance);
        SetWork(vertexCount, instanceCount, 1);
    }

    /// <summary>Draws an index range, adding vertexOffset to each index before fetching vertex data.</summary>
    public void DrawIndexed(int indexCount, int instanceCount = 1, int firstIndex = 0, int vertexOffset = 0, int firstInstance = 0)
    {
        EnsureGraphics();
        SetDrawRange(true, firstIndex, vertexOffset, firstInstance);
        SetWork(indexCount, instanceCount, 1);
    }

    private void SetDrawRange(bool indexed, int firstElement, int vertexOffset, int firstInstance)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(firstElement);
        ArgumentOutOfRangeException.ThrowIfNegative(firstInstance);
        _indexed = indexed;
        _firstElement = firstElement;
        _vertexOffset = vertexOffset;
        _firstInstance = firstInstance;
        _indirect = null;
    }

    /// <summary>Dispatches workgroups with the local size declared by the compute shader.</summary>
    public void Dispatch(int x, int y = 1, int z = 1)
    {
        EnsureCompute();
        SetWork(x, y, z);
        _threads = false;
        _indirect = null;
    }

    /// <summary>Rounds invocation counts up to the compiled local size. The shader must guard excess invocations.</summary>
    public void DispatchThreads(int x, int y = 1, int z = 1)
    {
        EnsureCompute();
        SetWork(x, y, z);
        _threads = true;
        _indirect = null;
    }

    private void SetWork(int x, int y, int z)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(x);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(y);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(z);
        _work = (x, y, z);
    }

    private void EnsureRecording() => ObjectDisposedException.ThrowIf(_sealed, this);

    private void EnsureGraphics()
    {
        EnsureRecording();
        if (_program.IsCompute)
            throw new InvalidOperationException($"Pass '{_name}' is a compute pass.");
    }

    private void EnsureCompute()
    {
        EnsureRecording();
        if (!_program.IsCompute)
            throw new InvalidOperationException($"Pass '{_name}' is a graphics pass.");
    }

    internal void Seal() => _sealed = true;

    internal ShaderPassDescription Build()
    {
        foreach (int binding in _inputRegions.Keys)
            if (!_bindings.TryGetValue(binding, out ShaderPassBinding? input) || input.Type != DescriptorType.CombinedImageSampler)
                throw new InvalidOperationException($"Pass '{_name}' has an input region without sampled-image binding {binding}.");
        if (_work is not { } work)
            throw new InvalidOperationException($"Pass '{_name}' requires a draw or dispatch.");
        ShaderColorAttachment[] attachments = _attachments.Values.OrderBy(a => a.Location).ToArray();
        if (!_program.IsCompute && attachments.Length == 0 && _depthAttachment is null)
            throw new InvalidOperationException($"Pass '{_name}' requires an attachment.");
        for (int i = 0; i < attachments.Length; i++)
        {
            if (attachments[i].Location != i)
                throw new InvalidOperationException($"Pass '{_name}' requires consecutive color locations starting at zero.");
            if (attachments[i].Load != attachments[0].Load)
                throw new NotSupportedException($"Pass '{_name}' requires a shared attachment load operation.");
            if (attachments.Take(i).Any(a => a.Image.Overlaps(attachments[i].Image))
                || _bindings.Values.Any(b => !b.Metadata && b.Resource is ShaderImage image && image.Overlaps(attachments[i].Image)))
                throw new InvalidOperationException($"Pass '{_name}' aliases attachment '{attachments[i].Image.Name}'.");
        }
        if (_indexed && _indices is null)
            throw new InvalidOperationException($"Pass '{_name}' requires an index buffer.");
        ValidateDepth();
        var draw = new ShaderDrawDescription(
            [.. _vertexBindings.OrderBy(b => b.Binding)], [.. _vertexAttributes.OrderBy(a => a.Location)],
            [.. _vertices.Values.OrderBy(v => v.Binding)], _indexed ? _indices : null,
            _topology, _firstElement, _vertexOffset, _firstInstance)
        { Indirect = _indirect };
        draw.Validate(_name, work.X, work.Y);
        ShaderRuntimeConstant[] runtimeConstants = [.. _runtimeConstants.Values.OrderBy(c => c.Kind)];
        if (runtimeConstants.Any(c => c.Offset < 0 || c.Offset % 4 != 0 || c.Offset + c.Size > _constants.Length))
            throw new InvalidOperationException($"Pass '{_name}' has runtime parameters outside its push constants.");
        return new(_name, _program, [.. _bindings.Values.OrderBy(b => b.Binding)], [.. attachments],
            _constants, _blend, work, _threads)
        {
            Draw = draw,
            DepthAttachment = _depthAttachment,
            DepthStencil = _depthStencil,
            CullMode = _cullMode,
            FrontFace = _frontFace,
            PatchControlPoints = _patchControlPoints,
            PartialRendering = _partialRendering,
            InputRegions = _inputRegions.ToImmutableDictionary(),
            RuntimeConstants = [.. runtimeConstants]
        };
    }
}

internal sealed record ShaderPassBinding(int Binding, ShaderResource Resource, DescriptorType Type,
    ResourceAccess Access, SamplerState Sampler, bool Metadata);

internal sealed record ShaderColorAttachment(int Location, ShaderImage Image, AttachmentLoadOp Load);
internal sealed record ShaderDepthAttachment(ShaderImage Image, AttachmentLoadOp Load, float ClearDepth, uint ClearStencil);

internal enum ShaderRuntimeConstantKind { ImageSize, BoundsOrigin }

/// <summary>A value resolved for each request and written over the pass's push constants at an offset.</summary>
/// <remarks>ImageSize writes an image's (pixel width, pixel height, raster scale); BoundsOrigin writes
/// <see cref="Origin"/> resolved in the complete input group's bounds.</remarks>
internal readonly record struct ShaderRuntimeConstant(ShaderRuntimeConstantKind Kind, int Offset,
    ShaderImage? Image = null, RelativePoint Origin = default)
{
    public int Size => Kind == ShaderRuntimeConstantKind.ImageSize ? 3 * sizeof(float) : 2 * sizeof(float);

    public void Write(Span<byte> constants, IReadOnlyDictionary<int, ShaderImageFrame> frames)
    {
        if (Kind == ShaderRuntimeConstantKind.ImageSize)
        {
            ShaderImageFrame frame = frames[Image!.Id];
            var value = new System.Numerics.Vector3(frame.PixelSize.Width, frame.PixelSize.Height, frame.Scale);
            MemoryMarshal.Write(constants[Offset..], in value);
        }
        else
        {
            Rect bounds = frames[0].ReferenceBounds ?? frames[0].Bounds;
            Point value = bounds.Position + Origin.ToPixels(bounds.Size);
            MemoryMarshal.Write(constants[Offset..], in value);
        }
    }
}

internal sealed record ShaderPassDescription(string Name, ShaderProgram? Program,
    ImmutableArray<ShaderPassBinding> Bindings, ImmutableArray<ShaderColorAttachment> Attachments,
    ImmutableArray<byte> Constants, BlendState Blend, (int X, int Y, int Z) Work, bool DispatchThreads)
{
    public ImmutableArray<ShaderRuntimeConstant> RuntimeConstants { get; init; } = [];
    public ShaderDrawDescription Draw { get; init; } = ShaderDrawDescription.Empty;
    public ShaderDepthAttachment? DepthAttachment { get; init; }
    public DepthStencilState DepthStencil { get; init; }
    public CullMode CullMode { get; init; }
    public FrontFace FrontFace { get; init; }
    public int PatchControlPoints { get; init; } = 3;
    public ShaderTransfer? Transfer { get; init; }
    public bool PartialRendering { get; init; }
    public ImmutableDictionary<int, ShaderInputRegion> InputRegions { get; init; } = ImmutableDictionary<int, ShaderInputRegion>.Empty;
}
