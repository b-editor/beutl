using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Shaders;
using Beutl.Language;
using Beutl.NodeGraph.Composition;
using Beutl.Serialization;

namespace Beutl.NodeGraph.Nodes.Shaders;

/// <summary>Everything one pass read from its wires and settings in one evaluation.</summary>
internal sealed record ShaderPassCapture(
    string Label,
    ShaderProgram Program,
    ShaderPassReflection Reflection,
    ImmutableDictionary<string, object?> Inputs,
    ShaderPassConfiguration?[] Settings,
    ShaderGraphValue<ShaderImage>?[] Targets,
    TextureFormat Format,
    float Padding,
    float ResolutionScale);

/// <summary>What a recorded pass made available to the wires after it.</summary>
internal sealed record ShaderPassResult(IReadOnlyDictionary<string, object> Resources, ShaderImage? Depth);

/// <summary>One shader pass. Its code declares the ports: reading the declarations spares every binding by hand.</summary>
public abstract partial class ShaderPassNode : ShaderDynamicNode, IShaderPictureOwner
{
    private const string InputPrefix = "In.";
    private const string OutputPrefix = "Out.";
    private readonly ShaderErrorReports _errors;

    protected ShaderPassNode()
    {
        Padding = CreateInput<float>("Padding", ShaderPortDisplays.Get("Padding"));
        ResolutionScale = CreateInput<float>("ResolutionScale", ShaderPortDisplays.Get("ResolutionScale"));
        ResolutionScale.Property?.SetValue(1f);
        Settings = new ListInputPort<ShaderPassConfiguration>
        {
            Name = { Value = "Settings" },
            Display = { Value = ShaderPortDisplays.Get("Settings") }
        };
        Error = new NodeMonitor<string?>
        {
            Name = { Value = "Error" },
            Display = { Value = NodePortDisplays.Error },
            ContentKind = NodeMonitorContentKind.Text
        };
        _errors = new ShaderErrorReports(Error);
    }

    public override double EditorWidth => 380;

    public InputPort<float> Padding { get; }

    public InputPort<float> ResolutionScale { get; }

    public ListInputPort<ShaderPassConfiguration> Settings { get; }

    public NodeMonitor<string?> Error { get; }

    /// <summary>The name a pass goes by in the graph and in messages about it.</summary>
    private protected abstract string PassLabel { get; }

    /// <summary>The stages this node's code fills, with no entry for an optional one left empty.</summary>
    private protected abstract IReadOnlyList<(ShaderStage Stage, string Code)> GetStages();

    private protected abstract ShaderProgram CreateProgram(IReadOnlyList<(ShaderStage Stage, string Code)> stages);

    private protected abstract void RecordPass(ShaderGraphBuilder builder, string name, ShaderProgram program, Action<ShaderPassBuilder> record);

    /// <summary>Records what the pass does when no setting chooses its draw or dispatch.</summary>
    private protected abstract void RecordDefaultWork(ShaderPassBuilder pass, ShaderImage workImage);

    /// <summary>Whether the coordinates the pass reads with run across its output, where a mesh brings its own.</summary>
    private protected virtual bool ReadsAcrossItsOutput => true;

    // A saved node keeps its ports; code only needs reading when it changes.
    private protected void OnCodeChanged()
    {
        if (!IsRestoring) RefreshPorts();
    }

    /// <summary>Reads the code and shows its declarations as ports. Code that does not compile keeps the ports and connections it had.</summary>
    internal bool RefreshPorts()
    {
        ShaderPassReflection reflection;
        try
        {
            IReadOnlyList<(ShaderStage Stage, string Code)> stages = GetStages();
            reflection = ShaderPassReflection.Compile(stages);
            // Compiling each stage alone cannot tell which of them do not go together.
            CreateProgram(stages);
        }
        catch (Exception ex)
        {
            ReportError(ex.Message);
            return false;
        }

        // What a picture failed on may be gone with the code it was about; the next evaluation says if it is not.
        _errors.ReportPicture(null);
        ReportError(null);
        SyncPorts(CreatePortSpecs(reflection));
        return true;
    }

