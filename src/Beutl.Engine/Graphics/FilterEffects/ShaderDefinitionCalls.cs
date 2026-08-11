using Beutl.Graphics.Rendering;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

/// <summary>Defines the fixed source, metadata, and binding shape of a shader operation.</summary>
/// <typeparam name="TState">The per-recording values supplied by <see cref="ShaderCall{TState}"/>.</typeparam>
/// <remarks>
/// A definition is reusable operation shape. A call supplies the values and request-scoped resource bindings for one
/// recording. When any pixel-affecting call state changes, the owning <see cref="RenderNode"/> must set
/// <see cref="RenderNode.HasChanges"/> before the next request. Value providers and execution binders must be
/// non-capturing so every changing value is read from the call state.
/// </remarks>
public sealed class ShaderDefinition<TState>
    where TState : notnull
{
    private readonly ShaderDescriptionKind _kind;
    private readonly SkslSource _source;
    private readonly RenderBoundsContract _bounds;
    private readonly SKShaderTileMode _sourceTileMode;
    private readonly IReadOnlyList<ShaderBindingTemplate<TState>> _bindings;
    private readonly IReadOnlyList<RenderResourceSlot> _resourceSlots;

    private ShaderDefinition(
        ShaderDescriptionKind kind,
        SkslSource source,
        RenderBoundsContract bounds,
        SKShaderTileMode sourceTileMode,
        Action<ShaderDefinitionBuilder<TState>>? bindings)
    {
        var builder = new ShaderDefinitionBuilder<TState>();
        bindings?.Invoke(builder);
        ValidateBindings(source, builder.Shapes, kind);

        _kind = kind;
        _source = source;
        _bounds = bounds;
        _sourceTileMode = sourceTileMode;
        _bindings = builder.Templates.ToArray();
        _resourceSlots = RenderDescriptionValidation.CopyResourceSlots(builder.ResourceSlots, nameof(bindings));
    }

    /// <summary>Creates a current-pixel shader definition.</summary>
    /// <param name="source">SkSL defining exactly one <c>half4 apply(half4 color)</c> entry point.</param>
    /// <param name="bindings">The fixed uniform and resource binding shape, or <see langword="null"/> for none.</param>
    public static ShaderDefinition<TState> CurrentPixel(
        string source,
        Action<ShaderDefinitionBuilder<TState>>? bindings = null)
        => new(
            ShaderDescriptionKind.CurrentPixel,
            new SkslSource(source, ShaderDescriptionKind.CurrentPixel),
            RenderBoundsContract.Identity,
            SKShaderTileMode.Decal,
            bindings);

    /// <summary>Creates a whole-source shader definition.</summary>
    /// <param name="source">
    /// SkSL defining exactly one <c>half4 main(float2 coord)</c> entry point and an implicit
    /// <c>uniform shader src;</c> input.
    /// </param>
    /// <param name="bounds">The fixed pure mapping from complete input to complete output bounds.</param>
    /// <param name="bindings">The fixed uniform and resource binding shape, or <see langword="null"/> for none.</param>
    /// <param name="sourceTileMode">The fixed sampling mode outside the implicit source bounds.</param>
    public static ShaderDefinition<TState> WholeSource(
        string source,
        RenderBoundsContract bounds,
        Action<ShaderDefinitionBuilder<TState>>? bindings = null,
        SKShaderTileMode sourceTileMode = SKShaderTileMode.Decal)
    {
        bounds.ThrowIfUninitialized(nameof(bounds));
        if (!Enum.IsDefined(sourceTileMode))
            throw new ArgumentOutOfRangeException(nameof(sourceTileMode), sourceTileMode, "The source tile mode is invalid.");

        return new ShaderDefinition<TState>(
            ShaderDescriptionKind.WholeSource,
            new SkslSource(source, ShaderDescriptionKind.WholeSource),
            bounds,
            sourceTileMode,
            bindings);
    }

    /// <summary>Binds this shader shape to values and resource tokens for one recording.</summary>
    public ShaderCall<TState> Call(
        TState state,
        IEnumerable<RenderResourceBinding>? bindings = null)
        => new(this, state, bindings);

    internal ShaderDescription CreateDescription(
        TState state,
        IEnumerable<RenderResourceBinding>? bindings)
    {
        IReadOnlyList<RenderResourceBinding> resourceBindings =
            RenderDescriptionValidation.ValidateResourceBindings(_resourceSlots, bindings, nameof(bindings));

        Action<ShaderBindingBuilder> apply = builder =>
        {
            foreach (ShaderBindingTemplate<TState> binding in _bindings)
                binding.Apply(builder, state, resourceBindings);
        };

        return _kind == ShaderDescriptionKind.CurrentPixel
            ? ShaderDescription.CurrentPixel(_source, apply)
            : ShaderDescription.WholeSource(_source, _bounds, apply, _sourceTileMode);
    }

    private static void ValidateBindings(
        SkslSource source,
        IReadOnlyList<ShaderBindingShape> shapes,
        ShaderDescriptionKind kind)
    {
        var supplied = new HashSet<string>(StringComparer.Ordinal);
        foreach (ShaderBindingShape shape in shapes)
        {
            if (!source.Uniforms.TryGetValue(shape.Name, out SkslUniformDeclaration declaration))
                throw new ArgumentException($"The shader does not declare binding '{shape.Name}'.", nameof(shapes));

            if (shape.IsResource != declaration.IsShader)
            {
                throw new ArgumentException(
                    shape.IsResource
                        ? $"Uniform '{shape.Name}' requires a uniform binding."
                        : $"Shader declaration '{shape.Name}' requires a resource binding.",
                    nameof(shapes));
            }

            if (kind == ShaderDescriptionKind.CurrentPixel
                && shape.IsResource
                && shape.CoordinateSpace != ShaderResourceCoordinateSpace.Value)
            {
                throw new ArgumentException(
                    "CurrentPixel shader resources must use Value coordinates.",
                    nameof(shapes));
            }

            supplied.Add(shape.Name);
        }

        foreach ((string name, SkslUniformDeclaration declaration) in source.Uniforms)
        {
            if (kind == ShaderDescriptionKind.WholeSource && name == "src" && declaration.IsShader)
                continue;
            if (!supplied.Contains(name))
                throw new ArgumentException($"Shader binding '{name}' was declared but not supplied.", nameof(shapes));
        }

        if (kind == ShaderDescriptionKind.WholeSource
            && (!source.Uniforms.TryGetValue("src", out SkslUniformDeclaration sourceDeclaration)
                || !sourceDeclaration.IsShader))
        {
            throw new ArgumentException(
                "A WholeSource shader must declare its implicit upstream input as 'uniform shader src;'.",
                nameof(source));
        }
    }
}

