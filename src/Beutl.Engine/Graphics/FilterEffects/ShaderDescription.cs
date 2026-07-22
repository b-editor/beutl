using System.Collections.ObjectModel;
using Beutl.Graphics.Rendering;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

/// <summary>Declares an immutable, validated SkSL stage recorded into a render graph.</summary>
/// <remarks>
/// Create instances through <see cref="CurrentPixel"/> or <see cref="WholeSource"/>. Description instances use
/// reference equality; the renderer derives separate structural and runtime identities for cache reuse. Declared
/// binding callbacks run only during execution and receive execution-scoped writers and contexts that must not be
/// retained.
/// </remarks>
public sealed class ShaderDescription
{
    private ShaderDescription(
        ShaderDescriptionKind kind,
        string source,
        RenderBoundsContract bounds,
        Action<ShaderBindingBuilder>? bindings,
        SKShaderTileMode sourceTileMode)
    {
        if (!Enum.IsDefined(sourceTileMode))
            throw new ArgumentOutOfRangeException(nameof(sourceTileMode), sourceTileMode, "The source tile mode is invalid.");

        var parsed = new SkslSource(source, kind);
        var builder = new ShaderBindingBuilder();
        bindings?.Invoke(builder);
        ValidateBindings(parsed, builder.Uniforms, builder.Resources, kind);

        Kind = kind;
        Source = parsed;
        Bounds = bounds;
        Uniforms = new ReadOnlyCollection<ShaderUniformBinding>(builder.Uniforms.ToArray());
        Resources = new ReadOnlyCollection<ShaderResourceBinding>(builder.Resources.ToArray());
        SourceTileMode = sourceTileMode;
        StructuralIdentity = new ShaderDescriptionStructuralIdentity(
            kind,
            parsed.Text,
            bounds.StructuralIdentity,
            sourceTileMode,
            Uniforms.Select(static item => new ShaderBindingStructuralIdentity(item.Name, item.StructuralKey)).ToArray(),
            Resources.Select(static item => new ShaderResourceStructuralIdentity(
                item.Name,
                item.CoordinateSpace,
                item.StructuralKey)).ToArray());
    }

    /// <summary>Gets whether the stage transforms only the current pixel or samples the complete upstream source.</summary>
    public ShaderDescriptionKind Kind { get; }

    /// <summary>Gets the non-null normalized and validated SkSL source.</summary>
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

    internal object StructuralIdentity { get; }

    internal object CreateRuntimeIdentity()
        => new ShaderDescriptionRuntimeIdentity(
            Uniforms.Select(static item => item.CreateRuntimeIdentity()).ToArray(),
            Resources.Select(static item => (ShaderResourceRuntimeIdentity)item.CreateRuntimeIdentity()).ToArray());

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
    public static ShaderDescription CurrentPixel(
        string source,
        Action<ShaderBindingBuilder>? bindings = null)
        => new(
            ShaderDescriptionKind.CurrentPixel,
            source,
            RenderBoundsContract.Identity,
            bindings,
            SKShaderTileMode.Decal);

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
    /// The stage runs as an unfused materialization boundary. Its <c>coord</c> argument is expressed in local
    /// output-device pixels and its recorded effective scale is the resolved working density.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The bounds contract, source grammar, entry point, declarations, or supplied bindings are invalid or
    /// incompatible.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="sourceTileMode"/> is not a defined <see cref="SKShaderTileMode"/> value.
    /// </exception>
    public static ShaderDescription WholeSource(
        string source,
        RenderBoundsContract bounds,
        Action<ShaderBindingBuilder>? bindings = null,
        SKShaderTileMode sourceTileMode = SKShaderTileMode.Decal)
    {
        bounds.ThrowIfUninitialized(nameof(bounds));
        return new ShaderDescription(
            ShaderDescriptionKind.WholeSource,
            source,
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
    private object Bounds => bounds;
    private SKShaderTileMode TileMode => tileMode;
    private ShaderBindingStructuralIdentity[] Uniforms => uniforms;
    private ShaderResourceStructuralIdentity[] Resources => resources;
}

internal sealed record ShaderBindingStructuralIdentity(string Name, object StructuralKey);

internal sealed record ShaderResourceStructuralIdentity(
    string Name,
    ShaderResourceCoordinateSpace CoordinateSpace,
    object StructuralKey);

internal sealed class ShaderDescriptionRuntimeIdentity(object[] uniforms, ShaderResourceRuntimeIdentity[] resources)
    : IEquatable<ShaderDescriptionRuntimeIdentity>
{
    public bool Equals(ShaderDescriptionRuntimeIdentity? other)
        => other is not null
           && uniforms.AsSpan().SequenceEqual(other.Uniforms)
           && resources.AsSpan().SequenceEqual(other.Resources);

    public override bool Equals(object? obj) => obj is ShaderDescriptionRuntimeIdentity other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (object item in uniforms)
            hash.Add(item);
        foreach (ShaderResourceRuntimeIdentity item in resources)
            hash.Add(item);
        return hash.ToHashCode();
    }

    private object[] Uniforms => uniforms;
    private ShaderResourceRuntimeIdentity[] Resources => resources;
}

internal sealed record ShaderResourceRuntimeIdentity(RenderResourceIdentity Resource, object RuntimeIdentity);