    internal void ReportError(string? message) => _errors.ReportOwn(message);

    void IShaderPictureOwner.ReportPictureFailure(string? message) => _errors.ReportPicture(message);

    private static IReadOnlyList<ShaderPortSpec> CreatePortSpecs(ShaderPassReflection reflection)
    {
        var outputs = new List<ShaderPortSpec>();
        var inputs = new List<ShaderPortSpec>();
        foreach (ShaderColorOutputLayout color in reflection.ColorOutputs)
            outputs.Add(new(OutputPrefix + color.Name, typeof(ShaderGraphValue<ShaderImage>), true, Label(color.Name)));

        foreach (ShaderDescriptorLayout descriptor in reflection.Descriptors)
        {
            Type wire = descriptor.Type is DescriptorType.StorageBuffer or DescriptorType.UniformBuffer
                ? typeof(ShaderGraphValue<ShaderBuffer>) : typeof(ShaderGraphValue<ShaderImage>);
            inputs.Add(new(InputPrefix + descriptor.Name, wire, false, Label(descriptor.Name)));
            if (descriptor.Type == DescriptorType.UniformBuffer)
            {
                foreach (ShaderValueLayout member in descriptor.Members)
                    inputs.Add(MemberPort(descriptor.Name, member));
            }
            else if ((descriptor.Access & ResourceAccess.Write) != 0)
            {
                outputs.Add(new(OutputPrefix + descriptor.Name, wire, true, Label(descriptor.Name)));
            }
        }

        foreach (ShaderValueLayout member in reflection.PushConstants)
            inputs.Add(MemberPort(reflection.PushConstantName ?? "", member));

        foreach ((ShaderSpecializationLayout layout, _) in reflection.Specializations)
        {
            if (ShaderParameterPacking.ScalarType(layout.ScalarKind, layout.BitWidth) is { } type)
                inputs.Add(new(InputPrefix + layout.Name, type, false, Label(layout.Name), ShaderParameterPacking.DefaultValue(layout)));
        }

        return [.. outputs, .. inputs];
    }

    private static ShaderPortSpec MemberPort(string block, ShaderValueLayout member)
        => new(MemberName(block, member), ShaderParameterPacking.PortType(member), false, Label(member.Name),
            ShaderParameterPacking.DefaultValue(member));

    private static string MemberName(string block, ShaderValueLayout member) => $"{InputPrefix}{block}.{member.Name}";

    private static DisplayAttribute Label(string name) => new() { Name = name };

    // A member is named after its block; its label is the name the code gives it.
    private protected override DisplayAttribute DisplayOf(string portName)
        => Label(portName[(portName.LastIndexOf('.') + 1)..]);

    public partial class Resource
    {
        private IReadOnlyList<(ShaderStage Stage, string Code)>? _stages;
        private ShaderPassReflection? _reflection;
        private string? _reflectionError;
        private ShaderProgram? _program;
        private ImmutableArray<SpecializationConstant> _constants = [];
        private ShaderPassReflection? _specialized;

        /// <summary>What the rest of the node's outputs read from, or null while the code does not compile.</summary>
        private protected ShaderGraphValue<ShaderPassResult>? Result { get; private set; }