/// <summary>Binds one shader definition to the values and resources for one recording.</summary>
public sealed class ShaderCall<TState>
    where TState : notnull
{
    internal ShaderCall(
        ShaderDefinition<TState> definition,
        TState state,
        IEnumerable<RenderResourceBinding>? bindings)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Definition = definition;
        State = state;
        Description = definition.CreateDescription(state, bindings);
    }

    /// <summary>Gets the immutable shader shape.</summary>
    public ShaderDefinition<TState> Definition { get; }

    /// <summary>Gets the callback state supplied for this recording.</summary>
    public TState State { get; }

    internal ShaderDescription Description { get; }
}

/// <summary>Declares the fixed uniform and child-shader binding shape of a shader definition.</summary>
/// <typeparam name="TState">The call state used to obtain each uniform value.</typeparam>
public sealed class ShaderDefinitionBuilder<TState>
    where TState : notnull
{
    private readonly List<ShaderBindingTemplate<TState>> _templates = [];
    private readonly List<ShaderBindingShape> _shapes = [];
    private readonly List<RenderResourceSlot> _resourceSlots = [];
    private readonly HashSet<string> _names = new(StringComparer.Ordinal);

    internal ShaderDefinitionBuilder()
    {
    }

    /// <summary>Declares a direct canonical uniform supplied from the call state.</summary>
    /// <remarks>The provider must not capture values; use a <see langword="static"/> lambda or method.</remarks>
    public void Uniform<T>(string name, Func<TState, T> value)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(value);
        ValidateCallStateCallback(value, nameof(value));
        AddUniform(name, new DirectUniformTemplate<TState, T>(name, value));
    }

    /// <summary>Declares a floating-point sequence uniform supplied from the call state.</summary>
    /// <remarks>The provider must not capture values; use a <see langword="static"/> lambda or method.</remarks>
    public void Uniform(string name, Func<TState, IReadOnlyList<float>> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        ValidateCallStateCallback(values, nameof(values));
        AddUniform(name, new FloatSequenceUniformTemplate<TState>(name, values));
    }

    /// <summary>Declares a custom uniform binder supplied from the call state.</summary>
    /// <remarks>Both callbacks must not capture values; use <see langword="static"/> callbacks.</remarks>
    public void Uniform<T>(
        string name,
        Func<TState, T> value,
        Action<ShaderUniformWriter, T, ShaderExecutionContext> bind)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(bind);
        ValidateCallStateCallback(value, nameof(value));
        ValidateCallStateCallback(bind, nameof(bind));
        AddUniform(name, new CustomUniformTemplate<TState, T>(name, value, bind));
    }

    /// <summary>Declares a typed child-shader resource slot and its execution binder.</summary>
    /// <remarks>The binder must not capture values; use a <see langword="static"/> callback.</remarks>
    public void Resource<T>(
        string name,
        RenderResourceSlot<T> slot,
        ShaderResourceCoordinateSpace coordinateSpace,
        Action<ShaderResourceWriter, T, ShaderExecutionContext> bind)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(bind);
        ValidateCallStateCallback(bind, nameof(bind));
        if (!Enum.IsDefined(coordinateSpace))
            throw new ArgumentOutOfRangeException(nameof(coordinateSpace), coordinateSpace, "The coordinate space is invalid.");

        ValidateName(name);
        _templates.Add(new ResourceTemplate<TState, T>(name, slot, coordinateSpace, bind));
        _shapes.Add(new ShaderBindingShape(name, IsResource: true, coordinateSpace));
        _resourceSlots.Add(slot);
    }

    internal IReadOnlyList<ShaderBindingTemplate<TState>> Templates => _templates;

    internal IReadOnlyList<ShaderBindingShape> Shapes => _shapes;

    internal IReadOnlyList<RenderResourceSlot> ResourceSlots => _resourceSlots;

    private void AddUniform(string name, ShaderBindingTemplate<TState> template)
    {
        ValidateName(name);
        _templates.Add(template);
        _shapes.Add(new ShaderBindingShape(name, IsResource: false, CoordinateSpace: null));
    }

    private void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!(char.IsLetter(name[0]) || name[0] == '_'))
            throw new ArgumentException("A shader binding name must be a valid identifier.", nameof(name));
        for (int i = 1; i < name.Length; i++)
        {
            if (!(char.IsLetterOrDigit(name[i]) || name[i] == '_'))
                throw new ArgumentException("A shader binding name must be a valid identifier.", nameof(name));
        }
        if (!_names.Add(name))
            throw new ArgumentException($"Duplicate shader binding name '{name}'.", nameof(name));
    }

    private static void ValidateCallStateCallback(Delegate callback, string parameterName)
    {
        if (RenderIdentityKeyValidator.CapturesState(callback))
        {
            throw new ArgumentException(
                "A shader definition callback must not capture values. Read changing values from call state and pass a static callback.",
                parameterName);
        }
    }
}

