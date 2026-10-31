using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;

namespace Beutl.Graphics.Shaders;

/// <summary>
/// Reads the interface of compiler-produced SPIR-V. This does not parse or restrict shader expressions.
/// Unsupported resource shapes are diagnosed before a device pipeline is created.
/// </summary>
internal sealed class ShaderInterface
{
    private readonly Dictionary<uint, (ushort Op, uint[] Args)> _types = [];
    private readonly Dictionary<(uint Id, uint Decoration), uint> _decorations = [];
    private readonly Dictionary<(uint Id, uint Member, uint Decoration), uint> _members = [];
    private readonly Dictionary<uint, ulong> _constants = [];
    private readonly List<(uint Type, uint Id, uint Storage)> _variables = [];
    private readonly Dictionary<(uint Location, uint Component, bool Patch), string> _inputs = [];
    private readonly Dictionary<(uint Location, uint Component, bool Patch), uint> _inputTypes = [];
    private readonly Dictionary<(uint Location, uint Component, bool Patch), string> _outputs = [];

    internal sealed record Binding(int Slot, DescriptorType Type, int MinimumSize, int ArrayStride,
        bool ReadOnly, TextureFormat? ImageFormat, ImageKind? Kind, NumericType? NumericType, bool Comparison);

    public List<Binding> Bindings { get; } = [];
    public HashSet<int> ColorLocations { get; } = [];
    private readonly Dictionary<int, NumericType> _colorCategories = [];
    public Dictionary<uint, uint> ExecutionModes { get; } = [];
    public HashSet<uint> Capabilities { get; } = [];
    public (uint X, uint Y, uint Z) LocalSize { get; private set; } = (1, 1, 1);
    public int PushConstantSize { get; private set; }
    public int SharedMemorySize { get; private set; }