        public override void Update(GraphCompositionContext context)
        {
            ShaderPassNode node = RequireOriginal();
            ShaderPassReflection? reflection = Reflect(node);
            string? failure = _reflectionError;
            ShaderPassCapture? capture = null;
            if (reflection != null)
            {
                try
                {
                    capture = Capture(node, context, reflection);
                }
                catch (Exception ex)
                {
                    failure = ex.Message;
                }
            }

            if (capture == null)
            {
                string error = failure!;
                if (ReadsCurrentCode(node)) node.ReportError(error);
                Result = new(_ => throw new InvalidOperationException(error));
            }
            else
            {
                Result = new(recording =>
                {
                    try
                    {
                        ShaderPassResult result = Record(node, recording, capture);
                        if (ReadsCurrentCode(node)) node.ReportError(null);
                        return result;
                    }
                    catch (Exception ex)
                    {
                        if (ReadsCurrentCode(node)) node.ReportError(ex.Message);
                        throw;
                    }
                });
            }

            ShaderGraphValue<ShaderPassResult> pass = Result;
            for (int index = 0; index < Members.Length; index++)
            {
                string name = Members[index].Name.Value;
                if (!name.StartsWith(OutputPrefix, StringComparison.Ordinal)) continue;
                switch (ItemValues[index])
                {
                    case ItemValue<ShaderGraphValue<ShaderImage>> image:
                        image.Value = new(recording => (ShaderImage)Resolve(recording, pass, name));
                        break;
                    case ItemValue<ShaderGraphValue<ShaderBuffer>> buffer:
                        buffer.Value = new(recording => (ShaderBuffer)Resolve(recording, pass, name));
                        break;
                }
            }
        }

        private static object Resolve(ShaderGraphRecordingContext recording, ShaderGraphValue<ShaderPassResult> pass, string name)
            => recording.Evaluate(pass).Resources.TryGetValue(name, out object? resource)
                ? resource
                : throw new InvalidOperationException(string.Format(NodeGraphStrings.ShaderError_NotConnected, name));

        // Rendering runs alongside editing, and an edit has already reported on code newer than this evaluation read.
        private bool ReadsCurrentCode(ShaderPassNode node) => _stages != null && node.GetStages().SequenceEqual(_stages);

        // Comparing the sources is what a frame pays for; invalid code is not compiled again until it changes.
        private ShaderPassReflection? Reflect(ShaderPassNode node)
        {
            IReadOnlyList<(ShaderStage Stage, string Code)> stages = node.GetStages();
            if (_stages == null || !stages.SequenceEqual(_stages))
            {
                _stages = stages;
                _program = null;
                _specialized = null;
                try
                {
                    _reflection = ShaderPassReflection.Compile(stages);
                    _reflectionError = null;
                }
                catch (Exception ex)
                {
                    _reflection = null;
                    _reflectionError = ex.Message;
                }
            }

            return _reflection;
        }

        private ShaderPassCapture Capture(ShaderPassNode node, GraphCompositionContext context, ShaderPassReflection reflection)
        {
            var inputs = ImmutableDictionary.CreateBuilder<string, object?>(StringComparer.Ordinal);
            for (int index = 0; index < Members.Length; index++)
            {
                if (Members[index] is IInputPort port && port.Name.Value.StartsWith(InputPrefix, StringComparison.Ordinal))
                    inputs[port.Name.Value] = ItemValues[index].GetBoxed();
            }

            ImmutableArray<SpecializationConstant> constants = ReadConstants(reflection, inputs);
            if (_program == null || !constants.AsSpan().SequenceEqual(_constants.AsSpan()))
            {
                ShaderProgram program = node.CreateProgram(_stages!);
                _program = constants.IsEmpty ? program : program.Specialize(constants.AsSpan());
                _specialized = constants.IsEmpty ? reflection : reflection.Specialize(constants);
                _constants = constants;
            }

            (TextureFormat format, ShaderGraphValue<ShaderImage>?[] targets) = ReadColorTargets(context);
            return new(node.PassLabel, _program, _specialized!, inputs.ToImmutable(),
                [.. context.CollectListInputValues(node.Settings)], targets, format, Padding, ResolutionScale);
        }

        private protected virtual (TextureFormat Format, ShaderGraphValue<ShaderImage>?[] Targets) ReadColorTargets(
            GraphCompositionContext context) => (TextureFormat.RGBA16Float, []);

