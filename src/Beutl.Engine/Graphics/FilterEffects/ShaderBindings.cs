using System.Collections.ObjectModel;
using System.Numerics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

public enum ShaderResourceCoordinateSpace
{
    Value,
    OutputLogical,
    OutputDevice,
}

public sealed class ShaderUniformBinding
{
    private readonly Action<ShaderUniformWriter, ShaderExecutionContext> _bind;
    private readonly Action<SkslUniformDeclaration> _validate;
    private readonly object _runtimeValue;
    private readonly bool _hasAdditionalRuntimeIdentity;
    private readonly bool _requestUniqueRuntimeIdentity;

    internal ShaderUniformBinding(
        string name,
        object structuralKey,
        RenderRuntimeIdentity? runtimeIdentity,
        Action<ShaderUniformWriter, ShaderExecutionContext> bind,
        Action<SkslUniformDeclaration> validate,
        object runtimeValue,
        bool hasAdditionalRuntimeIdentity = false,
        bool requestUniqueRuntimeIdentity = false)
    {
        Name = name;
        StructuralKey = structuralKey;
        RuntimeIdentity = runtimeIdentity;
        _bind = bind;
        _validate = validate;
        _runtimeValue = runtimeValue;
        _hasAdditionalRuntimeIdentity = hasAdditionalRuntimeIdentity;
        _requestUniqueRuntimeIdentity = requestUniqueRuntimeIdentity;
    }

    public string Name { get; }

    public object StructuralKey { get; }

    public RenderRuntimeIdentity? RuntimeIdentity { get; }

    internal void ValidateDeclaration(SkslUniformDeclaration declaration) => _validate(declaration);

    internal object CreateRuntimeIdentity()
    {
        if (!_hasAdditionalRuntimeIdentity)
            return _runtimeValue;

        object additionalIdentity = _requestUniqueRuntimeIdentity
            ? new object()
            : RuntimeIdentity!.Value.Key;
        return new CustomUniformRuntimeValue(_runtimeValue, additionalIdentity);
    }

    internal ShaderUniformValue Bind(SkslUniformDeclaration declaration, ShaderExecutionContext context)
    {
        var writer = new ShaderUniformWriter(declaration);
        try
        {
            _bind(writer, context);
            return writer.Complete();
        }
        finally
        {
            writer.Deactivate();
        }
    }
}

public sealed class ShaderResourceBinding
{
    private readonly Action<ShaderResourceWriter, object, ShaderExecutionContext> _bind;
    private readonly Func<Action<object>, bool> _useResource;
    private readonly bool _requestUniqueRuntimeIdentity;

    internal ShaderResourceBinding(
        string name,
        RenderResource resource,
        ShaderResourceCoordinateSpace coordinateSpace,
        object structuralKey,
        RenderRuntimeIdentity? runtimeIdentity,
        Action<ShaderResourceWriter, object, ShaderExecutionContext> bind,
        Func<Action<object>, bool> useResource,
        bool requestUniqueRuntimeIdentity)
    {
        Name = name;
        Resource = resource;
        CoordinateSpace = coordinateSpace;
        StructuralKey = structuralKey;
        RuntimeIdentity = runtimeIdentity;
        _bind = bind;
        _useResource = useResource;
        _requestUniqueRuntimeIdentity = requestUniqueRuntimeIdentity;
    }

    public string Name { get; }

    public ShaderResourceCoordinateSpace CoordinateSpace { get; }

    public RenderResource Resource { get; }

    public object StructuralKey { get; }

    public RenderRuntimeIdentity? RuntimeIdentity { get; }

    internal object CreateRuntimeIdentity()
        => new ShaderResourceRuntimeIdentity(
            Resource.CacheIdentity,
            _requestUniqueRuntimeIdentity ? new object() : RuntimeIdentity!.Value.Key);

