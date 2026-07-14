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
                parts.Add(KeyPart.Create(KeyPartKind.SourceIdentity, shader.Source.IdentityHash));
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
            if (value is Type type)
            {
                // RuntimeType.ToString() omits assembly identity, so two plugin load contexts can legally expose
                // distinct types with the same full name. Type itself has stable identity and is immutable.
                return new KeyPart(KeyPartKind.StructuralToken, typeof(Type), type);
            }

            // StructuralToken historically used its textual identity. Snapshot that text now so a mutable token
            // cannot change an already-cached key's equality while retaining the token's runtime type as a separate
            // field. The typed sequence supplies the boundary that the old delimiter-joined string lacked.
            return new KeyPart(KeyPartKind.StructuralToken, value.GetType(), value.ToString() ?? string.Empty);
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
