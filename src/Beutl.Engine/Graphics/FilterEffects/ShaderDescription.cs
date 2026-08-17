using System.Collections.ObjectModel;
using Beutl.Graphics.Rendering;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

/// <summary>Declares one immutable renderer-neutral shader stage recorded into a render graph.</summary>
/// <remarks>
/// Every description keeps its validated SkSL lowering for compatibility and may also carry an engine-authored
/// SPIR-V lowering. Create instances through <see cref="CurrentPixel"/> or <see cref="WholeSource"/>. The renderer
/// derives plan shape from source and declared binding layout. Declared binding callbacks run only during execution
/// and receive execution-scoped writers and contexts that must not be retained.
/// </remarks>
internal sealed class ShaderDescription
{
    private ShaderDescription(
        ShaderDescriptionKind kind,
        SkslSource parsed,
        SpirvShaderLowering? spirvLowering,
        RenderBoundsContract bounds,
        Action<ShaderBindingBuilder>? bindings,
        SKShaderTileMode sourceTileMode)
    {
        var builder = new ShaderBindingBuilder();
        bindings?.Invoke(builder);
        ValidateBindings(parsed, builder.Uniforms, builder.Resources, kind);

        Kind = kind;
        Source = parsed;
        Bounds = bounds;
        Uniforms = new ReadOnlyCollection<ShaderUniformBinding>(builder.Uniforms.ToArray());
        Resources = new ReadOnlyCollection<ShaderResourceBinding>(builder.Resources.ToArray());
        SourceTileMode = sourceTileMode;
        spirvLowering?.ValidateForDescription(kind, parsed, Uniforms, Resources);
        SpirvLowering = spirvLowering;
        StructuralIdentity = new ShaderDescriptionStructuralIdentity(
            kind,
            parsed.Text,
            spirvLowering?.StructuralIdentity,
            bounds.StructuralIdentity,
            sourceTileMode,
            Uniforms.Select(static item => new ShaderBindingStructuralIdentity(item.Name, item.DefinitionFingerprint)).ToArray(),
            Resources.Select(static item => new ShaderResourceStructuralIdentity(
                item.Name,
                item.CoordinateSpace,
                item.DefinitionFingerprint)).ToArray());
    }

    /// <summary>Gets whether the stage transforms only the current pixel or samples the complete upstream source.</summary>
    public ShaderDescriptionKind Kind { get; }

    /// <summary>Gets the non-null normalized SkSL compatibility source.</summary>
    /// <remarks>Backend program validation may still reject the source during execution.</remarks>
    public SkslSource Source { get; }

    /// <summary>Gets the pure mapping from complete input bounds to complete output bounds.</summary>
    /// <remarks><see cref="CurrentPixel"/> descriptions always use <see cref="RenderBoundsContract.Identity"/>.</remarks>
    public RenderBoundsContract Bounds { get; }

    /// <summary>Gets the non-null immutable uniform bindings in declaration order.</summary>
    public IReadOnlyList<ShaderUniformBinding> Uniforms { get; }

    /// <summary>Gets the non-null immutable child-shader resource bindings in declaration order.</summary>
    public IReadOnlyList<ShaderResourceBinding> Resources { get; }

    /// <summary>Gets the sampling mode used outside the implicit <c>src</c> input bounds.</summary>
    /// <remarks>The value is meaningful for <see cref="ShaderDescriptionKind.WholeSource"/> descriptions.</remarks>
    public SKShaderTileMode SourceTileMode { get; }

    /// <summary>Gets the optional engine-authored Vulkan lowering for this stage.</summary>
    internal SpirvShaderLowering? SpirvLowering { get; }

    internal object StructuralIdentity { get; }

    internal object GetStructuralIdentity(ShaderProgramBackend backend)
    {
        if (!Enum.IsDefined(backend))
            throw new ArgumentOutOfRangeException(nameof(backend));
        if (backend == ShaderProgramBackend.Spirv && SpirvLowering is null)
            throw new InvalidOperationException("The shader description has no SPIR-V lowering.");
        return new ShaderDescriptionBackendStructuralIdentity(backend, StructuralIdentity);
    }

