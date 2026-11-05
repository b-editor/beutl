using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;

namespace Beutl.Graphics.Shaders;

/// <summary>The scalar interpretation of a reflected shader value.</summary>
internal enum ShaderScalarKind { Boolean, SignedInteger, UnsignedInteger, FloatingPoint, Aggregate }

/// <summary>A declared value and its actual SPIR-V memory layout.</summary>
internal sealed record ShaderValueLayout(
    string Name, ShaderScalarKind ScalarKind, int BitWidth, int Rows, int Columns,
    int Offset, int Size, int ArrayLength, int ArrayStride, int MatrixStride, bool RowMajor,
    ImmutableArray<ShaderValueLayout> Members);

/// <summary>A shader descriptor, with members for buffers and a stable numeric binding.</summary>
internal sealed record ShaderDescriptorLayout(
    string Name, int Binding, DescriptorType Type, int MinimumSize, bool ReadOnly,
    ImmutableArray<ShaderValueLayout> Members)
{
    public ResourceAccess Access { get; init; }

    /// <summary>The element stride of a buffer that is one flat array, whose length the shader leaves open.</summary>
    public int ArrayStride { get; init; }

    /// <summary>Whether the block's length is decided by the buffer bound to it.</summary>
    public bool IsRuntimeSized { get; init; }

    /// <summary>The format a storage image declares, which the bound image must have.</summary>
    public TextureFormat? ImageFormat { get; init; }

    public ImageKind? ImageKind { get; init; }
}

/// <summary>A fragment output and the color location it writes.</summary>
internal sealed record ShaderColorOutputLayout(string Name, int Location, ShaderScalarKind ScalarKind);

/// <summary>A declared specialization constant and the value it has when nothing specializes it.</summary>
internal sealed record ShaderSpecializationLayout(
    string Name, uint ConstantId, ShaderScalarKind ScalarKind, int BitWidth, ulong DefaultBits);

/// <summary>Authoring metadata from the same interface reader used by renderer validation.</summary>
internal sealed record ShaderAuthoringInterface(
    ShaderStage Stage, ImmutableArray<ShaderDescriptorLayout> Descriptors,
    string? PushConstantName, ImmutableArray<ShaderValueLayout> PushConstants, int PushConstantSize,
    ImmutableArray<ShaderColorOutputLayout> ColorOutputs,
    ImmutableArray<ShaderSpecializationLayout> SpecializationConstants)
{
    /// <summary>Compiles declarations without requiring a GPU or removing unused authored inputs.</summary>
    public static ShaderAuthoringInterface Compile(ShaderSource source, ShaderStage stage,
        ImmutableArray<SpecializationConstant> specializationConstants = default)
        => Read(CompileSpirv(source, stage), stage, specializationConstants);

    /// <summary>The bytes <see cref="Compile"/> reads, to read them again under other specialization values.</summary>
    public static byte[] CompileSpirv(ShaderSource source, ShaderStage stage)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var compiler = new VulkanShaderCompiler(preserveInterface: true);
        return compiler.CompileToSpirv(source.Text, stage);
    }

    public static ShaderAuthoringInterface Read(byte[] spirv, ShaderStage stage,
        ImmutableArray<SpecializationConstant> specializationConstants = default)
    {
        ArgumentNullException.ThrowIfNull(spirv);
        return new ShaderInterface(spirv, stage, specializationConstants).GetAuthoringInterface(stage);
    }
}

internal sealed partial class ShaderInterface
{
    private readonly Dictionary<uint, string> _names = [];
    private readonly Dictionary<(uint, uint), string> _memberNames = [];
    private readonly List<(uint ConstantId, uint Type, uint Id, ulong Bits)> _specializationDefaults = [];

    private static string ReadName(ReadOnlySpan<uint> words)
    {
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(words);
        int end = bytes.IndexOf((byte)0);
        return Encoding.UTF8.GetString(end < 0 ? bytes : bytes[..end]);
    }