        private static ImmutableArray<SpecializationConstant> ReadConstants(ShaderPassReflection reflection,
            ImmutableDictionary<string, object?>.Builder inputs)
        {
            if (reflection.Specializations.IsEmpty) return [];
            var constants = ImmutableArray.CreateBuilder<SpecializationConstant>(reflection.Specializations.Length);
            foreach ((ShaderSpecializationLayout layout, ShaderStage stages) in reflection.Specializations)
            {
                object? value = inputs.GetValueOrDefault(InputPrefix + layout.Name) ?? ShaderParameterPacking.DefaultValue(layout);
                SpecializationConstant? constant = value switch
                {
                    bool v => SpecializationConstant.Create(layout.ConstantId, v, stages),
                    int v => SpecializationConstant.Create(layout.ConstantId, v, stages),
                    uint v => SpecializationConstant.Create(layout.ConstantId, v, stages),
                    float v => SpecializationConstant.Create(layout.ConstantId, v, stages),
                    long v => SpecializationConstant.Create(layout.ConstantId, v, stages),
                    ulong v => SpecializationConstant.Create(layout.ConstantId, v, stages),
                    double v => SpecializationConstant.Create(layout.ConstantId, v, stages),
                    _ => null
                };
                if (constant is { } known) constants.Add(known);
            }

            return constants.ToImmutable();
        }

