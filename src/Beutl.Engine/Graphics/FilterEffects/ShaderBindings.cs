using System.Collections.ObjectModel;
using System.Numerics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

/// <summary>Declares how coordinates passed to a child shader are interpreted.</summary>
/// <remarks>
/// The resource binder uses its <see cref="ShaderExecutionContext"/> to create a shader or local matrix that matches
/// the declared space. The binder must not retain its writer, context, or callback-provided raw resource and must not
/// dispose the raw resource; disposal ownership remains defined by the original owned or borrowed registration.
/// </remarks>
public enum ShaderResourceCoordinateSpace
{
    /// <summary>Interprets coordinates as author-defined value coordinates without an output-space conversion.</summary>
    /// <remarks>This is the only coordinate space accepted by <see cref="ShaderDescriptionKind.CurrentPixel"/>.</remarks>
    Value,

    /// <summary>Interprets coordinates in logical composition units.</summary>
    OutputLogical,

    /// <summary>
    /// Interprets coordinates in local output-device pixels, matching the <c>coord</c> argument of a whole-source
    /// shader.
    /// </summary>
    /// <remarks>
    /// For a coordinate <c>coord</c>, the corresponding logical point is
    /// <c>LogicalOrigin + coord / WorkingScale</c>.
    /// </remarks>
    OutputDevice,
}

/// <summary>Describes one immutable uniform binding declared for a shader.</summary>
/// <remarks>Instances are created through <see cref="ShaderBindingBuilder"/>.</remarks>
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

    /// <summary>Gets the non-null SkSL uniform declaration name.</summary>
    public string Name { get; }

    /// <summary>Gets the equality-stable key that identifies the binding's structural behavior.</summary>
    /// <remarks>
    /// Runtime values are excluded from this key. A custom binder must supply an explicit structural key when
    /// captured state changes the generated binding shape.
    /// </remarks>
    public object StructuralKey { get; }

    /// <summary>Gets the optional identity for pixel-affecting runtime state read by a custom binder.</summary>
    /// <remarks>
    /// Direct bindings supply a canonical identity automatically. For custom bindings, <see langword="null"/> makes
    /// the binding request-unique and therefore disables cross-request output-cache reuse.
    /// </remarks>
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

/// <summary>Describes one immutable child-shader resource binding declared for a shader.</summary>
/// <remarks>Instances are created through <see cref="ShaderBindingBuilder"/>.</remarks>
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

    /// <summary>Gets the non-null SkSL child-shader declaration name.</summary>
    public string Name { get; }

    /// <summary>Gets how coordinates passed to the child shader are interpreted.</summary>
    public ShaderResourceCoordinateSpace CoordinateSpace { get; }

    /// <summary>Gets the request-scoped resource token used by the execution-time binder.</summary>
    /// <remarks>
    /// The token scopes access to the raw resource without changing whether the request or the caller owns it.
    /// </remarks>
    public RenderResource Resource { get; }

    /// <summary>Gets the equality-stable key that identifies the binding's structural behavior.</summary>
    /// <remarks>
    /// Resource contents and other runtime values are excluded. Supply an explicit key when captured state changes
    /// the generated binding shape.
    /// </remarks>
    public object StructuralKey { get; }

    /// <summary>Gets the optional identity for pixel-affecting runtime state read by the binder.</summary>
    /// <remarks>
    /// <see langword="null"/> makes the binding request-unique and therefore disables cross-request output-cache
    /// reuse. The resource token's cache identity is tracked independently.
    /// </remarks>
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

/// <summary>Declares uniform and child-shader bindings while a <see cref="ShaderDescription"/> is created.</summary>
/// <remarks>
/// The description invokes its builder callback synchronously and snapshots the declared bindings before returning.
/// Registered execution binders run later. Their writers, contexts, and callback-provided raw resources must not be
/// retained, and binders must not dispose raw resources. Disposal ownership continues to follow each resource's owned
/// or borrowed registration. Every binding name must be a unique SkSL identifier matching a declaration in the
/// source.
/// </remarks>
public sealed class ShaderBindingBuilder
{
    private readonly List<ShaderUniformBinding> _uniforms = [];
    private readonly List<ShaderResourceBinding> _resources = [];
    private readonly HashSet<string> _names = new(StringComparer.Ordinal);

