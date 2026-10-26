using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Beutl.Graphics.Backend;

namespace Beutl.Graphics.Shaders;

/// <summary>Declares bindings, attachments, parameter bytes and one draw or dispatch.</summary>
/// <remarks>Bindings refer to set zero in the shader. The compiled interface is validated before execution.</remarks>
public sealed class ShaderPassBuilder
{
    private readonly ShaderGraphBuilder _graph;
    private readonly string _name;
    private readonly ShaderProgram _program;
    private readonly Dictionary<int, ShaderPassBinding> _bindings = [];
    private readonly Dictionary<int, ShaderColorAttachment> _attachments = [];
    private ImmutableArray<byte> _constants = [];
    private BlendState _blend = BlendState.Replace;
    private (int X, int Y, int Z)? _work;
    private bool _threads;
    private bool _sealed;

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
            || !Enum.IsDefined(state.AddressU) || !Enum.IsDefined(state.AddressV))
            throw new ArgumentOutOfRangeException(nameof(sampler));
        AddBinding(binding, image, DescriptorType.CombinedImageSampler, ResourceAccess.Read, state);
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
        if (ReferenceEquals(image, _graph.Source) && (access & ResourceAccess.Write) != 0)
            throw new ArgumentException("The source image is read-only.", nameof(image));
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
        if (ReferenceEquals(image, _graph.Source))
            throw new ArgumentException("The source image is read-only.", nameof(image));
        if (!_attachments.TryAdd(location, new(location, image, load)))
            throw new ArgumentException($"Pass '{_name}' already writes location {location}.", nameof(location));
    }

    /// <summary>Copies parameter bytes. Use an explicitly laid out unmanaged struct matching the shader block.</summary>
    /// <remarks>The current backend accepts up to 128 bytes, in multiples of four. Values are not pipeline cache keys.</remarks>
    public void PushConstants<T>(T value) where T : unmanaged
    {
        EnsureRecording();
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref value, 1));
        if (bytes.Length > 128 || bytes.Length % 4 != 0)
            throw new ArgumentException("Push constants must contain at most 128 bytes in multiples of four.", nameof(value));
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

    /// <summary>Draws triangle-list vertices, optionally instanced, generated by the vertex shader.</summary>
    public void Draw(int vertexCount, int instanceCount = 1)
    {
        EnsureGraphics();
        SetWork(vertexCount, instanceCount, 1);
    }

    /// <summary>Dispatches workgroups with the local size declared by the compute shader.</summary>
    public void Dispatch(int x, int y = 1, int z = 1)
    {
        EnsureCompute();
        SetWork(x, y, z);
        _threads = false;
    }

    /// <summary>Rounds invocation counts up to the compiled local size. The shader must guard excess invocations.</summary>
    public void DispatchThreads(int x, int y = 1, int z = 1)
    {
        EnsureCompute();
        SetWork(x, y, z);
        _threads = true;
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
        if (_work is not { } work)
            throw new InvalidOperationException($"Pass '{_name}' requires a draw or dispatch.");
        ShaderColorAttachment[] attachments = _attachments.Values.OrderBy(a => a.Location).ToArray();
        if (!_program.IsCompute && attachments.Length == 0)
            throw new InvalidOperationException($"Pass '{_name}' requires a color attachment.");
        for (int i = 0; i < attachments.Length; i++)
        {
            if (attachments[i].Location != i)
                throw new InvalidOperationException($"Pass '{_name}' requires consecutive color locations starting at zero.");
            if (attachments[i].Load != attachments[0].Load)
                throw new NotSupportedException($"Pass '{_name}' requires a shared attachment load operation.");
            if (attachments.Take(i).Any(a => ReferenceEquals(a.Image, attachments[i].Image))
                || _bindings.Values.Any(b => !b.Metadata && ReferenceEquals(b.Resource, attachments[i].Image)))
                throw new InvalidOperationException($"Pass '{_name}' aliases attachment '{attachments[i].Image.Name}'.");
        }
        return new(_name, _program, [.. _bindings.Values.OrderBy(b => b.Binding)], [.. attachments],
            _constants, _blend, work, _threads);
    }
}

internal sealed record ShaderPassBinding(int Binding, ShaderResource Resource, DescriptorType Type,
    ResourceAccess Access, SamplerState Sampler, bool Metadata);

internal sealed record ShaderColorAttachment(int Location, ShaderImage Image, AttachmentLoadOp Load);

internal sealed record ShaderPassDescription(string Name, ShaderProgram Program,
    ImmutableArray<ShaderPassBinding> Bindings, ImmutableArray<ShaderColorAttachment> Attachments,
    ImmutableArray<byte> Constants, BlendState Blend, (int X, int Y, int Z) Work, bool DispatchThreads);
