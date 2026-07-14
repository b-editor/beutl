using System.Collections.Immutable;
using System.Diagnostics;
using Beutl.Graphics.Effects;
using SkiaSharp;

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
        return new StructuralKey(parts.DrainToImmutable());
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
                parts.Add(KeyPart.SourceIdentity(shader.Source));
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

            default:
                throw new NotSupportedException(
                    $"Effect descriptor type '{descriptor.GetType().FullName}' is not supported by the structural key.");
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
        private readonly KeyPartValueKind _valueKind;
        private readonly long _scalar;
        private readonly object? _reference;

        private KeyPart(KeyPartKind kind, KeyPartValueKind valueKind, long scalar, object? reference)
        {
            _kind = kind;
            _valueKind = valueKind;
            _scalar = scalar;
            _reference = reference;
        }

        public static KeyPart Create(KeyPartKind kind, bool value)
            => new(kind, KeyPartValueKind.Boolean, value ? 1 : 0, null);

        public static KeyPart Create(KeyPartKind kind, int value)
            => new(kind, KeyPartValueKind.Int32, value, null);

        public static KeyPart Create(KeyPartKind kind, long value)
            => new(kind, KeyPartValueKind.Int64, value, null);

        public static KeyPart Create(KeyPartKind kind, float value)
            => new(kind, KeyPartValueKind.Single, BitConverter.SingleToInt32Bits(value), null);

        public static KeyPart Create(KeyPartKind kind, double value)
            => new(kind, KeyPartValueKind.Double, BitConverter.DoubleToInt64Bits(value), null);

        public static KeyPart Create(KeyPartKind kind, SkslSourceKind value)
            => new(kind, KeyPartValueKind.SkslSourceKind, (long)value, null);

        public static KeyPart Create(KeyPartKind kind, SKShaderTileMode value)
            => new(kind, KeyPartValueKind.ShaderTileMode, (long)value, null);

        public static KeyPart Create(KeyPartKind kind, ComputeFallback value)
            => new(kind, KeyPartValueKind.ComputeFallback, (long)value, null);

        public static KeyPart Create(KeyPartKind kind, ComputeDispatchFailureBehavior value)
            => new(kind, KeyPartValueKind.DispatchFailureBehavior, (long)value, null);

        public static KeyPart Create(KeyPartKind kind, BlendMode value)
            => new(kind, KeyPartValueKind.BlendMode, (long)value, null);

        public static KeyPart Create(KeyPartKind kind, Type value)
        {
            ArgumentNullException.ThrowIfNull(value);
            return new KeyPart(kind, KeyPartValueKind.Type, 0, value);
        }

        public static KeyPart SourceIdentity(SkslSource source)
        {
            ArgumentNullException.ThrowIfNull(source);
            return new KeyPart(KeyPartKind.SourceIdentity, KeyPartValueKind.ShaderSourceIdentity, 0, source);
        }

        public static KeyPart Token(object value)
        {
            ArgumentNullException.ThrowIfNull(value);
            // Structural-token equality is the public authoring contract: equal, same-runtime-type values share a
            // plan shape. Keep the value itself so two unequal plugin tokens with the same ToString() can never alias.
            // Like every dictionary key, a custom token must keep Equals/GetHashCode stable for its lifetime.
            return new KeyPart(KeyPartKind.StructuralToken, KeyPartValueKind.StructuralToken, 0, value);
        }

        public bool Equals(KeyPart other)
        {
            if (_kind != other._kind || _valueKind != other._valueKind)
                return false;

            return _valueKind switch
            {
                KeyPartValueKind.Type => Equals(_reference, other._reference),
                KeyPartValueKind.ShaderSourceIdentity =>
                    new ShaderSourceIdentity((SkslSource)_reference!)
                        .Equals(new ShaderSourceIdentity((SkslSource)other._reference!)),
                KeyPartValueKind.StructuralToken =>
                    _reference!.GetType() == other._reference!.GetType()
                        && Equals(_reference, other._reference),
                _ => _scalar == other._scalar,
            };
        }

        public override bool Equals(object? obj) => obj is KeyPart other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            AddToHash(ref hash);
            return hash.ToHashCode();
        }

        public void AddToHash(ref HashCode hash)
        {
            hash.Add(_kind);
            switch (_valueKind)
            {
                case KeyPartValueKind.Boolean:
                    hash.Add(typeof(bool));
                    hash.Add(_scalar != 0);
                    break;
                case KeyPartValueKind.Int32:
                    hash.Add(typeof(int));
                    hash.Add((int)_scalar);
                    break;
                case KeyPartValueKind.Int64:
                    hash.Add(typeof(long));
                    hash.Add(_scalar);
                    break;
                case KeyPartValueKind.Single:
                    hash.Add(typeof(float));
                    hash.Add((int)_scalar);
                    break;
                case KeyPartValueKind.Double:
                    hash.Add(typeof(double));
                    hash.Add(_scalar);
                    break;
                case KeyPartValueKind.SkslSourceKind:
                    hash.Add(typeof(SkslSourceKind));
                    hash.Add((SkslSourceKind)_scalar);
                    break;
                case KeyPartValueKind.ShaderTileMode:
                    hash.Add(typeof(SKShaderTileMode));
                    hash.Add((SKShaderTileMode)_scalar);
                    break;
                case KeyPartValueKind.ComputeFallback:
                    hash.Add(typeof(ComputeFallback));
                    hash.Add((ComputeFallback)_scalar);
                    break;
                case KeyPartValueKind.DispatchFailureBehavior:
                    hash.Add(typeof(ComputeDispatchFailureBehavior));
                    hash.Add((ComputeDispatchFailureBehavior)_scalar);
                    break;
                case KeyPartValueKind.BlendMode:
                    hash.Add(typeof(BlendMode));
                    hash.Add((BlendMode)_scalar);
                    break;
                case KeyPartValueKind.Type:
                    hash.Add(typeof(Type));
                    hash.Add((Type)_reference!);
                    break;
                case KeyPartValueKind.ShaderSourceIdentity:
                    hash.Add(typeof(ShaderSourceIdentity));
                    hash.Add(new ShaderSourceIdentity((SkslSource)_reference!));
                    break;
                case KeyPartValueKind.StructuralToken:
                    hash.Add(_reference!.GetType());
                    hash.Add(_reference);
                    break;
                default:
                    throw new UnreachableException();
            }
        }
    }

    private enum KeyPartValueKind
    {
        Boolean,
        Int32,
        Int64,
        Single,
        Double,
        SkslSourceKind,
        ShaderTileMode,
        ComputeFallback,
        DispatchFailureBehavior,
        BlendMode,
        Type,
        ShaderSourceIdentity,
        StructuralToken,
    }
}