        private static ShaderPassResult Record(ShaderPassNode node, ShaderGraphRecordingContext recording, ShaderPassCapture capture)
        {
            ShaderGraphBuilder builder = recording.Builder;
            ShaderPassReflection reflection = capture.Reflection;
            var bindings = new List<Action<ShaderPassBuilder>>();
            var resources = new Dictionary<string, object>(StringComparer.Ordinal);
            ShaderGraphValue<T>? Wire<T>(string name) => capture.Inputs.GetValueOrDefault(InputPrefix + name) as ShaderGraphValue<T>;

            // What the pass reads comes first, so what it allocates can take its footprint.
            ShaderImage? firstRead = null;
            var samplers = new List<(ShaderDescriptorLayout Descriptor, ShaderImage Image, ShaderSampledImageValue? Options)>();
            foreach (ShaderDescriptorLayout descriptor in reflection.Descriptors)
            {
                if (descriptor.Type == DescriptorType.CombinedImageSampler)
                {
                    ShaderGraphValue<ShaderImage>? wire = Wire<ShaderImage>(descriptor.Name);
                    ShaderImage image = wire != null ? recording.Evaluate(wire) : builder.Source;
                    // A table of a set size says nothing about what the pass covers.
                    if (!image.Description.HasValue) firstRead ??= image;
                    samplers.Add((descriptor, image, wire as ShaderSampledImageValue));
                }
                else if (descriptor is { Type: DescriptorType.StorageImage, Access: ResourceAccess.Read })
                {
                    int binding = descriptor.Binding;
                    ShaderGraphValue<ShaderImage>? wire = Wire<ShaderImage>(descriptor.Name);
                    ShaderImage image = wire != null ? recording.Evaluate(wire) : builder.Source;
                    if (!image.Description.HasValue) firstRead ??= image;
                    bindings.Add(pass => pass.StorageImage(binding, image, ResourceAccess.Read));
                }
            }

            ShaderImage[] targets = [.. capture.Targets.Where(target => target != null).Select(target => recording.Evaluate(target!))];
            ShaderImage like = targets.FirstOrDefault() ?? firstRead ?? builder.Source;
            ShaderImage? firstWritten = null;
            foreach (ShaderDescriptorLayout descriptor in reflection.Descriptors)
            {
                int binding = descriptor.Binding;
                switch (descriptor.Type)
                {
                    case DescriptorType.StorageImage when descriptor.Access != ResourceAccess.Read:
                        {
                            ShaderGraphValue<ShaderImage>? wire = Wire<ShaderImage>(descriptor.Name);
                            ShaderImage image;
                            if (wire != null)
                            {
                                image = recording.Evaluate(wire);
                            }
                            else if (descriptor.ImageKind == ImageKind.Image2D && descriptor.ImageFormat is { } format)
                            {
                                image = Allocate(recording, capture, descriptor.Name, like, format);
                                // GLSL images read and write unless told otherwise, so what they read has to be known.
                                if ((descriptor.Access & ResourceAccess.Read) != 0) ShaderImageContents.Clear(recording, image);
                            }
                            else
                            {
                                throw new InvalidOperationException(string.Format(NodeGraphStrings.ShaderError_NotConnected, descriptor.Name));
                            }

                            firstWritten ??= image;
                            resources[OutputPrefix + descriptor.Name] = image;
                            bindings.Add(pass => pass.StorageImage(binding, image, descriptor.Access));
                            break;
                        }
                    case DescriptorType.StorageBuffer:
                        {
                            ShaderGraphValue<ShaderBuffer>? wire = Wire<ShaderBuffer>(descriptor.Name);
                            ShaderBuffer buffer;
                            if (wire != null)
                            {
                                buffer = recording.Evaluate(wire);
                            }
                            else if ((descriptor.Access & ResourceAccess.Write) != 0 && !descriptor.IsRuntimeSized)
                            {
                                string name = recording.UniqueName($"{capture.Label}.{descriptor.Name}");
                                // A block that is one array is checked against its element stride.
                                int stride = descriptor.ArrayStride > 0 ? descriptor.ArrayStride : descriptor.MinimumSize;
                                buffer = builder.Buffer(name, descriptor.MinimumSize / stride, stride);
                                if ((descriptor.Access & ResourceAccess.Read) != 0) builder.Clear(recording.UniqueName($"{name}.clear"), buffer);
                            }
                            else
                            {
                                throw new InvalidOperationException(string.Format(NodeGraphStrings.ShaderError_NotConnected, descriptor.Name));
                            }

                            if ((descriptor.Access & ResourceAccess.Write) != 0) resources[OutputPrefix + descriptor.Name] = buffer;
                            bindings.Add(pass => pass.StorageBuffer(binding, buffer, descriptor.Access));
                            break;
                        }
                    case DescriptorType.UniformBuffer:
                        {
                            switch (capture.Inputs.GetValueOrDefault(InputPrefix + descriptor.Name))
                            {
                                case ShaderImageParametersValue parameters:
                                    {
                                        ShaderImage image = recording.Evaluate(parameters.Image);
                                        bindings.Add(pass => pass.ImageParameters(binding, image));
                                        break;
                                    }
                                case ShaderGraphValue<ShaderBuffer> wire:
                                    {
                                        ShaderBuffer buffer = recording.Evaluate(wire);
                                        bindings.Add(pass => pass.UniformBuffer(binding, buffer));
                                        break;
                                    }
                                default:
                                    {
                                        byte[] bytes = ShaderParameterPacking.Pack(descriptor.Members, descriptor.MinimumSize,
                                            member => capture.Inputs.GetValueOrDefault(MemberName(descriptor.Name, member)));
                                        ShaderBuffer buffer = builder.Buffer(recording.UniqueName($"{capture.Label}.{descriptor.Name}"), bytes, 1);
                                        bindings.Add(pass => pass.UniformBuffer(binding, buffer));
                                        break;
                                    }
                            }

                            break;
                        }
                }
            }

            if (reflection.PushConstantSize > 0)
            {
                byte[] constants = ShaderParameterPacking.Pack(reflection.PushConstants, reflection.PushConstantSize,
                    member => capture.Inputs.GetValueOrDefault(MemberName(reflection.PushConstantName ?? "", member)));
                bindings.Add(pass => pass.PushConstants(constants));
            }

            var colors = new List<(int Location, ShaderImage Image)>();
            foreach (ShaderColorOutputLayout color in reflection.ColorOutputs)
            {
                ShaderImage image;
                if (color.Location < targets.Length)
                {
                    image = targets[color.Location];
                }
                else
                {
                    image = Allocate(recording, capture, color.Name, like, FormatFor(color, capture.Format));
                    // A pass that draws onto a target keeps what is there, and all its outputs load alike.
                    if (targets.Length > 0) ShaderImageContents.Clear(recording, image);
                }

                resources[OutputPrefix + color.Name] = image;
                colors.Add((color.Location, image));
            }

            // A pass told where its images lie does its own arithmetic, which a copy elsewhere would throw off.
            bool aligns = node.ReadsAcrossItsOutput && !capture.Inputs.Values.OfType<ShaderImageParametersValue>().Any();
            ShaderImage? frame = !aligns ? null : colors.Count > 0 ? colors[0].Image : firstWritten;
            foreach ((ShaderDescriptorLayout descriptor, ShaderImage image, ShaderSampledImageValue? options) in samplers)
            {
                int binding = descriptor.Binding;
                ShaderImage read = frame != null && ShaderImageAlignment.IsNeeded(image, frame)
                    ? ShaderImageAlignment.Align(recording, $"{capture.Label}.{descriptor.Name}", image, frame, options?.Sampler)
                    : image;
                bindings.Add(pass =>
                {
                    pass.SampledImage(binding, read, options?.Sampler);
                    if (options?.Region is { } region) pass.InputRegion(binding, region);
                });
            }

            Action<ShaderPassRecording>[] settings = [.. capture.Settings.Where(setting => setting != null)
                .Select(setting => setting!.Prepare(recording))];
            ShaderImage? depth = null;
            node.RecordPass(builder, recording.UniqueName(capture.Label), capture.Program, pass =>
            {
                foreach (Action<ShaderPassBuilder> bind in bindings) bind(pass);
                var recorded = new ShaderPassRecording(pass);
                foreach (Action<ShaderPassRecording> apply in settings) apply(recorded);
                AttachmentLoadOp load = recorded.Load ?? (targets.Length > 0 ? AttachmentLoadOp.Load : AttachmentLoadOp.Clear);
                foreach ((int location, ShaderImage image) in colors) pass.ColorAttachment(location, image, load);
                if (recorded.VertexBindings.Count > 0 || recorded.VertexAttributes.Count > 0)
                {
                    pass.VertexInput(new VertexInputDescription
                    {
                        Bindings = [.. recorded.VertexBindings],
                        Attributes = [.. recorded.VertexAttributes]
                    });
                }

                if (!recorded.HasWork) node.RecordDefaultWork(pass, firstWritten ?? like);
                depth = recorded.DepthAttachment;
            });
            return new(resources, depth);
        }