    internal SKShader Bind(ShaderExecutionContext context)
    {
        SKShader? result = null;
        bool invoked = _useResource(value =>
        {
            var writer = new ShaderResourceWriter();
            bool completed = false;
            try
            {
                _bind(writer, value, context);
                result = writer.Complete();
                completed = true;
            }
            finally
            {
                writer.Deactivate();
                if (!completed)
                    writer.DisposePending();
            }
        });
        if (!invoked || result is null)
            throw new InvalidOperationException($"Shader resource binder '{Name}' did not produce a shader.");
        return result;
    }
}

public sealed class ShaderBindingBuilder
{
    private readonly List<ShaderUniformBinding> _uniforms = [];
    private readonly List<ShaderResourceBinding> _resources = [];
    private readonly HashSet<string> _names = new(StringComparer.Ordinal);

    internal ShaderBindingBuilder()
    {
    }

    public void Uniform<T>(string name, T value)
        where T : unmanaged
    {
        ValidateName(name);
        ShaderCanonicalValue canonical = ShaderCanonicalValue.Create(value);
        _uniforms.Add(new ShaderUniformBinding(
            name,
            new DirectUniformStructuralKey(typeof(T)),
            new RenderRuntimeIdentity(canonical.Identity),
            (writer, _) => writer.Set(value),
            canonical.ThrowIfIncompatible,
            canonical.Identity));
    }

    public void Uniform(string name, ReadOnlySpan<float> values)
    {
        ValidateName(name);
        float[] copy = values.ToArray();
        if (copy.Length == 0)
            throw new ArgumentException("A direct uniform span cannot be empty.", nameof(values));
        var identity = new FloatSequenceIdentity(copy.Select(BitConverter.SingleToInt32Bits).ToArray());
        _uniforms.Add(new ShaderUniformBinding(
            name,
            typeof(FloatSequenceIdentity),
            new RenderRuntimeIdentity(identity),
            (writer, _) => writer.Set(copy),
            declaration => ShaderCanonicalValue.ThrowIfFloatSequenceIncompatible(copy, declaration),
            identity));
    }

    public void Uniform<T>(
        string name,
        T value,
        Action<ShaderUniformWriter, T, ShaderExecutionContext> bind,
        object? structuralKey = null,
        RenderRuntimeIdentity? runtimeIdentity = null)
        where T : unmanaged
    {
        ValidateName(name);
        ArgumentNullException.ThrowIfNull(bind);
        if (structuralKey is not null)
            RenderIdentityKeyValidator.ThrowIfInvalid(structuralKey, nameof(structuralKey));
        if (runtimeIdentity is { } identity)
            identity.ThrowIfUninitialized(nameof(runtimeIdentity));

        ShaderCanonicalValue canonical = ShaderCanonicalValue.Create(value);
        object key = structuralKey ?? bind.Method;
        _uniforms.Add(new ShaderUniformBinding(
            name,
            new CustomUniformStructuralKey(typeof(T), key),
            runtimeIdentity,
            (writer, context) => bind(writer, value, context),
            static _ => { },
            canonical.Identity,
            hasAdditionalRuntimeIdentity: true,
            requestUniqueRuntimeIdentity: runtimeIdentity is null));
    }

    public void Resource<T>(
        string name,
        RenderResource<T> resource,
        ShaderResourceCoordinateSpace coordinateSpace,
        Action<ShaderResourceWriter, T, ShaderExecutionContext> bind,
        object? structuralKey = null,
        RenderRuntimeIdentity? runtimeIdentity = null)
        where T : class
    {
        ValidateName(name);
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(bind);
        if (!Enum.IsDefined(coordinateSpace))
            throw new ArgumentOutOfRangeException(nameof(coordinateSpace), coordinateSpace, "The coordinate space is invalid.");
        if (structuralKey is not null)
            RenderIdentityKeyValidator.ThrowIfInvalid(structuralKey, nameof(structuralKey));
        if (runtimeIdentity is { } identity)
            identity.ThrowIfUninitialized(nameof(runtimeIdentity));

        object key = structuralKey ?? bind.Method;
        _resources.Add(new ShaderResourceBinding(
            name,
            resource,
            coordinateSpace,
            new ResourceBindingStructuralKey(typeof(T), key),
            runtimeIdentity,
            (writer, value, context) => bind(writer, (T)value, context),
            use => resource.Registry.Use(resource, value =>
            {
                use(value);
                return true;
            }),
            requestUniqueRuntimeIdentity: runtimeIdentity is null));
    }

