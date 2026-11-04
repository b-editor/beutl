using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
using Beutl.Language;

namespace Beutl.NodeGraph.Nodes.Shaders;

/// <summary>A wire value that <see cref="ShaderGraphRecordingContext"/> can record without knowing its type.</summary>
internal interface IShaderGraphValue
{
    void Record(ShaderGraphRecordingContext context);

    /// <summary>The same value, recorded only once <paramref name="prerequisites"/> have been, in their order.</summary>
    IShaderGraphValue After(IReadOnlyList<IShaderGraphValue> prerequisites);
}

/// <summary>What a shader wire carries: a recipe that declares a resource and the passes that write it.</summary>
/// <remarks>Evaluating a value evaluates the wires it depends on first, so the connections decide what is
/// recorded and in which order, and a value nothing reaches records nothing. A resource written in place
/// comes out of the writing node as a new value; read that one to see the write.</remarks>
public class ShaderGraphValue<T> : IShaderGraphValue
{
    private readonly Func<ShaderGraphRecordingContext, T> _record;

    public ShaderGraphValue(Func<ShaderGraphRecordingContext, T> record)
    {
        ArgumentNullException.ThrowIfNull(record);
        _record = record;
    }

    internal T Evaluate(ShaderGraphRecordingContext context) => _record(context);

    void IShaderGraphValue.Record(ShaderGraphRecordingContext context) => context.Evaluate(this);

    IShaderGraphValue IShaderGraphValue.After(IReadOnlyList<IShaderGraphValue> prerequisites) => After(prerequisites);

    private protected virtual ShaderGraphValue<T> After(IReadOnlyList<IShaderGraphValue> prerequisites)
        => new(context =>
        {
            foreach (IShaderGraphValue prerequisite in prerequisites)
                prerequisite.Record(context);
            return context.Evaluate(this);
        });
}

/// <summary>An image wire that also says how the pass reading it samples it.</summary>
public sealed class ShaderSampledImageValue : ShaderGraphValue<ShaderImage>
{
    private readonly ShaderGraphValue<ShaderImage> _image;

    public ShaderSampledImageValue(ShaderGraphValue<ShaderImage> image, SamplerState? sampler, ShaderInputRegion? region)
        : base(context => context.Evaluate(image))
    {
        _image = image;
        Sampler = sampler;
        Region = region;
    }

    public SamplerState? Sampler { get; }

    public ShaderInputRegion? Region { get; }

    private protected override ShaderGraphValue<ShaderImage> After(IReadOnlyList<IShaderGraphValue> prerequisites)
        => new ShaderSampledImageValue((ShaderGraphValue<ShaderImage>)((IShaderGraphValue)_image).After(prerequisites), Sampler, Region);
}

/// <summary>A uniform block's wire that fills the block with an image's frame instead of a buffer's contents.</summary>
public sealed class ShaderImageParametersValue(ShaderGraphValue<ShaderImage> image)
    : ShaderGraphValue<ShaderBuffer>(_ => throw new InvalidOperationException(NodeGraphStrings.ShaderError_ImageParametersNeedUniformBlock))
{
    public ShaderGraphValue<ShaderImage> Image { get; } = image;
}

/// <summary>One recording of the wires reaching a <see cref="ShaderApplyNode"/>.</summary>
public sealed class ShaderGraphRecordingContext
{
    private readonly Dictionary<object, object?> _values = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<object> _recording = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, int> _names = new(StringComparer.Ordinal);
    private readonly Dictionary<RenderNode, ShaderImage> _externalImages = new(ReferenceEqualityComparer.Instance);

    public ShaderGraphRecordingContext(ShaderGraphBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        Builder = builder;
    }

    public ShaderGraphBuilder Builder { get; }

    /// <summary>The pictures the graph reads besides the source, in the order it declared them.</summary>
    internal IReadOnlyCollection<RenderNode> ExternalImages => _externalImages.Keys;

    /// <summary>The image holding what <paramref name="node"/> renders, one however often it is read.</summary>
    internal ShaderImage ExternalImage(RenderNode node)
    {
        if (!_externalImages.TryGetValue(node, out ShaderImage? image))
        {
            image = Builder.ExternalImage(UniqueName("External"));
            _externalImages.Add(node, image);
        }

        return image;
    }

    /// <summary>A resource or pass name not yet used. A graph repeats names whenever a loop records a node again.</summary>
    public string UniqueName(string name)
    {
        int count = _names.GetValueOrDefault(name);
        _names[name] = count + 1;
        return count == 0 ? name : $"{name}#{count}";
    }

    public T Evaluate<T>(ShaderGraphValue<T> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (_values.TryGetValue(value, out object? result)) return (T)result!;
        if (!_recording.Add(value))
            throw new InvalidOperationException(NodeGraphStrings.ShaderError_Cycle);
        try
        {
            T recorded = value.Evaluate(this);
            _values.Add(value, recorded);
            return recorded;
        }
        finally
        {
            _recording.Remove(value);
        }
    }

    /// <summary>The value of a wire a node cannot work without.</summary>
    public T Require<T>(ShaderGraphValue<T>? value, string portName)
        => value == null
            ? throw new InvalidOperationException(string.Format(NodeGraphStrings.ShaderError_NotConnected, portName))
            : Evaluate(value);

    internal static ShaderGraphDescription Build(ShaderGraphValue<ShaderImage> image, ColorEncoding color)
        => Build(image, color, out _);

    internal static ShaderGraphDescription Build(ShaderGraphValue<ShaderImage> image, ColorEncoding color,
        out IReadOnlyList<RenderNode> externalImages)
    {
        ArgumentNullException.ThrowIfNull(image);
        ShaderGraphRecordingContext? recording = null;
        ShaderGraphDescription description = ShaderGraphBuilder.Build((Image: image, Color: color), (builder, state) =>
        {
            recording = new ShaderGraphRecordingContext(builder);
            return builder.Output(recording.Evaluate(state.Image), state.Color);
        });
        externalImages = [.. recording!.ExternalImages];
        return description;
    }
}

/// <summary>What a settings node contributes to one pass, once the wires it reads are recorded.</summary>
/// <remarks>A builder rejects declaring resources inside a pass, so the wires are read first.</remarks>
public sealed class ShaderPassConfiguration(Func<ShaderGraphRecordingContext, Action<ShaderPassRecording>> prepare)
{
    internal Action<ShaderPassRecording> Prepare(ShaderGraphRecordingContext context) => prepare(context);
}

/// <summary>The pass being recorded, with what its settings add up to.</summary>
public sealed class ShaderPassRecording(ShaderPassBuilder pass)
{
    public ShaderPassBuilder Pass { get; } = pass;

    /// <summary>Whether a setting chose the pass's draw or dispatch.</summary>
    public bool HasWork { get; set; }

    public ShaderImage? DepthAttachment { get; set; }

    /// <summary>What the color outputs start from, when a setting decides that.</summary>
    public AttachmentLoadOp? Load { get; set; }

    public List<VertexBindingDescription> VertexBindings { get; } = [];

    public List<VertexAttributeDescription> VertexAttributes { get; } = [];
}