        private static ShaderImage Allocate(ShaderGraphRecordingContext recording, ShaderPassCapture capture, string name,
            ShaderImage like, TextureFormat format)
        {
            return ShaderImageContents.Declare(recording, $"{capture.Label}.{name}", like, format, capture.Padding, capture.ResolutionScale);
        }

        private static TextureFormat FormatFor(ShaderColorOutputLayout output, TextureFormat format) => output.ScalarKind switch
        {
            ShaderScalarKind.SignedInteger => TextureFormat.RGBA32SInt,
            ShaderScalarKind.UnsignedInteger => TextureFormat.RGBA32UInt,
            _ => format
        };
    }
}

/// <summary>Keeps <c>uv</c> reading every input at the same place, when an input does not cover what the output does.</summary>
/// <remarks>A graph's coordinates run from zero to one across each image separately, so padding an output would shrink
/// an input read with the output's coordinates. A copy that covers the output is one pass, where the alternative is
/// arithmetic in every shader and a wire from each image to each pass that reads it. A fixed-size image has no
/// position to keep, and its coordinates stay its own.</remarks>
internal static class ShaderImageAlignment
{
    private static readonly ShaderProgram s_resample = ShaderProgram.Fullscreen(new ShaderSource("""
        #version 450
        layout(location = 0) in vec2 uv;
        layout(location = 0) out vec4 color;
        layout(set = 0, binding = 0) uniform sampler2D image;
        layout(set = 0, binding = 1) uniform To { vec4 bounds; vec4 raster; vec4 size; } to;
        layout(set = 0, binding = 2) uniform From { vec4 bounds; vec4 raster; vec4 size; } from_;

        void main() {
            color = texture(image, (to.raster.xy - from_.raster.xy + uv * to.raster.zw) / from_.raster.zw);
        }
        """));