    internal IReadOnlyList<ShaderUniformBinding> Uniforms => new ReadOnlyCollection<ShaderUniformBinding>(_uniforms);

    internal IReadOnlyList<ShaderResourceBinding> Resources => new ReadOnlyCollection<ShaderResourceBinding>(_resources);

    private void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!IsIdentifier(name))
            throw new ArgumentException("A shader binding name must be a valid identifier.", nameof(name));
        if (!_names.Add(name))
            throw new ArgumentException($"Duplicate shader binding name '{name}'.", nameof(name));
    }

    private static bool IsIdentifier(string name)
    {
        if (!(char.IsLetter(name[0]) || name[0] == '_'))
            return false;
        for (int i = 1; i < name.Length; i++)
        {
            if (!(char.IsLetterOrDigit(name[i]) || name[i] == '_'))
                return false;
        }
        return true;
    }
}

public sealed class ShaderUniformWriter
{
    private readonly SkslUniformDeclaration _declaration;
    private ShaderUniformValue? _value;
    private bool _active = true;

    internal ShaderUniformWriter(SkslUniformDeclaration declaration)
    {
        _declaration = declaration;
    }

    public void Set<T>(T value)
        where T : unmanaged
    {
        ThrowIfInactive();
        if (_value is not null)
            throw new InvalidOperationException("A shader uniform binder must set its writer exactly once.");
        ShaderCanonicalValue canonical = ShaderCanonicalValue.Create(value);
        canonical.ThrowIfIncompatible(_declaration);
        _value = new ShaderUniformValue(canonical.Values, canonical.Integers, canonical.IsInteger);
    }

    public void Set(ReadOnlySpan<float> values)
    {
        ThrowIfInactive();
        if (_value is not null)
            throw new InvalidOperationException("A shader uniform binder must set its writer exactly once.");
        float[] copy = values.ToArray();
        ShaderCanonicalValue.ThrowIfFloatSequenceIncompatible(copy, _declaration);
        _value = new ShaderUniformValue(copy, null, false);
    }

    internal ShaderUniformValue Complete()
    {
        ThrowIfInactive();
        return _value
               ?? throw new InvalidOperationException("A shader uniform binder must set its writer exactly once.");
    }

    internal void Deactivate() => _active = false;

    private void ThrowIfInactive()
    {
        if (!_active)
            throw new InvalidOperationException("The shader uniform writer is no longer active.");
    }
}

public sealed class ShaderResourceWriter
{
    private SKShader? _shader;
    private bool _active = true;

    internal ShaderResourceWriter()
    {
    }

    public void Set(SKShader shader)
    {
        ThrowIfInactive();
        ArgumentNullException.ThrowIfNull(shader);
        ObjectDisposedException.ThrowIf(shader.Handle == IntPtr.Zero, shader);
        if (_shader is not null)
            throw new InvalidOperationException("A shader resource binder must set its writer exactly once.");
        _shader = shader;
    }

    internal SKShader Complete()
    {
        ThrowIfInactive();
        return _shader
               ?? throw new InvalidOperationException("A shader resource binder must set its writer exactly once.");
    }

    internal void Deactivate() => _active = false;

    internal void DisposePending()
    {
        _shader?.Dispose();
        _shader = null;
    }

    private void ThrowIfInactive()
    {
        if (!_active)
            throw new InvalidOperationException("The shader resource writer is no longer active.");
    }
}