internal sealed record ShaderBindingShape(
    string Name,
    bool IsResource,
    ShaderResourceCoordinateSpace? CoordinateSpace);

internal abstract class ShaderBindingTemplate<TState>
    where TState : notnull
{
    internal abstract void Apply(
        ShaderBindingBuilder builder,
        TState state,
        IReadOnlyList<RenderResourceBinding> resourceBindings);
}

internal sealed class DirectUniformTemplate<TState, TValue>(
    string name,
    Func<TState, TValue> value)
    : ShaderBindingTemplate<TState>
    where TState : notnull
    where TValue : unmanaged
{
    internal override void Apply(
        ShaderBindingBuilder builder,
        TState state,
        IReadOnlyList<RenderResourceBinding> resourceBindings)
        => builder.Uniform(name, value(state));
}

internal sealed class FloatSequenceUniformTemplate<TState>(
    string name,
    Func<TState, IReadOnlyList<float>> values)
    : ShaderBindingTemplate<TState>
    where TState : notnull
{
    internal override void Apply(
        ShaderBindingBuilder builder,
        TState state,
        IReadOnlyList<RenderResourceBinding> resourceBindings)
    {
        IReadOnlyList<float> current = values(state)
            ?? throw new InvalidOperationException("A shader uniform value provider returned null.");
        builder.Uniform(name, current.ToArray());
    }
}

internal sealed class CustomUniformTemplate<TState, TValue>(
    string name,
    Func<TState, TValue> value,
    Action<ShaderUniformWriter, TValue, ShaderExecutionContext> bind)
    : ShaderBindingTemplate<TState>
    where TState : notnull
    where TValue : unmanaged
{
    internal override void Apply(
        ShaderBindingBuilder builder,
        TState state,
        IReadOnlyList<RenderResourceBinding> resourceBindings)
        => builder.Uniform(name, value(state), bind);
}

internal sealed class ResourceTemplate<TState, TValue>(
    string name,
    RenderResourceSlot<TValue> slot,
    ShaderResourceCoordinateSpace coordinateSpace,
    Action<ShaderResourceWriter, TValue, ShaderExecutionContext> bind)
    : ShaderBindingTemplate<TState>
    where TState : notnull
    where TValue : class
{
    internal override void Apply(
        ShaderBindingBuilder builder,
        TState state,
        IReadOnlyList<RenderResourceBinding> resourceBindings)
    {
        RenderResourceBinding binding = resourceBindings.FirstOrDefault(item => ReferenceEquals(item.Slot, slot))
            ?? throw new InvalidOperationException("The shader definition slot was not bound for this call.");
        builder.Resource(name, (RenderResource<TValue>)binding.Resource, coordinateSpace, bind);
    }
}
