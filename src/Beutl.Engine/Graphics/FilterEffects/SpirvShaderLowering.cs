using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.Graphics.Effects;

/// <summary>
/// Describes an engine-authored Vulkan fragment program that is compiled to SPIR-V for one
/// <see cref="ShaderDescription"/>.
/// </summary>
/// <remarks>
/// The first native increment accepts one CurrentPixel input texture at descriptor binding 0 and maps scalar or
/// vector uniforms to explicitly offset Vulkan push constants. The renderer reserves the first 16 bytes for an
/// integer source-texel offset that preserves raster aprons and partial materialization without filtered sampling.
/// Description construction rejects layouts outside that complete subset instead of deferring an unsupported
/// binding to execution.
/// </remarks>
internal sealed class SpirvShaderLowering
{
    private readonly IReadOnlyList<SpirvPushConstantBinding> _pushConstants;

    public SpirvShaderLowering(
        string fragmentShaderSource,
        IReadOnlyList<SpirvPushConstantBinding> pushConstants,
        bool supportsBitExactSkiaHandoff)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fragmentShaderSource);
        ArgumentNullException.ThrowIfNull(pushConstants);

        FragmentShaderSource = fragmentShaderSource.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        SpirvPushConstantBinding[] copy = pushConstants.ToArray();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (SpirvPushConstantBinding binding in copy)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(binding.Name);
            if (!names.Add(binding.Name))
                throw new ArgumentException($"Duplicate SPIR-V push-constant binding '{binding.Name}'.", nameof(pushConstants));
            ArgumentOutOfRangeException.ThrowIfNegative(binding.Offset);
            if (binding.Offset < SpirvPushConstants.UserByteOffset || (binding.Offset & 3) != 0)
            {
                throw new ArgumentException(
                    $"SPIR-V push-constant binding '{binding.Name}' must start at a four-byte-aligned offset at or after {SpirvPushConstants.UserByteOffset}.",
                    nameof(pushConstants));
            }
        }

        _pushConstants = new ReadOnlyCollection<SpirvPushConstantBinding>(copy);
        SupportsBitExactSkiaHandoff = supportsBitExactSkiaHandoff;
        StructuralIdentity = new SpirvShaderLoweringStructuralIdentity(
            FragmentShaderSource,
            copy,
            SupportsBitExactSkiaHandoff);
    }

    public string FragmentShaderSource { get; }

    public IReadOnlyList<SpirvPushConstantBinding> PushConstants => _pushConstants;

    public bool SupportsBitExactSkiaHandoff { get; }

    internal object StructuralIdentity { get; }

    internal void ValidateForDescription(
        ShaderDescriptionKind kind,
        SkslSource skslSource,
        IReadOnlyList<ShaderUniformBinding> uniforms,
        IReadOnlyList<ShaderResourceBinding> resources)
    {
        if (kind != ShaderDescriptionKind.CurrentPixel)
        {
            throw new ArgumentException(
                "The SPIR-V lowering currently supports only CurrentPixel descriptions.",
                nameof(kind));
        }
        if (resources.Count != 0)
        {
            throw new ArgumentException(
                "The SPIR-V CurrentPixel lowering currently supports only its implicit source texture.",
                nameof(resources));
        }
        if (_pushConstants.Count != uniforms.Count)
        {
            throw new ArgumentException(
                "Every CurrentPixel uniform must have exactly one SPIR-V push-constant mapping.",
                nameof(uniforms));
        }

        var occupiedRanges = new List<(int Start, int End, string Name)>();
        foreach (SpirvPushConstantBinding mapping in _pushConstants)
        {
            ShaderUniformBinding binding = uniforms.SingleOrDefault(
                item => string.Equals(item.Name, mapping.Name, StringComparison.Ordinal))
                ?? throw new ArgumentException(
                    $"SPIR-V push constant '{mapping.Name}' has no matching shader uniform binding.",
                    nameof(uniforms));
            SkslUniformDeclaration declaration = skslSource.Uniforms[binding.Name];
            (int alignment, int byteSize) = GetLayout(mapping.Name, declaration);
            if (mapping.Offset % alignment != 0)
            {
                throw new ArgumentException(
                    $"SPIR-V push constant '{mapping.Name}' at offset {mapping.Offset} does not meet its {alignment}-byte alignment.",
                    nameof(uniforms));
            }

            int end = checked(mapping.Offset + byteSize);
            if (end > SpirvPushConstants.ByteSize)
            {
                throw new ArgumentException(
                    $"SPIR-V push constant '{mapping.Name}' exceeds the {SpirvPushConstants.ByteSize}-byte Vulkan minimum.",
                    nameof(uniforms));
            }
            if (occupiedRanges.Any(range => mapping.Offset < range.End && end > range.Start))
            {
                throw new ArgumentException(
                    $"SPIR-V push constant '{mapping.Name}' overlaps another mapping.",
                    nameof(uniforms));
            }
            occupiedRanges.Add((mapping.Offset, end, mapping.Name));
        }
    }

    internal SpirvPushConstants Bind(
        ShaderDescription description,
        ShaderExecutionContext context,
        PixelPoint sourceTexelOffset)
    {
        var result = new SpirvPushConstants();
        Span<byte> bytes = result;
        BinaryPrimitives.WriteInt32LittleEndian(bytes[..sizeof(int)], sourceTexelOffset.X);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.Slice(sizeof(int), sizeof(int)), sourceTexelOffset.Y);
        foreach (SpirvPushConstantBinding mapping in _pushConstants)
        {
            ShaderUniformBinding binding = FindUniform(description, mapping.Name);
            SkslUniformDeclaration declaration = description.Source.Uniforms[binding.Name];
            ShaderUniformValue value = binding.Bind(declaration, context);
            if (value.IsInteger)
            {
                int[] integers = value.Integers!;
                for (int index = 0; index < integers.Length; index++)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(
                        bytes.Slice(mapping.Offset + (index * sizeof(int)), sizeof(int)),
                        integers[index]);
                }
            }
            else
            {
                float[] floats = value.Floats!;
                for (int index = 0; index < floats.Length; index++)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(
                        bytes.Slice(mapping.Offset + (index * sizeof(float)), sizeof(float)),
                        BitConverter.SingleToInt32Bits(floats[index]));
                }
            }
        }
        return result;
    }

    /// <remarks>
    /// <see cref="ValidateForDescription"/> already rejected a lowering whose push constant names no uniform,
    /// so the miss here is unreachable rather than an author error.
    /// </remarks>
    private static ShaderUniformBinding FindUniform(ShaderDescription description, string name)
    {
        IReadOnlyList<ShaderUniformBinding> uniforms = description.Uniforms;
        for (int index = 0; index < uniforms.Count; index++)
        {
            if (string.Equals(uniforms[index].Name, name, StringComparison.Ordinal))
                return uniforms[index];
        }

        throw new InvalidOperationException($"The shader description declares no uniform '{name}'.");
    }

    private static (int Alignment, int ByteSize) GetLayout(
        string name,
        SkslUniformDeclaration declaration)
    {
        if (declaration.ArrayExtent is not null)
        {
            throw new ArgumentException(
                $"SPIR-V push constant '{name}' cannot use an array in the current native subset.",
                nameof(declaration));
        }

        return declaration.Type switch
        {
            "float" or "half" or "int" or "bool" => (4, 4),
            "float2" or "half2" or "int2" => (8, 8),
            "float3" or "half3" or "int3" => (16, 12),
            "float4" or "half4" or "int4" => (16, 16),
            _ => throw new ArgumentException(
                $"SPIR-V push constant '{name}' uses unsupported type '{declaration.Type}'.",
                nameof(declaration)),
        };
    }
}

