using System.Collections.Immutable;
using Beutl.Graphics.Effects;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// The cache identity of an <see cref="EffectGraph"/>'s structure (feature 004, data-model §3, research D3).
/// The key is a typed sequence rather than a delimiter-joined string: arbitrary structural-token text can never
/// impersonate a node boundary or another field. Uniform values, colors, matrices, sampler contents, bounds, ROIs,
/// and resolved sizes remain parameters and are deliberately excluded.
/// </summary>
internal readonly struct StructuralKey : IEquatable<StructuralKey>
{
    private readonly ImmutableArray<KeyPart> _parts;
    private readonly int _hashCode;

    private StructuralKey(ImmutableArray<KeyPart> parts)
    {
        _parts = parts;
        var hash = new HashCode();
        foreach (KeyPart part in parts)
            part.AddToHash(ref hash);
        _hashCode = hash.ToHashCode();
    }

    internal static StructuralKey Compute(EffectGraph graph)
    {
        var parts = ImmutableArray.CreateBuilder<KeyPart>();
        foreach (EffectNode node in graph.Nodes)
            Append(parts, node.Descriptor);
        return new StructuralKey(parts.ToImmutable());
    }

    private static void Append(ImmutableArray<KeyPart>.Builder parts, EffectNodeDescriptor descriptor)
    {
        parts.Add(KeyPart.Create(KeyPartKind.DescriptorType, descriptor.GetType()));
        parts.Add(KeyPart.Create(KeyPartKind.CoordinateInvariant, descriptor.IsCoordinateInvariant));

        switch (descriptor)
        {
            case ShaderNodeDescriptor shader:
                // The stable 64-bit hash is only an index hint. ShaderSourceIdentity uses it for O(1)-sized key
                // hashing but keeps complete source equality, so a collision can never alias two shader programs.
                parts.Add(KeyPart.Create(KeyPartKind.SourceIdentity, new ShaderSourceIdentity(shader.Source)));
                parts.Add(KeyPart.Create(KeyPartKind.SourceKind, shader.Source.Kind));
                parts.Add(KeyPart.Create(KeyPartKind.SrcTileMode, shader.SrcTileMode));
                parts.Add(KeyPart.Create(KeyPartKind.ChildCount, shader.Children.Length));
                if (!shader.IsCoordinateInvariant)
                    parts.Add(KeyPart.Create(KeyPartKind.BoundsIdentity, shader.Bounds.StructuralIdentity));
                break;

            case ColorFilterNodeDescriptor colorFilter:
                parts.Add(KeyPart.Token(colorFilter.StructuralToken));
                break;

            case SkiaFilterNodeDescriptor skiaFilter:
                parts.Add(KeyPart.Token(skiaFilter.StructuralToken));
                parts.Add(KeyPart.Create(KeyPartKind.BoundsIdentity, skiaFilter.Bounds.StructuralIdentity));
                break;

            case GeometryNodeDescriptor geometry:
                parts.Add(KeyPart.Token(geometry.StructuralToken));
                parts.Add(KeyPart.Create(KeyPartKind.RequiresReadback, geometry.RequiresReadback));
                parts.Add(KeyPart.Create(KeyPartKind.BoundsIdentity, geometry.Bounds.StructuralIdentity));
                break;

            case ComputeNodeDescriptor compute:
                parts.Add(KeyPart.Token(compute.StructuralToken));
                parts.Add(KeyPart.Create(KeyPartKind.PassCount, compute.PassCount));
                parts.Add(KeyPart.Create(KeyPartKind.ColorScratchCount, compute.ColorScratchCount));
                parts.Add(KeyPart.Create(KeyPartKind.ComputeFallback, compute.Fallback));
                parts.Add(KeyPart.Create(KeyPartKind.CpuReadback, compute.CpuFallbackRequiresReadback));
                parts.Add(KeyPart.Create(KeyPartKind.DispatchFailureBehavior, compute.DispatchFailureBehavior));
                break;

            case SplitNodeDescriptor split:
                parts.Add(KeyPart.Token(split.StructuralToken));
                parts.Add(KeyPart.Create(KeyPartKind.DynamicOutputs, split.IsDynamicOutputs));
                if (!split.IsDynamicOutputs)
                    parts.Add(KeyPart.Create(KeyPartKind.BranchCount, split.BranchCount));
                parts.Add(KeyPart.Create(KeyPartKind.RequiresReadback, split.RequiresReadback));
                break;

            case CompositeNodeDescriptor composite:
                parts.Add(KeyPart.Token(composite.StructuralToken));
                parts.Add(KeyPart.Create(KeyPartKind.BlendMode, composite.BlendMode));
                parts.Add(KeyPart.Create(KeyPartKind.InputOffsetCount, composite.InputOffsets.Length));
                break;

            case NestedGraphNodeDescriptor nested:
                parts.Add(KeyPart.Token(nested.StructuralToken));
                break;

            case CustomRenderNodeDescriptor custom:
                parts.Add(KeyPart.Create(KeyPartKind.CustomNodeType, custom.NodeType));
                parts.Add(KeyPart.Create(KeyPartKind.ResourceIdentity, custom.Resource.StructuralId));
                break;
        }
    }

    public bool Equals(StructuralKey other)
    {
        if (_parts.IsDefault || other._parts.IsDefault)
            return _parts.IsDefault == other._parts.IsDefault;
        if (_parts.Length != other._parts.Length)
            return false;

        for (int i = 0; i < _parts.Length; i++)
        {
            if (!_parts[i].Equals(other._parts[i]))
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is StructuralKey other && Equals(other);

    public override int GetHashCode() => _hashCode;

    public static bool operator ==(StructuralKey left, StructuralKey right) => left.Equals(right);

    public static bool operator !=(StructuralKey left, StructuralKey right) => !left.Equals(right);

    private enum KeyPartKind
    {
        DescriptorType,
        CoordinateInvariant,
        StructuralToken,
        SourceIdentity,
        SourceKind,
        SrcTileMode,
        ChildCount,
        BoundsIdentity,
        RequiresReadback,
        PassCount,
        ColorScratchCount,
        ComputeFallback,
        CpuReadback,
        DispatchFailureBehavior,
        DynamicOutputs,
        BranchCount,
        BlendMode,
        InputOffsetCount,
        CustomNodeType,
        ResourceIdentity,
    }

    private readonly struct ShaderSourceIdentity : IEquatable<ShaderSourceIdentity>
    {
        private readonly SkslSource _source;
        private readonly int _hashCode;

        public ShaderSourceIdentity(SkslSource source)
        {
            _source = source;
            // IdentityHash has fixed size. The full source remains the collision check in Equals, not hash input.
            _hashCode = HashCode.Combine(source.Kind, source.IdentityHash);
        }

        public bool Equals(ShaderSourceIdentity other)
            => _source.Kind == other._source.Kind
                && string.Equals(_source.Source, other._source.Source, StringComparison.Ordinal);

        public override bool Equals(object? obj)
            => obj is ShaderSourceIdentity other && Equals(other);

        public override int GetHashCode() => _hashCode;
    }

    private readonly struct KeyPart : IEquatable<KeyPart>
    {
        private readonly KeyPartKind _kind;
        private readonly Type _valueType;
        private readonly object _value;

        private KeyPart(KeyPartKind kind, Type valueType, object value)
        {
            _kind = kind;
            _valueType = valueType;
            _value = value;
        }

        public static KeyPart Create<T>(KeyPartKind kind, T value) where T : notnull
            => new(kind, typeof(T), value);

        public static KeyPart Token(object value)
        {
            ArgumentNullException.ThrowIfNull(value);
            // Structural-token equality is the public authoring contract: equal, same-runtime-type values share a
            // plan shape. Keep the value itself so two unequal plugin tokens with the same ToString() can never alias.
            // Like every dictionary key, a custom token must keep Equals/GetHashCode stable for its lifetime.
            return new KeyPart(KeyPartKind.StructuralToken, value.GetType(), value);
        }

        public bool Equals(KeyPart other)
            => _kind == other._kind
                && _valueType == other._valueType
                && Equals(_value, other._value);

        public override bool Equals(object? obj) => obj is KeyPart other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(_kind, _valueType, _value);

        public void AddToHash(ref HashCode hash)
        {
            hash.Add(_kind);
            hash.Add(_valueType);
            hash.Add(_value);
        }
    }
}