    internal ShaderBindingBuilder()
    {
    }

    /// <summary>Declares a direct uniform whose canonical value is written without an execution callback.</summary>
    /// <typeparam name="T">An unmanaged type in the supported canonical scalar, vector, or matrix allowlist.</typeparam>
    /// <param name="name">The unique non-null SkSL uniform declaration name.</param>
    /// <param name="value">The value copied into the immutable description and its runtime cache identity.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is invalid or duplicated, or <typeparamref name="T"/> is not a supported canonical
    /// uniform type.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">An unsigned value cannot be represented by its SkSL type.</exception>
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

    /// <summary>Declares a direct floating-point uniform from a sequence copied during description creation.</summary>
    /// <param name="name">The unique non-null SkSL uniform declaration name.</param>
    /// <param name="values">A non-empty sequence whose contents are copied immediately and are never retained.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is invalid or duplicated, or <paramref name="values"/> is empty.
    /// </exception>
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

    /// <summary>Declares a uniform whose value is produced by an execution-time binder.</summary>
    /// <typeparam name="T">An unmanaged type in the supported canonical scalar, vector, or matrix allowlist.</typeparam>
    /// <param name="name">The unique non-null SkSL uniform declaration name.</param>
    /// <param name="value">
    /// The author value passed to <paramref name="bind"/> and automatically included in runtime cache identity.
    /// </param>
    /// <param name="bind">
    /// The non-null execution callback. It must call <see cref="ShaderUniformWriter.Set{T}(T)"/> or
    /// <see cref="ShaderUniformWriter.Set(ReadOnlySpan{float})"/> exactly once and must not retain the writer or
    /// context. The unmanaged <paramref name="value"/> is passed by value.
    /// </param>
    /// <param name="structuralKey">
    /// An optional immutable, equality-stable key for captured state that changes binding shape. When
    /// <see langword="null"/>, the binder method identifies the shape.
    /// </param>
    /// <param name="runtimeIdentity">
    /// An optional complete, equality-stable identity for any additional pixel-affecting state read by the binder.
    /// When <see langword="null"/>, the binding is request-unique and cannot reuse output cache entries across
    /// requests.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="name"/> or <paramref name="bind"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is invalid or duplicated, an identity is invalid, or <typeparamref name="T"/> is not
    /// a supported canonical uniform type.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">An unsigned value cannot be represented by its SkSL type.</exception>
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

    /// <summary>Declares a child-shader resource produced by an execution-time binder.</summary>
    /// <typeparam name="T">The raw request-scoped resource type.</typeparam>
    /// <param name="name">The unique non-null SkSL child-shader declaration name.</param>
    /// <param name="resource">A non-null resource token registered with the request family.</param>
    /// <param name="coordinateSpace">How the returned child shader interprets coordinates passed to its <c>eval</c>.</param>
    /// <param name="bind">
    /// The non-null execution callback. It must call <see cref="ShaderResourceWriter.Set"/> exactly once with a newly
    /// created shader. It must not retain the writer, context, or callback-provided resource and must not dispose the
    /// resource. A borrowed resource remains caller-owned and its pixel-affecting state must remain read-only
    /// throughout the executing request; an owned resource remains request-owned.
    /// </param>
    /// <param name="structuralKey">
    /// An optional immutable, equality-stable key for captured state that changes binding shape. When
    /// <see langword="null"/>, the binder method identifies the shape.
    /// </param>
    /// <param name="runtimeIdentity">
    /// An optional complete, equality-stable identity for additional pixel-affecting state read by the binder. When
    /// <see langword="null"/>, the binding is request-unique and cannot reuse output cache entries across requests.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="name"/>, <paramref name="resource"/>, or <paramref name="bind"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is invalid or duplicated, or an identity is invalid.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="coordinateSpace"/> is not a defined <see cref="ShaderResourceCoordinateSpace"/> value.
    /// </exception>
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

/// <summary>Writes the single value produced by an execution-time uniform binder.</summary>
/// <remarks>
/// A binder must call one <c>Set</c> overload exactly once. The writer is valid only during that binder invocation
/// and must not be retained.
/// </remarks>
public sealed class ShaderUniformWriter
{
    private readonly SkslUniformDeclaration _declaration;
    private ShaderUniformValue? _value;
    private bool _active = true;