public sealed class ShaderExecutionContext
{
    private readonly RenderExecutionSessionToken _token;
    private readonly Rect _inputBounds;
    private readonly Rect _outputBounds;
    private readonly Rect _requiredRegion;
    private readonly PixelRect _deviceBounds;
    private readonly EffectiveScale _inputEffectiveScale;
    private readonly float _outputScale;
    private readonly float _workingScale;
    private readonly float _maxWorkingScale;
    private readonly RenderIntent _intent;
    private readonly RenderRequestPurpose _purpose;

    internal ShaderExecutionContext(
        RenderExecutionSessionToken token,
        Rect inputBounds,
        Rect outputBounds,
        Rect requiredRegion,
        PixelRect deviceBounds,
        EffectiveScale inputEffectiveScale,
        float outputScale,
        float workingScale,
        float maxWorkingScale,
        RenderIntent intent,
        RenderRequestPurpose purpose)
    {
        ArgumentNullException.ThrowIfNull(token);
        _token = token;
        _inputBounds = inputBounds;
        _outputBounds = outputBounds;
        _requiredRegion = requiredRegion;
        _deviceBounds = deviceBounds;
        _inputEffectiveScale = inputEffectiveScale;
        _outputScale = outputScale;
        _workingScale = workingScale;
        _maxWorkingScale = maxWorkingScale;
        _intent = intent;
        _purpose = purpose;
    }

    public Rect InputBounds
    {
        get { _token.ThrowIfInactive(); return _inputBounds; }
    }

    public Rect OutputBounds
    {
        get { _token.ThrowIfInactive(); return _outputBounds; }
    }

    public Rect RequiredRegion
    {
        get { _token.ThrowIfInactive(); return _requiredRegion; }
    }

    public PixelRect DeviceBounds
    {
        get { _token.ThrowIfInactive(); return _deviceBounds; }
    }

    public PixelSize DeviceSize
    {
        get { _token.ThrowIfInactive(); return _deviceBounds.Size; }
    }

    public Point LogicalOrigin
    {
        get
        {
            _token.ThrowIfInactive();
            return new Point(
                _deviceBounds.X / _workingScale,
                _deviceBounds.Y / _workingScale);
        }
    }

    public EffectiveScale InputEffectiveScale
    {
        get { _token.ThrowIfInactive(); return _inputEffectiveScale; }
    }

    public float OutputScale
    {
        get { _token.ThrowIfInactive(); return _outputScale; }
    }

    public float WorkingScale
    {
        get { _token.ThrowIfInactive(); return _workingScale; }
    }

    public float MaxWorkingScale
    {
        get { _token.ThrowIfInactive(); return _maxWorkingScale; }
    }

    public RenderIntent Intent
    {
        get { _token.ThrowIfInactive(); return _intent; }
    }

    public RenderRequestPurpose Purpose
    {
        get { _token.ThrowIfInactive(); return _purpose; }
    }
}

internal sealed record ShaderUniformValue(float[]? Floats, int[]? Integers, bool IsInteger);

internal sealed record DirectUniformStructuralKey(Type Type);

internal sealed record CustomUniformStructuralKey(Type Type, object Binder);

internal sealed record ResourceBindingStructuralKey(Type Type, object Binder);

internal sealed record CustomUniformRuntimeValue(object Value, object AdditionalIdentity);

internal sealed class FloatSequenceIdentity(int[] bits) : IEquatable<FloatSequenceIdentity>
{
    private readonly int[] _bits = bits;

    public bool Equals(FloatSequenceIdentity? other)
        => other is not null && _bits.AsSpan().SequenceEqual(other._bits);

    public override bool Equals(object? obj) => obj is FloatSequenceIdentity other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (int value in _bits)
            hash.Add(value);
        return hash.ToHashCode();
    }
}