    public ShaderInterface(byte[] bytecode, ShaderStage stage,
        ImmutableArray<SpecializationConstant> specializationConstants = default)
    {
        ReadOnlySpan<uint> words = MemoryMarshal.Cast<byte, uint>(bytecode);
        if (words.Length < 5 || words[0] != 0x07230203)
            throw new InvalidOperationException("The compiler returned invalid SPIR-V.");
        var composites = new Dictionary<uint, uint[]>();
        var specializations = (specializationConstants.IsDefault ? [] : specializationConstants)
            .Where(c => (c.Stages & stage) != 0).ToDictionary(c => c.ConstantId);
        uint[]? localSizeIds = null;
        Span<byte> scalar = stackalloc byte[sizeof(ulong)];
        for (int offset = 5; offset < words.Length;)
        {
            int length = (int)(words[offset] >> 16);
            ushort op = (ushort)words[offset];
            if (length <= 0 || length > words.Length - offset)
                throw new InvalidOperationException("The compiler returned a truncated SPIR-V instruction.");
            uint[] args = words.Slice(offset + 1, length - 1).ToArray();
            switch (op)
            {
                case 16 when args[1] == 17: // OpExecutionMode LocalSize
                    LocalSize = (args[2], args[3], args[4]);
                    break;
                case 16: // OpExecutionMode
                    ExecutionModes[args[1]] = args.Length > 2 ? args[2] : 1;
                    break;
                case 17: // OpCapability
                    Capabilities.Add(args[0]);
                    break;
                case >= 19 and <= 32: // type declarations
                    _types.Add(args[0], (op, args[1..]));
                    break;
                case 41 or 42 or 43: // OpConstantTrue, OpConstantFalse, OpConstant
                case 48 or 49 or 50: // OpSpecConstantTrue, OpSpecConstantFalse, OpSpecConstant
                    ulong value = op is 41 or 48 ? 1u : op is 42 or 49 ? 0u
                        : args.Length > 3 ? args[2] | ((ulong)args[3] << 32) : args[2];
                    if (op is 48 or 49 or 50 && _decorations.TryGetValue((args[1], 1u), out uint constantId)
                        && specializations.TryGetValue(constantId, out SpecializationConstant constant))
                    {
                        if (constant.SizeInBytes != SizeOf(args[0]))
                            throw new ArgumentException($"Specialization constant {constantId} in {stage} requires {SizeOf(args[0])} bytes; supplied {constant.SizeInBytes}.");
                        constant.CopyValueTo(scalar);
                        value = constant.SizeInBytes == sizeof(ulong) ? BitConverter.ToUInt64(scalar) : BitConverter.ToUInt32(scalar);
                    }
                    _constants.Add(args[1], value);
                    break;
                case 44 or 51: // OpConstantComposite / OpSpecConstantComposite (including WorkgroupSize)
                    composites.Add(args[1], args[2..]);
                    break;
                case 59: // OpVariable
                    _variables.Add((args[0], args[1], args[2]));
                    break;
                case 71: // OpDecorate
                    _decorations[(args[0], args[1])] = args.Length > 2 ? args[2] : 0;
                    break;
                case 72: // OpMemberDecorate
                    _members[(args[0], args[1], args[2])] = args.Length > 3 ? args[3] : 0;
                    break;
                case 331 when args[1] == 38: // OpExecutionModeId LocalSizeId
                    localSizeIds = args[2..];
                    break;
            }
            offset += length;
        }
        if (localSizeIds is { } ids)
            LocalSize = (Constant(ids[0]), Constant(ids[1]), Constant(ids[2]));
        foreach ((uint id, uint[] values) in composites)
        {
            if (_decorations.GetValueOrDefault((id, 11u), uint.MaxValue) == 25)
                LocalSize = (Constant(values[0]), Constant(values[1]), Constant(values[2]));
        }
        foreach ((uint pointer, uint id, uint storage) in _variables)
        {
            uint type = _types[pointer].Args[1];
            if (storage == 9) // PushConstant
            {
                PushConstantSize = Math.Max(PushConstantSize, SizeOf(type));
                continue;
            }
            if (storage == 4) // Workgroup
            {
                SharedMemorySize = checked(SharedMemorySize + SizeOf(type));
                continue;
            }
            if (storage == 1 || storage == 3 && stage is not (ShaderStage.Fragment or ShaderStage.Compute))
            {
                bool perVertexArray = storage == 1 && stage is ShaderStage.Geometry or ShaderStage.TessellationControl or ShaderStage.TessellationEvaluation
                    || storage == 3 && stage == ShaderStage.TessellationControl;
                if (perVertexArray && !_decorations.ContainsKey((id, 15)) && _types[type].Op == 28)
                    type = _types[type].Args[0];
                ReadStageVariables(type, id, storage == 1 ? _inputs : _outputs);
                continue;
            }
            if (storage == 3 && stage == ShaderStage.Fragment
                && _decorations.TryGetValue((id, 30), out uint location))
            {
                if (NumericTypeOf(type) is not { } colorType
                    || _decorations.GetValueOrDefault((id, 31u)) != 0
                    || _decorations.GetValueOrDefault((id, 32u)) != 0)
                    throw new NotSupportedException($"Color location {location}: a 32-bit scalar or vector output without component packing or dual-source blending is required.");
                ColorLocations.Add(checked((int)location));
                _colorCategories.Add((int)location, colorType);
                continue;
            }
            if (storage is not (0 or 2 or 12))
                continue;
            if (!_decorations.TryGetValue((id, 33), out uint slot)
                || _decorations.GetValueOrDefault((id, 34u), uint.MaxValue) != 0)
                throw new NotSupportedException("Shader graph resources must declare a binding in descriptor set zero.");
            (ushort op, uint[] args) = _types[type];
            if (op is 28 or 29)
                throw new NotSupportedException($"Binding {slot}: descriptor arrays are not supported yet.");
            DescriptorType descriptor;
            int size = 0;
            int stride = 0;
            bool readOnly = true;
            TextureFormat? imageFormat = null;
            ImageKind? kind = null;
            NumericType? numericType = null;
            bool comparison = false;
            if (storage == 0 && op is 25 or 27)
            {
                uint[] image = op == 27 ? _types[args[0]].Args : args;
                if (image[4] != 0)
                    throw new NotSupportedException($"Binding {slot}: multisampled images are not supported.");
                kind = (image[1], image[3]) switch
                {
                    (1, 0) => ImageKind.Image2D,
                    (1, 1) => ImageKind.Image2DArray,
                    (2, 0) => ImageKind.Image3D,
                    (3, 0) => ImageKind.Cube,
                    (3, 1) => ImageKind.CubeArray,
                    _ => throw new NotSupportedException($"Binding {slot}: unsupported image dimensionality."),
                };
                numericType = NumericTypeOf(image[0]);
                comparison = image[2] == 1;
                if (numericType is null)
                    throw new NotSupportedException($"Binding {slot}: image components must use 32-bit float or integer types.");
                descriptor = op == 27 ? DescriptorType.CombinedImageSampler : DescriptorType.StorageImage;
                if (descriptor == DescriptorType.StorageImage)
                {
                    // SPIR-V Image Format operands map to Vulkan image view formats, not texel byte size.
                    imageFormat = image[6] switch
                    {
                        1 => TextureFormat.RGBA32Float,
                        2 => TextureFormat.RGBA16Float,
                        3 => TextureFormat.R32Float,
                        4 => TextureFormat.RGBA8Unorm,
                        6 => TextureFormat.RG32Float,
                        21 => TextureFormat.RGBA32SInt,
                        24 => TextureFormat.R32SInt,
                        30 => TextureFormat.RGBA32UInt,
                        33 => TextureFormat.R32UInt,
                        9 => TextureFormat.R16Float,
                        15 => TextureFormat.R8Unorm,
                        _ => throw new NotSupportedException($"Binding {slot}: storage images require an explicit format supported by graph images."),
                    };
                    readOnly = _decorations.ContainsKey((id, 24)) || _decorations.ContainsKey((type, 24));
                }
            }
            else if (storage == 12 || storage == 2 && _decorations.ContainsKey((type, 3)))
            {
                descriptor = DescriptorType.StorageBuffer;
                size = SizeOf(type);
                readOnly = _decorations.ContainsKey((id, 24)) || _decorations.ContainsKey((type, 24))
                    || args.Select((_, member) => _members.ContainsKey((type, (uint)member, 24))).All(v => v);
                // A flat array block can be checked against the declared element stride. Struct/header
                // layouts still have their minimum byte length checked; their unmanaged layout is explicit.
                if (args.Length == 1 && _types[args[0]].Op is 28 or 29)
                    stride = checked((int)_decorations[(args[0], 6)]);
            }
            else if (storage == 2 && _decorations.ContainsKey((type, 2)))
            {
                descriptor = DescriptorType.UniformBuffer;
                size = SizeOf(type);
            }
            else
                throw new NotSupportedException($"Binding {slot}: this shader resource type is not supported yet.");
            Bindings.Add(new(checked((int)slot), descriptor, size, stride, readOnly, imageFormat, kind, numericType, comparison));
        }
    }