    internal ShaderUniformWriter(SkslUniformDeclaration declaration)
    {
        _declaration = declaration;
    }

    /// <summary>Sets the binder result from a supported canonical scalar, vector, or matrix value.</summary>
    /// <typeparam name="T">An unmanaged type in the supported canonical uniform allowlist.</typeparam>
    /// <param name="value">The value to validate against the parsed SkSL declaration.</param>
    /// <exception cref="InvalidOperationException">
    /// The writer is inactive, a value was already set, or the value is incompatible with the SkSL declaration.
    /// </exception>
    /// <exception cref="ArgumentException"><typeparamref name="T"/> is not a supported canonical uniform type.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An unsigned value cannot be represented by its SkSL type.</exception>
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

    /// <summary>Sets the binder result from a floating-point sequence copied during the call.</summary>
    /// <param name="values">The values to validate and copy; the caller's memory is not retained.</param>
    /// <exception cref="InvalidOperationException">
    /// The writer is inactive, a value was already set, or the sequence is incompatible with the SkSL declaration.
    /// </exception>
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

/// <summary>Transfers the single child shader produced by an execution-time resource binder to the renderer.</summary>
/// <remarks>
/// A binder must call <see cref="Set"/> exactly once. The writer is valid only during that binder invocation and
/// must not be retained.
/// </remarks>
public sealed class ShaderResourceWriter
{
    private SKShader? _shader;
    private bool _active = true;

    internal ShaderResourceWriter()
    {
    }

    /// <summary>Sets the binder result and transfers ownership of the shader to the renderer.</summary>
    /// <param name="shader">A non-null, non-disposed shader newly created for this binding invocation.</param>
    /// <remarks>
    /// The renderer disposes <paramref name="shader"/> after binding and program execution, or if binding fails. The
    /// binder must not retain, use, or dispose it after this method returns.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="shader"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException"><paramref name="shader"/> is already disposed.</exception>
    /// <exception cref="InvalidOperationException">The writer is inactive or a shader was already set.</exception>
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

/// <summary>Exposes resolved, stage-local metadata to an execution-time shader binder.</summary>
/// <remarks>
/// The context is valid only during the current compiled shader run's binding phase and must not be retained. Every
/// property throws <see cref="InvalidOperationException"/> after that phase completes.
/// </remarks>
public sealed class ShaderExecutionContext
{
    private readonly RenderExecutionSessionToken _token;
    private readonly Rect _inputBounds;
    private readonly Rect _outputBounds;
    private readonly Rect _requiredRegion;
    private readonly PixelRect _deviceBounds;
    private readonly Point _logicalOrigin;
    private readonly Vector _deviceGridOffset;
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
        : this(
            token,
            inputBounds,
            outputBounds,
            requiredRegion,
            deviceBounds,
            deviceBounds.ToRect(workingScale),
            inputEffectiveScale,
            outputScale,
            workingScale,
            maxWorkingScale,
            intent,
            purpose)
    {
    }

    internal ShaderExecutionContext(
        RenderExecutionSessionToken token,
        Rect inputBounds,
        Rect outputBounds,
        Rect requiredRegion,
        PixelRect deviceBounds,
        Rect rasterBounds,
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
        _logicalOrigin = rasterBounds.Position;
        _deviceGridOffset = new Vector(
            (deviceBounds.X / workingScale) - rasterBounds.X,
            (deviceBounds.Y / workingScale) - rasterBounds.Y);
        _inputEffectiveScale = inputEffectiveScale;
        _outputScale = outputScale;
        _workingScale = workingScale;
        _maxWorkingScale = maxWorkingScale;
        _intent = intent;
        _purpose = purpose;
    }

    /// <summary>Gets the stage's complete logical input bounds.</summary>
    /// <exception cref="InvalidOperationException">The shader binding phase has completed.</exception>
    public Rect InputBounds
    {
        get { _token.ThrowIfInactive(); return _inputBounds; }
    }