    /// <summary>Creates a coordinate-independent shader stage that transforms one resolved pixel value.</summary>
    /// <param name="source">
    /// Non-null SkSL defining exactly one <c>half4 apply(half4 color)</c> entry point. Its argument and result are
    /// premultiplied linear-light RGBA16F values.
    /// </param>
    /// <param name="bindings">
    /// An optional callback invoked immediately to declare bindings, or <see langword="null"/> to declare none.
    /// Binder callbacks registered by the builder are deferred until execution.
    /// </param>
    /// <returns>An immutable deferred shader description.</returns>
    /// <remarks>
    /// The description declares identity bounds and no independent scale change. A stage recorded directly through
    /// <see cref="RenderNodeContext.Shader(RenderFragmentHandle, ShaderDescription)"/> preserves its input effective
    /// scale; when it is the first surviving operation of a <see cref="FilterEffectContext"/>, the enclosing filter
    /// render node may fold its working-scale contract into that stage and select another density. Public
    /// current-pixel stages do not fuse across analytic or antialiased coverage production; the planner resolves
    /// that coverage before applying the stage. Compatible fused stages receive stage-local bounds, required region,
    /// device footprint, input effective scale, and working scale in their execution-time binders.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The source grammar, entry point, declarations, or supplied bindings are invalid or incompatible.
    /// </exception>
    internal static ShaderDescription CurrentPixel(
        string source,
        Action<ShaderBindingBuilder>? bindings = null)
        => CurrentPixel(new SkslSource(source, ShaderDescriptionKind.CurrentPixel), bindings);

    /// <summary>
    /// Creates a current-pixel stage from a source that was already normalized and validated.
    /// </summary>
    /// <remarks>
    /// Engine stages whose SkSL text is a compile-time constant share one parsed source so that recording a
    /// fragment does not re-tokenize and re-validate it.
    /// </remarks>
    internal static ShaderDescription CurrentPixel(
        SkslSource source,
        Action<ShaderBindingBuilder>? bindings)
    {
        if (source.Kind != ShaderDescriptionKind.CurrentPixel)
            throw new ArgumentException("The parsed source is not a CurrentPixel source.", nameof(source));

        return new ShaderDescription(
            ShaderDescriptionKind.CurrentPixel,
            source,
            spirvLowering: null,
            RenderBoundsContract.Identity,
            bindings,
            SKShaderTileMode.Decal);
    }

    /// <summary>Creates a current-pixel stage with both its existing SkSL and Vulkan-native lowerings.</summary>
    internal static ShaderDescription CurrentPixel(
        SkslSource source,
        SpirvShaderLowering spirvLowering,
        Action<ShaderBindingBuilder>? bindings)
    {
        ArgumentNullException.ThrowIfNull(spirvLowering);
        if (source.Kind != ShaderDescriptionKind.CurrentPixel)
            throw new ArgumentException("The parsed source is not a CurrentPixel source.", nameof(source));

        return new ShaderDescription(
            ShaderDescriptionKind.CurrentPixel,
            source,
            spirvLowering,
            RenderBoundsContract.Identity,
            bindings,
            SKShaderTileMode.Decal);
    }

    /// <summary>Creates a materializing shader stage that may sample arbitrary upstream locations.</summary>
    /// <param name="source">
    /// Non-null SkSL defining exactly one <c>half4 main(float2 coord)</c> entry point and declaring the implicit
    /// upstream input as <c>uniform shader src;</c>.
    /// </param>
    /// <param name="bounds">An initialized pure input-to-output bounds contract.</param>
    /// <param name="bindings">
    /// An optional callback invoked immediately to declare bindings other than <c>src</c>, or
    /// <see langword="null"/> to declare none. Binder callbacks registered by the builder are deferred until
    /// execution.
    /// </param>
    /// <param name="sourceTileMode">The tile mode used when the implicit source is sampled outside its bounds.</param>
    /// <returns>An immutable deferred shader description.</returns>
    /// <remarks>
    /// The stage may lead a fused run whose remaining stages are CurrentPixel transforms, but it never consumes an
    /// earlier stage inside that run. Its <c>coord</c> argument is expressed in local output-device pixels and its
    /// recorded effective scale is the resolved working density.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The bounds contract, source grammar, entry point, declarations, or supplied bindings are invalid or
    /// incompatible.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="sourceTileMode"/> is not a defined <see cref="SKShaderTileMode"/> value.
    /// </exception>
    internal static ShaderDescription WholeSource(
        string source,
        RenderBoundsContract bounds,
        Action<ShaderBindingBuilder>? bindings = null,
        SKShaderTileMode sourceTileMode = SKShaderTileMode.Decal)
    {
        bounds.ThrowIfUninitialized(nameof(bounds));
        if (!Enum.IsDefined(sourceTileMode))
            throw new ArgumentOutOfRangeException(nameof(sourceTileMode), sourceTileMode, "The source tile mode is invalid.");

        return new ShaderDescription(
            ShaderDescriptionKind.WholeSource,
            new SkslSource(source, ShaderDescriptionKind.WholeSource),
            spirvLowering: null,
            bounds,
            bindings,
            sourceTileMode);
    }