    private uint Constant(uint id) => checked((uint)_constants[id]);

    private void ReadStageVariables(uint type, uint id, Dictionary<(uint, uint, bool), string> variables)
    {
        if (_decorations.ContainsKey((id, 11))) // BuiltIn
            return;
        uint location = _decorations.GetValueOrDefault((id, 30u), uint.MaxValue);
        uint component = _decorations.GetValueOrDefault((id, 31u));
        (ushort op, uint[] args) = _types[type];
        if (op == 30 && _decorations.ContainsKey((type, 2))) // interface block
        {
            for (uint member = 0; member < args.Length; member++)
            {
                if (_members.ContainsKey((type, member, 11)))
                    continue;
                location = _members.GetValueOrDefault((type, member, 30u), location);
                Add(args[member], location, _members.GetValueOrDefault((type, member, 31u), component),
                    _members.ContainsKey((type, member, 15)) || _decorations.ContainsKey((id, 15)));
                location = checked(location + LocationCount(args[member]));
            }
        }
        else
            Add(type, location, component, _decorations.ContainsKey((id, 15)));

        void Add(uint valueType, uint valueLocation, uint valueComponent, bool patch)
        {
            if (valueLocation == uint.MaxValue)
                throw new NotSupportedException("Shader stage inputs and outputs must declare a location or a built-in.");
            variables.Add((valueLocation, valueComponent, patch), InterfaceType(valueType));
            if (ReferenceEquals(variables, _inputs))
                _inputTypes.Add((valueLocation, valueComponent, patch), valueType);
        }
    }

    // SPIR-V IDs are local to a module; compare structural types across independently compiled stages.
    private string InterfaceType(uint type)
    {
        (ushort op, uint[] args) = _types[type];
        return op switch
        {
            21 => $"int{args[0]}:{args[1]}",
            22 => $"float{args[0]}",
            23 or 24 => $"{op}({InterfaceType(args[0])},{args[1]})",
            28 => $"array({InterfaceType(args[0])},{_constants[args[1]]})",
            30 => $"struct({string.Join(",", args.Select(InterfaceType))})",
            _ => throw new NotSupportedException($"Unsupported shader stage interface type (SPIR-V opcode {op})."),
        };
    }