    /// <summary>Gets the stage's complete logical output bounds.</summary>
    /// <exception cref="InvalidOperationException">The shader binding phase has completed.</exception>
    public Rect OutputBounds
    {
        get { _token.ThrowIfInactive(); return _outputBounds; }
    }

    /// <summary>Gets the stage-local logical output region required by the current request.</summary>
    /// <exception cref="InvalidOperationException">The shader binding phase has completed.</exception>
    public Rect RequiredRegion
    {
        get { _token.ThrowIfInactive(); return _requiredRegion; }
    }

    /// <summary>Gets the destination footprint in composition-device pixels.</summary>
    /// <remarks>
    /// The footprint reflects the actual runtime-clamped <see cref="WorkingScale"/>.
    /// Subtract <see cref="DeviceGridOffset"/> after converting it to logical units to obtain
    /// the stage-local footprint.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The shader binding phase has completed.</exception>
    public PixelRect DeviceBounds
    {
        get { _token.ThrowIfInactive(); return _deviceBounds; }
    }

    /// <summary>Gets the destination footprint size, equal to <see cref="DeviceBounds"/>.<c>Size</c>.</summary>
    /// <exception cref="InvalidOperationException">The shader binding phase has completed.</exception>
    public PixelSize DeviceSize
    {
        get { _token.ThrowIfInactive(); return _deviceBounds.Size; }
    }

    /// <summary>
    /// Gets the translation from stage-local coordinates to the composition-device grid used to
    /// round <see cref="DeviceBounds"/>.
    /// </summary>
    public Vector DeviceGridOffset
    {
        get { _token.ThrowIfInactive(); return _deviceGridOffset; }
    }

    /// <summary>Gets the logical point represented by local output-device coordinate <c>(0, 0)</c>.</summary>
    /// <remarks>
    /// A local device coordinate <c>coord</c> represents
    /// <c>LogicalOrigin + coord / WorkingScale</c>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The shader binding phase has completed.</exception>
    public Point LogicalOrigin
    {
        get
        {
            _token.ThrowIfInactive();
            return _logicalOrigin;
        }
    }

    /// <summary>Gets the effective-scale supply resolved for the stage input.</summary>
    /// <remarks>
    /// The first fused stage receives the materialized input scale; later stages receive the fused run's
    /// <see cref="WorkingScale"/>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The shader binding phase has completed.</exception>
    public EffectiveScale InputEffectiveScale
    {
        get { _token.ThrowIfInactive(); return _inputEffectiveScale; }
    }

    /// <summary>Gets the final output density requested for the render, in device pixels per logical unit.</summary>
    /// <remarks>This value is not an intermediate allocation ceiling; use <see cref="WorkingScale"/> for execution.</remarks>
    /// <exception cref="InvalidOperationException">The shader binding phase has completed.</exception>
    public float OutputScale
    {
        get { _token.ThrowIfInactive(); return _outputScale; }
    }

    /// <summary>
    /// Gets the positive finite density selected for this stage after working-scale and allocation-limit clamping.
    /// </summary>
    /// <exception cref="InvalidOperationException">The shader binding phase has completed.</exception>
    public float WorkingScale
    {
        get { _token.ThrowIfInactive(); return _workingScale; }
    }

    /// <summary>Gets the sanitized maximum working density allowed by the render request.</summary>
    /// <exception cref="InvalidOperationException">The shader binding phase has completed.</exception>
    public float MaxWorkingScale
    {
        get { _token.ThrowIfInactive(); return _maxWorkingScale; }
    }

    /// <summary>Gets whether the request targets interactive preview or delivery-quality rendering.</summary>
    /// <exception cref="InvalidOperationException">The shader binding phase has completed.</exception>
    public RenderIntent Intent
    {
        get { _token.ThrowIfInactive(); return _intent; }
    }

    /// <summary>Gets the high-level operation that caused this render request.</summary>
    /// <exception cref="InvalidOperationException">The shader binding phase has completed.</exception>
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
            uint current => throw new ArgumentOutOfRangeException(
                nameof(value),
                current,
                "A UInt32 shader uniform value cannot exceed Int32.MaxValue."),
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