internal enum ShaderBackendPreference : byte
{
    Auto,
    Sksl,
    Spirv,
}

internal readonly record struct SpirvPushConstantBinding(string Name, int Offset);

[InlineArray(ByteSize)]
internal struct SpirvPushConstants
{
    public const int ByteSize = 128;
    public const int UserByteOffset = 16;

    private byte _element0;
}

internal sealed class SpirvShaderLoweringStructuralIdentity(
    string source,
    SpirvPushConstantBinding[] pushConstants,
    bool supportsBitExactSkiaHandoff)
    : IEquatable<SpirvShaderLoweringStructuralIdentity>
{
    public bool Equals(SpirvShaderLoweringStructuralIdentity? other)
        => other is not null
           && source == other.Source
           && supportsBitExactSkiaHandoff == other.SupportsBitExactSkiaHandoff
           && pushConstants.AsSpan().SequenceEqual(other.PushConstants);

    public override bool Equals(object? obj)
        => obj is SpirvShaderLoweringStructuralIdentity other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(source, StringComparer.Ordinal);
        hash.Add(supportsBitExactSkiaHandoff);
        foreach (SpirvPushConstantBinding item in pushConstants)
            hash.Add(item);
        return hash.ToHashCode();
    }

    private string Source => source;

    private SpirvPushConstantBinding[] PushConstants => pushConstants;

    private bool SupportsBitExactSkiaHandoff => supportsBitExactSkiaHandoff;
}