    internal static ShaderDescription WholeSource(
        SkslSource source,
        RenderBoundsContract bounds,
        Action<ShaderBindingBuilder>? bindings,
        SKShaderTileMode sourceTileMode)
    {
        if (source.Kind != ShaderDescriptionKind.WholeSource)
            throw new ArgumentException("The parsed source is not a WholeSource source.", nameof(source));
        bounds.ThrowIfUninitialized(nameof(bounds));
        if (!Enum.IsDefined(sourceTileMode))
            throw new ArgumentOutOfRangeException(nameof(sourceTileMode), sourceTileMode, "The source tile mode is invalid.");

        return new ShaderDescription(
            ShaderDescriptionKind.WholeSource,
            source,
            spirvLowering: null,
            bounds,
            bindings,
            sourceTileMode);
    }

    private static void ValidateBindings(
        SkslSource source,
        IReadOnlyList<ShaderUniformBinding> uniforms,
        IReadOnlyList<ShaderResourceBinding> resources,
        ShaderDescriptionKind kind)
    {
        var supplied = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (ShaderUniformBinding uniform in uniforms)
        {
            if (!source.Uniforms.TryGetValue(uniform.Name, out SkslUniformDeclaration declaration))
                throw new ArgumentException($"The shader does not declare uniform '{uniform.Name}'.", nameof(uniforms));
            if (declaration.IsShader)
                throw new ArgumentException($"Shader declaration '{uniform.Name}' requires a resource binding.", nameof(uniforms));
            uniform.ValidateDeclaration(declaration);
            supplied.Add(uniform.Name, false);
        }

        foreach (ShaderResourceBinding resource in resources)
        {
            if (kind == ShaderDescriptionKind.WholeSource && resource.Name == "src")
            {
                throw new ArgumentException(
                    "The implicit WholeSource input 'src' cannot be supplied as an explicit resource binding.",
                    nameof(resources));
            }

            if (!source.Uniforms.TryGetValue(resource.Name, out SkslUniformDeclaration declaration))
                throw new ArgumentException($"The shader does not declare resource '{resource.Name}'.", nameof(resources));
            if (!declaration.IsShader)
                throw new ArgumentException($"Uniform '{resource.Name}' requires a uniform binding.", nameof(resources));
            if (kind == ShaderDescriptionKind.CurrentPixel
                && resource.CoordinateSpace != ShaderResourceCoordinateSpace.Value)
            {
                throw new ArgumentException(
                    "CurrentPixel shader resources must use Value coordinates.",
                    nameof(resources));
            }
            supplied.Add(resource.Name, true);
        }

        foreach ((string name, SkslUniformDeclaration declaration) in source.Uniforms)
        {
            if (kind == ShaderDescriptionKind.WholeSource
                && name == "src"
                && declaration.IsShader)
            {
                continue;
            }

            if (!supplied.ContainsKey(name))
                throw new ArgumentException($"Shader binding '{name}' was declared but not supplied.", nameof(uniforms));
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

internal sealed class ShaderDescriptionStructuralIdentity(
    ShaderDescriptionKind kind,
    string source,
    object? spirvLowering,
    object bounds,
    SKShaderTileMode tileMode,
    ShaderBindingStructuralIdentity[] uniforms,
    ShaderResourceStructuralIdentity[] resources)
    : IEquatable<ShaderDescriptionStructuralIdentity>
{
    public bool Equals(ShaderDescriptionStructuralIdentity? other)
        => other is not null
           && kind == other.Kind
           && source == other.Source
           && Equals(spirvLowering, other.SpirvLowering)
           && Equals(bounds, other.Bounds)
           && tileMode == other.TileMode
           && uniforms.AsSpan().SequenceEqual(other.Uniforms)
           && resources.AsSpan().SequenceEqual(other.Resources);

    public override bool Equals(object? obj) => obj is ShaderDescriptionStructuralIdentity other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(kind);
        hash.Add(source, StringComparer.Ordinal);
        hash.Add(spirvLowering);
        hash.Add(bounds);
        hash.Add(tileMode);
        foreach (ShaderBindingStructuralIdentity item in uniforms)
            hash.Add(item);
        foreach (ShaderResourceStructuralIdentity item in resources)
            hash.Add(item);
        return hash.ToHashCode();
    }

    private ShaderDescriptionKind Kind => kind;
    private string Source => source;
    private object? SpirvLowering => spirvLowering;
    private object Bounds => bounds;
    private SKShaderTileMode TileMode => tileMode;
    private ShaderBindingStructuralIdentity[] Uniforms => uniforms;
    private ShaderResourceStructuralIdentity[] Resources => resources;
}

internal sealed record ShaderBindingStructuralIdentity(string Name, object DefinitionFingerprint);

internal sealed record ShaderResourceStructuralIdentity(
    string Name,
    ShaderResourceCoordinateSpace CoordinateSpace,
    object DefinitionFingerprint);