    internal ShaderAuthoringInterface GetAuthoringInterface(ShaderStage stage)
    {
        var descriptors = ImmutableArray.CreateBuilder<ShaderDescriptorLayout>();
        var constants = ImmutableArray.CreateBuilder<ShaderValueLayout>();
        var outputs = ImmutableArray.CreateBuilder<ShaderColorOutputLayout>();
        string? pushConstantName = null;
        foreach (var (pointer, id, storage) in _variables)
        {
            uint type = _types[pointer].Args[1];
            if (storage == 9)
            {
                pushConstantName = VariableName(id, type, "constants");
                constants.AddRange(ReadMembers(type));
            }
            else if (storage == 3 && stage == ShaderStage.Fragment && _decorations.TryGetValue((id, 30u), out uint location))
            {
                outputs.Add(new(VariableName(id, type, $"output{location}"), checked((int)location), ScalarKindOf(type)));
            }
            else if (_decorations.TryGetValue((id, 33u), out uint slot))
            {
                Binding binding = Bindings.Single(b => b.Slot == slot);
                bool writeOnly = _decorations.ContainsKey((id, 25u)) || _decorations.ContainsKey((type, 25u))
                    || _types[type].Op == 30 && _types[type].Args.Length > 0
                    && _types[type].Args.Select((_, member) => _members.ContainsKey((type, (uint)member, 25u))).All(v => v);
                descriptors.Add(new(VariableName(id, type, $"binding{slot}"), binding.Slot,
                    binding.Type, binding.MinimumSize, binding.ReadOnly,
                    _types[type].Op == 30 ? ReadMembers(type) : [])
                {
                    Access = binding.ReadOnly ? ResourceAccess.Read : writeOnly ? ResourceAccess.Write : ResourceAccess.ReadWrite,
                    ArrayStride = binding.ArrayStride,
                    IsRuntimeSized = _types[type].Op == 30 && _types[type].Args.Any(member => _types[member].Op == 29),
                    ImageFormat = binding.ImageFormat,
                    ImageKind = binding.Kind
                });
            }
        }

        var specializations = _specializationDefaults
            .Select(c => new ShaderSpecializationLayout(
                _names.GetValueOrDefault(c.Id, $"constant{c.ConstantId}"), c.ConstantId,
                ScalarKindOf(c.Type), BitWidthOf(c.Type), c.Bits))
            .ToImmutableArray();
        return new(stage, descriptors.ToImmutable(), pushConstantName, constants.ToImmutable(), PushConstantSize,
            outputs.ToImmutable(), specializations);
    }

    // A block without an instance name keeps its name on the type.
    private string VariableName(uint id, uint type, string fallback)
    {
        string? name = _names.GetValueOrDefault(id);
        if (string.IsNullOrEmpty(name)) name = _names.GetValueOrDefault(type);
        return string.IsNullOrEmpty(name) ? fallback : name;
    }

    private ShaderScalarKind ScalarKindOf(uint type)
    {
        (ushort op, uint[] args) = _types[type];
        if (op is 23 or 24 or 28 or 29) return ScalarKindOf(args[0]);
        return op switch
        {
            20 => ShaderScalarKind.Boolean,
            21 => args[1] == 0 ? ShaderScalarKind.UnsignedInteger : ShaderScalarKind.SignedInteger,
            22 => ShaderScalarKind.FloatingPoint,
            _ => ShaderScalarKind.Aggregate
        };
    }

    private int BitWidthOf(uint type)
    {
        (ushort op, uint[] args) = _types[type];
        return op switch
        {
            20 => 32,
            21 or 22 => checked((int)args[0]),
            _ => 0
        };
    }

    private ImmutableArray<ShaderValueLayout> ReadMembers(uint type)
    {
        var result = ImmutableArray.CreateBuilder<ShaderValueLayout>();
        uint[] members = _types[type].Args;
        for (uint index = 0; index < members.Length; index++)
        {
            int offset = checked((int)_members.GetValueOrDefault((type, index, 35u)));
            int stride = checked((int)_members.GetValueOrDefault((type, index, 7u)));
            bool rowMajor = _members.ContainsKey((type, index, 4u));
            result.Add(ReadValue(members[index], _memberNames.GetValueOrDefault((type, index), $"member{index}"),
                offset, stride, rowMajor));
        }
        return result.ToImmutable();
    }

    private ShaderValueLayout ReadValue(uint type, string name, int offset, int matrixStride, bool rowMajor)
    {
        uint element = type;
        int length = 0;
        int arrayStride = 0;
        if (_types[element].Op is 28 or 29)
        {
            var array = _types[element];
            length = array.Op == 28 ? checked((int)Constant(array.Args[1])) : -1;
            arrayStride = checked((int)_decorations.GetValueOrDefault((element, 6u)));
            element = array.Args[0];
        }
        int columns = 1;
        int rows = 1;
        uint scalar = element;
        if (_types[scalar].Op == 24)
        {
            columns = checked((int)_types[scalar].Args[1]);
            scalar = _types[scalar].Args[0];
        }
        if (_types[scalar].Op == 23)
        {
            rows = checked((int)_types[scalar].Args[1]);
            scalar = _types[scalar].Args[0];
        }
        return new(name, ScalarKindOf(scalar), BitWidthOf(scalar),
            rows, columns, offset, SizeOf(type, matrixStride, rowMajor), length, arrayStride, matrixStride,
            rowMajor, _types[element].Op == 30 ? ReadMembers(element) : []);
    }
}