    private uint LocationCount(uint type)
    {
        (ushort op, uint[] args) = _types[type];
        return checked(op switch
        {
            21 or 22 => 1u,
            23 => (_types[args[0]].Args[0] * args[1] + 127) / 128,
            24 => LocationCount(args[0]) * args[1],
            28 => LocationCount(args[0]) * Constant(args[1]),
            30 => args.Aggregate(0u, (count, member) => checked(count + LocationCount(member))),
            _ => throw new NotSupportedException($"Unsupported shader stage interface type (SPIR-V opcode {op})."),
        });
    }

    // Shared by renderer preflight and user-script validation; this requires no device resources.
    internal static void ValidateProgram(IReadOnlyDictionary<ShaderStage, ShaderInterface> interfaces,
        ShaderPassDescription pass)
    {
        var writableBindings = interfaces.Values.SelectMany(shader => shader.Bindings)
            .Where(binding => !binding.ReadOnly).Select(binding => binding.Slot).ToHashSet();
        foreach ((ShaderStage stage, ShaderInterface shader) in interfaces)
            shader.Validate(pass, stage, writableBindings);
        if (pass.Program!.IsCompute)
            return;
        ShaderInterface previous = interfaces[ShaderStage.Vertex];
        foreach (ShaderStage stage in new[] { ShaderStage.TessellationControl, ShaderStage.TessellationEvaluation, ShaderStage.Geometry, ShaderStage.Fragment })
        {
            if (!interfaces.TryGetValue(stage, out ShaderInterface? current)) continue;
            current.ValidateInputs(previous, pass.Name);
            previous = current;
        }
    }

    public void ValidateInputs(ShaderInterface vertex, string passName)
    {
        foreach ((var location, string type) in _inputs)
        {
            if (!vertex._outputs.TryGetValue(location, out string? outputType) || outputType != type)
                throw new ArgumentException($"Pass '{passName}': stage input at location {location.Location}, component {location.Component} does not match the preceding stage output.");
        }
    }

    private void ValidateVertexInputs(ShaderPassDescription pass)
    {
        foreach ((var location, uint type) in _inputTypes)
            Validate(type, location.Location);

        void Validate(uint type, uint location)
        {
            (ushort op, uint[] args) = _types[type];
            if (op is 24 or 28) // matrix columns and array elements occupy consecutive locations
            {
                uint count = op == 24 ? args[1] : Constant(args[1]);
                for (uint i = 0; i < count; i++)
                    Validate(args[0], checked(location + i * LocationCount(args[0])));
                return;
            }
            NumericType? numericType = NumericTypeOf(type);
            if (!pass.Draw.Attributes.Any(a => a.Location == location))
                throw new ArgumentException($"Pass '{pass.Name}': missing vertex attribute at location {location}.");
            VertexAttributeDescription attribute = pass.Draw.Attributes.First(a => a.Location == location);
            // Vulkan fills missing vector components with (0,0,0,1) and discards unused components.
            // Numeric type and bit width must match, including component-packed shader inputs.
            if (numericType is null || attribute.Format.GetNumericType() != numericType)
                throw new ArgumentException($"Pass '{pass.Name}': vertex attribute at location {location} does not match the shader's numeric type.");
        }
    }

    // A 32-bit scalar, or a vector of up to four of them.
    private NumericType? NumericTypeOf(uint type)
    {
        (ushort op, uint[] args) = _types[type];
        if (op == 23 && args[1] <= 4) (op, args) = _types[args[0]];
        if (args.Length == 0 || args[0] != 32)
            return null;
        return op switch
        {
            22 => NumericType.Float,
            21 => args[1] == 1 ? NumericType.SInt : NumericType.UInt,
            _ => null,
        };
    }

    private int SizeOf(uint type, int matrixStride = 0, bool rowMajor = false)
    {
        (ushort op, uint[] args) = _types[type];
        return checked(op switch
        {
            20 => 4,
            21 or 22 => (int)args[0] / 8,
            23 => SizeOf(args[0]) * (int)args[1],
            24 when matrixStride > 0 => matrixStride * (int)(rowMajor ? _types[args[0]].Args[1] : args[1]),
            24 => SizeOf(args[0]) * (int)args[1],
            28 => (int)_decorations.GetValueOrDefault((type, 6u), (uint)SizeOf(args[0], matrixStride, rowMajor)) * (int)_constants[args[1]],
            29 => 0,
            30 => StructSize(type, args),
            _ => throw new NotSupportedException($"Unsupported shader buffer member type (SPIR-V opcode {op})."),
        });
    }