internal readonly record struct ShaderCanonicalValue(
    float[]? Values,
    int[]? Integers,
    bool IsInteger,
    object Identity)
{
    public static ShaderCanonicalValue Create<T>(T value)
        where T : unmanaged
    {
        object boxed = value;
        return boxed switch
        {
            float current => Float([current]),
            double current => Float([(float)current]),
            int current => Integer([current]),
            uint current when current <= int.MaxValue => Integer([(int)current]),
            short current => Integer([current]),
            ushort current => Integer([current]),
            byte current => Integer([current]),
            sbyte current => Integer([current]),
            bool current => Integer([current ? 1 : 0]),
            Vector2 current => Float([current.X, current.Y]),
            Vector3 current => Float([current.X, current.Y, current.Z]),
            Vector4 current => Float([current.X, current.Y, current.Z, current.W]),
            Matrix3x2 current => Float([
                current.M11, current.M12,
                current.M21, current.M22,
                current.M31, current.M32]),
            Matrix4x4 current => Float([
                current.M11, current.M12, current.M13, current.M14,
                current.M21, current.M22, current.M23, current.M24,
                current.M31, current.M32, current.M33, current.M34,
                current.M41, current.M42, current.M43, current.M44]),
            SKPoint current => Float([current.X, current.Y]),
            SKPoint3 current => Float([current.X, current.Y, current.Z]),
            SKSize current => Float([current.Width, current.Height]),
            SKMatrix current => Float([
                current.ScaleX, current.SkewX, current.TransX,
                current.SkewY, current.ScaleY, current.TransY,
                current.Persp0, current.Persp1, current.Persp2]),
            _ => throw new ArgumentException(
                $"'{typeof(T).FullName}' is not a canonical shader uniform value type.",
                nameof(value)),
        };
    }

    public void ThrowIfIncompatible(SkslUniformDeclaration declaration)
    {
        if (declaration.IsShader)
            throw new InvalidOperationException("A shader resource declaration requires a resource binding.");
        int required = GetComponentCount(declaration);
        int actual = IsInteger ? Integers!.Length : Values!.Length;
        bool declaredInteger = declaration.Type is "int" or "int2" or "int3" or "int4" or "bool";
        if (declaredInteger != IsInteger || required != actual)
        {
            throw new InvalidOperationException(
                $"The supplied value is incompatible with SkSL uniform type '{declaration.Type}'.");
        }
    }

    public static void ThrowIfFloatSequenceIncompatible(float[] values, SkslUniformDeclaration declaration)
    {
        if (declaration.IsShader || declaration.Type.StartsWith("int", StringComparison.Ordinal) || declaration.Type == "bool")
            throw new InvalidOperationException($"SkSL uniform type '{declaration.Type}' does not accept float values.");
        int required = GetComponentCount(declaration);
        if (values.Length != required)
            throw new InvalidOperationException($"SkSL uniform type '{declaration.Type}' requires {required} values.");
    }

    private static int GetComponentCount(SkslUniformDeclaration declaration)
    {
        int count = declaration.Type switch
        {
            "float" or "half" or "int" or "bool" => 1,
            "float2" or "half2" or "int2" => 2,
            "float3" or "half3" or "int3" => 3,
            "float4" or "half4" or "int4" => 4,
            "float2x2" or "half2x2" or "mat2" => 4,
            "float3x3" or "half3x3" or "mat3" => 9,
            "float4x4" or "half4x4" or "mat4" => 16,
            _ => throw new InvalidOperationException($"Unsupported SkSL uniform type '{declaration.Type}'."),
        };
        return count * (declaration.ArrayExtent ?? 1);
    }

    private static ShaderCanonicalValue Float(float[] values)
    {
        var identity = new FloatSequenceIdentity(values.Select(BitConverter.SingleToInt32Bits).ToArray());
        return new ShaderCanonicalValue(values, null, false, identity);
    }

    private static ShaderCanonicalValue Integer(int[] values)
        => new(null, values, true, new IntSequenceIdentity(values));
}

internal sealed class IntSequenceIdentity(int[] values) : IEquatable<IntSequenceIdentity>
{
    private readonly int[] _values = [.. values];

    public bool Equals(IntSequenceIdentity? other)
        => other is not null && _values.AsSpan().SequenceEqual(other._values);

    public override bool Equals(object? obj) => obj is IntSequenceIdentity other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (int value in _values)
            hash.Add(value);
        return hash.ToHashCode();
    }
}