    // At one density the grids coincide, so each texel comes over whole. Filtering would let rounding in these
    // coordinates draw a trace of the picture across its edge.
    private static readonly ShaderProgram s_align = ShaderProgram.Fullscreen(new ShaderSource("""
        #version 450
        layout(location = 0) in vec2 uv;
        layout(location = 0) out vec4 color;
        layout(set = 0, binding = 0) uniform sampler2D image;
        layout(set = 0, binding = 1) uniform To { vec4 bounds; vec4 raster; vec4 size; } to;
        layout(set = 0, binding = 2) uniform From { vec4 bounds; vec4 raster; vec4 size; } from_;

        void main() {
            vec2 position = to.raster.xy - from_.raster.xy + uv * to.raster.zw;
            if (abs(to.size.z - from_.size.z) > 0.0001) {
                color = texture(image, position / from_.raster.zw);
                return;
            }

            ivec2 texel = ivec2(floor(position * from_.size.z));
            bool inside = all(greaterThanEqual(texel, ivec2(0))) && all(lessThan(texel, textureSize(image, 0)));
            color = inside ? texelFetch(image, texel, 0) : vec4(0);
        }
        """));

    private static readonly Rect s_firstProbe = new(0, 0, 1000, 1000);
    private static readonly Rect s_secondProbe = new(123, 456, 2000, 700);

    // Bounds follow the source's, which a recording does not know; two sizes tell any padding apart.
    public static bool IsNeeded(ShaderImage image, ShaderImage frame)
        => !image.Description.HasValue && !frame.Description.HasValue
           && !image.Format.IsDepthFormat() && image.Format.GetNumericType() == NumericType.Float
           && (image.ResolveBounds(s_firstProbe) != frame.ResolveBounds(s_firstProbe)
               || image.ResolveBounds(s_secondProbe) != frame.ResolveBounds(s_secondProbe));

    public static ShaderImage Align(ShaderGraphRecordingContext recording, string name, ShaderImage image, ShaderImage frame, SamplerState? sampler)
    {
        ShaderImage aligned = recording.Builder.Image2D(recording.UniqueName($"{name}.aligned"), frame, image.Format,
            resolutionScale: frame.ResolutionScale);
        // The copy lines up with the output texel for texel, whatever density the output has.
        aligned.RasterSource = frame;
        // A sampler that repeats or clamps says what lies outside the picture, which the exact copy takes to be nothing.
        bool transparentOutside = sampler is null or { AddressU: SamplerAddressMode.ClampToBorder, AddressV: SamplerAddressMode.ClampToBorder };
        recording.Builder.Fullscreen(recording.UniqueName($"{name}.align"), transparentOutside ? s_align : s_resample, pass =>
        {
            pass.SampledImage(0, image, sampler);
            pass.ImageParameters(1, aligned);
            pass.ImageParameters(2, image);
            pass.ColorAttachment(0, aligned);
        });
        return aligned;
    }
}

/// <summary>A pass that draws into color outputs.</summary>
public abstract partial class ShaderGraphicsPassNode : ShaderPassNode
{
    protected ShaderGraphicsPassNode()
    {
        Format = CreateInput<TextureFormat>("Format", ShaderPortDisplays.Get("Format"));
        Format.Property?.SetValue(TextureFormat.RGBA16Float);
        Targets = new ListInputPort<ShaderGraphValue<ShaderImage>>
        {
            Name = { Value = "Targets" },
            Display = { Value = ShaderPortDisplays.Get("Targets") }
        };
    }

    public InputPort<TextureFormat> Format { get; }

    public ListInputPort<ShaderGraphValue<ShaderImage>> Targets { get; }

    public partial class Resource
    {
        private protected override (TextureFormat Format, ShaderGraphValue<ShaderImage>?[] Targets) ReadColorTargets(
            GraphCompositionContext context)
            => (Format, [.. context.CollectListInputValues(RequireOriginal().Targets)]);
    }
}