    private int StructSize(uint type, uint[] members)
    {
        int size = 0;
        for (uint i = 0; i < members.Length; i++)
        {
            int offset = checked((int)_members.GetValueOrDefault((type, i, 35u), (uint)size));
            int memberSize = SizeOf(members[i], checked((int)_members.GetValueOrDefault((type, i, 7u))),
                _members.ContainsKey((type, i, 4)));
            size = Math.Max(size, checked(offset + memberSize));
        }
        return size;
    }

    public void Validate(ShaderPassDescription pass, ShaderStage stage, IReadOnlySet<int>? writableBindings = null)
    {
        if (stage == ShaderStage.Vertex)
            ValidateVertexInputs(pass);
        if (PushConstantSize > VulkanPipeline3D.MaxPushConstantsSize || pass.Constants.Length < PushConstantSize)
            throw new ArgumentException($"Pass '{pass.Name}' requires {PushConstantSize} push-constant bytes; supplied {pass.Constants.Length}.");
        foreach (Binding declared in Bindings)
        {
            ShaderPassBinding? bound = pass.Bindings.FirstOrDefault(b => b.Binding == declared.Slot);
            if (bound is null || bound.Type != declared.Type)
                throw new ArgumentException($"Pass '{pass.Name}', binding {declared.Slot}: expected {declared.Type}.");
            if (bound.Metadata && declared.MinimumSize > ShaderImageFrame.ParameterSize)
                throw new ArgumentException($"Pass '{pass.Name}', binding {declared.Slot}: image parameters contain three vec4 values.");
            if (declared.ImageFormat is { } format && bound.Resource is ShaderImage image && image.Format != format)
                throw new ArgumentException($"Pass '{pass.Name}', binding {declared.Slot}: shader image format {format} does not match {image.Format}.");
            if (declared.Kind is { } imageKind && bound.Resource is ShaderImage sampled
                && (sampled.Kind != imageKind || sampled.Format.GetNumericType() != declared.NumericType
                    || declared.Type == DescriptorType.CombinedImageSampler
                        && (declared.Comparison != bound.Sampler.Compare.HasValue || declared.Comparison && !sampled.Format.IsDepthFormat())))
                throw new ArgumentException($"Pass '{pass.Name}', binding {declared.Slot}: image dimensions, numeric type or depth comparison do not match the shader.");
            if (bound.Resource is ShaderBuffer buffer)
            {
                if (buffer.ByteLength < declared.MinimumSize || declared.ArrayStride != 0 && buffer.Stride != declared.ArrayStride)
                    throw new ArgumentException($"Pass '{pass.Name}', binding {declared.Slot}: buffer size or element stride does not match the shader.");
            }
            if (declared.Type is DescriptorType.StorageBuffer or DescriptorType.StorageImage)
            {
                string kind = declared.Type == DescriptorType.StorageBuffer ? "buffer" : "image";
                if (!declared.ReadOnly && (bound.Access & ResourceAccess.Write) == 0)
                    throw new ArgumentException($"Pass '{pass.Name}', binding {declared.Slot}: declare a readonly {kind} in the shader or a writable binding in the graph.");
                if (declared.ReadOnly && (bound.Access & ResourceAccess.Write) != 0
                    && writableBindings?.Contains(declared.Slot) != true)
                    throw new ArgumentException($"Pass '{pass.Name}', binding {declared.Slot}: a readonly {kind} cannot have write access in the graph.");
            }
        }
        if (stage == ShaderStage.Fragment)
            foreach (ShaderColorAttachment attachment in pass.Attachments)
                if (_colorCategories.TryGetValue(attachment.Location, out NumericType category) && category != attachment.Image.Format.GetNumericType())
                    throw new ArgumentException($"Pass '{pass.Name}': color location {attachment.Location} does not match the attachment's numeric type.");
        if (stage == ShaderStage.Fragment
            && !ColorLocations.SetEquals(Enumerable.Range(0, pass.Attachments.Length)))
            throw new ArgumentException($"Pass '{pass.Name}': fragment output locations do not match the color attachments.");
    }
}
