using Beutl.Graphics.Rendering;
using SkiaSharp;

namespace Beutl.Graphics.Shaders;

/// <summary>Declares one immutable renderer-neutral shader stage recorded into a render graph.</summary>
/// <remarks>
/// Every description keeps its validated SkSL lowering for compatibility and may also carry an engine-authored
/// SPIR-V lowering. Create instances through <see cref="CurrentPixel"/> or <see cref="WholeSource"/>. The renderer
/// derives plan shape from source and declared binding layout. Declared binding callbacks run only during execution
/// and receive execution-scoped writers and contexts that must not be retained.
/// </remarks>
public sealed class ShaderDescription
{
    private ShaderDescription(
        ShaderDescriptionKind kind,
        SkslSource parsed,
        SpirvShaderLowering? spirvLowering,
        RenderBoundsContract bounds,
        RenderInputDemandContract inputDemand,
        Action<ShaderBindingBuilder>? bindings,
        SKShaderTileMode sourceTileMode,
        RenderHitTestContract? hitTest,
        IReadOnlyList<RenderResourceBinding> hitTestResources)
    {
        var builder = new ShaderBindingBuilder();
        bindings?.Invoke(builder);
        builder.Close();
        ValidateBindings(parsed, builder.Names, builder.Uniforms, builder.Resources, kind);

        Kind = kind;
        Source = parsed;
        Bounds = bounds;
        InputDemand = inputDemand;
        HitTest = hitTest;
        HitTestResources = hitTestResources;
        Uniforms = builder.Uniforms;
        Resources = builder.Resources;
        // Every resource binding is produced by an execution binder; a uniform may or may not be. Indexed
        // rather than queried: a description is built once per recording, so an enumerator here is garbage
        // once a frame.
        bool hasExecutionContextBinder = Resources.Count > 0;
        for (int index = 0; !hasExecutionContextBinder && index < Uniforms.Count; index++)
            hasExecutionContextBinder = Uniforms[index].ReadsExecutionContext;
        HasExecutionContextBinder = hasExecutionContextBinder;
        SourceTileMode = sourceTileMode;
        spirvLowering?.ValidateForDescription(kind, parsed, Uniforms, Resources);
        SpirvLowering = spirvLowering;
        var uniformIdentities = new ShaderBindingStructuralIdentity[Uniforms.Count];
        for (int index = 0; index < uniformIdentities.Length; index++)
        {
            ShaderUniformBinding uniform = Uniforms[index];
            uniformIdentities[index] = new ShaderBindingStructuralIdentity(
                uniform.Name,
                uniform.DefinitionFingerprint);
        }

        var resourceIdentities = new ShaderResourceStructuralIdentity[Resources.Count];
        for (int index = 0; index < resourceIdentities.Length; index++)
        {
            ShaderResourceBinding resource = Resources[index];
            resourceIdentities[index] = new ShaderResourceStructuralIdentity(
                resource.Name,
                resource.CoordinateSpace,
                resource.DefinitionFingerprint);
        }

        StructuralIdentity = new ShaderDescriptionStructuralIdentity(
            kind,
            parsed.Text,
            spirvLowering?.StructuralIdentity,
            bounds.StructuralIdentity,
            inputDemand.StructuralIdentity,
            hitTest?.StructuralIdentity,
            sourceTileMode,
            uniformIdentities,
            resourceIdentities);
    }

    /// <summary>Gets whether the stage transforms only the current pixel or samples the complete upstream source.</summary>
    public ShaderDescriptionKind Kind { get; }

    /// <summary>Gets the non-null normalized SkSL compatibility source.</summary>
    /// <remarks>Backend program validation may still reject the source during execution.</remarks>
    public SkslSource Source { get; }

    /// <summary>Gets the pure mapping from complete input bounds to complete output bounds.</summary>
    /// <remarks><see cref="CurrentPixel"/> descriptions always use <see cref="RenderBoundsContract.Identity"/>.</remarks>
    public RenderBoundsContract Bounds { get; }

    /// <summary>Gets the mapping from this stage's resolved output demand to the demand on its input.</summary>
    /// <remarks>
    /// <see cref="CurrentPixel"/> descriptions always leave demand unchanged; they consume one resolved pixel
    /// value and never resample.
    /// </remarks>
    public RenderInputDemandContract InputDemand { get; }

    /// <summary>Gets the non-null immutable uniform bindings in declaration order.</summary>
    internal IReadOnlyList<ShaderUniformBinding> Uniforms { get; }

    /// <summary>Gets the non-null immutable child-shader resource bindings in declaration order.</summary>
    internal IReadOnlyList<ShaderResourceBinding> Resources { get; }

    /// <summary>Gets the sampling mode used outside the implicit <c>src</c> input bounds.</summary>
    /// <remarks>The value is meaningful for <see cref="ShaderDescriptionKind.WholeSource"/> descriptions.</remarks>
    public SKShaderTileMode SourceTileMode { get; }

    /// <summary>Gets the author-declared CPU hit-test contract, or <see langword="null"/> when none was declared.</summary>
    /// <remarks>
    /// A stage that leaves its input where it found it does not need one: forwarding the question to the input
    /// answers for exactly the pixels the stage produced. A stage whose <see cref="Bounds"/> relocate the
    /// content has to declare one, or the forwarded question is asked at a point the content no longer covers.
    /// </remarks>
    public RenderHitTestContract? HitTest { get; }

    /// <summary>Gets the slot-addressed resource bindings a declared hit test resolves against.</summary>
    /// <remarks>
    /// <see cref="Resources"/> holds the child-shader bindings execution needs, which are addressed by SkSL
    /// name. A hit test addresses the same request-scoped tokens by the slot the call bound them to, so it
    /// reads this list instead of that one.
    /// </remarks>
    public IReadOnlyList<RenderResourceBinding> HitTestResources { get; }

    /// <summary>States, in one place, how a recorded fragment of this stage answers a hit test.</summary>
    /// <remarks>
    /// Recording and the graph-wide re-resolution of a symbolic fragment both build this rule, and the two
    /// have to agree: a fragment whose bounds are resolved later must not change what it hits.
    /// </remarks>
    internal RenderFragmentHitTest CreateFragmentHitTest()
        => HitTest is { } contract
            ? RenderFragmentHitTest.FromContract(contract, HitTestResources)
            : RenderFragmentHitTest.Inputs;

    /// <summary>
    /// Gets whether any binding of this stage produces its value through an author-supplied binder that runs
    /// during execution and receives a <see cref="ShaderExecutionContext"/>.
    /// </summary>
    /// <remarks>
    /// A binder may read request state that the recorded graph does not otherwise express - the request's
    /// output scale and maximum working scale among it - and turn it into different pixels at bounds,
    /// coverage, and density a cache identity would otherwise call interchangeable. A stage that reports
    /// <see langword="true"/> therefore has to carry that request state in its cache identity; a stage whose
    /// values are all fixed while recording must not, or it would lose its reuse for nothing.
    /// </remarks>
    internal bool HasExecutionContextBinder { get; }

    /// <summary>Gets the optional engine-authored Vulkan lowering for this stage.</summary>
    internal SpirvShaderLowering? SpirvLowering { get; }

    internal object StructuralIdentity { get; }

    /// <summary>Creates a coordinate-independent shader stage that transforms one resolved pixel value.</summary>
    /// <param name="source">
    /// SkSL with one <c>half4 apply(half4 color)</c> over premultiplied linear-light RGBA16F.
    /// </param>
    /// <param name="bindings">
    /// An immediate binding declaration callback; registered binders run during execution.
    /// </param>
    /// <param name="hitTest">
    /// A CPU output hit test, or <see langword="null"/> to forward to the input. Supply one when transparent
    /// input can produce non-zero alpha.
    /// </param>
    /// <param name="hitTestResources">
    /// Slot-addressed resources used by <paramref name="hitTest"/>, or <see langword="null"/>.
    /// </param>
    /// <returns>An immutable deferred shader description.</returns>
    /// <remarks>
    /// The stage preserves input bounds and scale. Filter-effect lowering may fold its working-scale contract into
    /// the first stage, but public stages do not fuse across analytic or antialiased coverage production.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The source grammar, entry point, declarations, or supplied bindings are invalid or incompatible.
    /// </exception>
    public static ShaderDescription CurrentPixel(
        string source,
        Action<ShaderBindingBuilder>? bindings = null,
        RenderHitTestContract? hitTest = null,
        IReadOnlyList<RenderResourceBinding>? hitTestResources = null)
        => CurrentPixel(
            new SkslSource(source, ShaderDescriptionKind.CurrentPixel),
            bindings,
            hitTest,
            hitTestResources);

    /// <summary>
    /// Creates a current-pixel stage from a source that was already normalized and validated.
    /// </summary>
    /// <remarks>
    /// Engine stages whose SkSL text is a compile-time constant share one parsed source so that recording a
    /// fragment does not re-tokenize and re-validate it.
    /// </remarks>
    public static ShaderDescription CurrentPixel(
        SkslSource source,
        Action<ShaderBindingBuilder>? bindings,
        RenderHitTestContract? hitTest = null,
        IReadOnlyList<RenderResourceBinding>? hitTestResources = null)
    {
        if (source.Kind != ShaderDescriptionKind.CurrentPixel)
            throw new ArgumentException("The parsed source is not a CurrentPixel source.", nameof(source));
        hitTest?.ThrowIfUninitialized(nameof(hitTest));

        return new ShaderDescription(
            ShaderDescriptionKind.CurrentPixel,
            source,
            spirvLowering: null,
            RenderBoundsContract.Identity,
            RenderInputDemandContract.Unchanged,
            bindings,
            SKShaderTileMode.Decal,
            hitTest,
            RenderDescriptionValidation.CopyResourceBindings(
                hitTestResources,
                nameof(hitTestResources)));
    }

    /// <summary>Creates a current-pixel stage with both its existing SkSL and Vulkan-native lowerings.</summary>
    /// <inheritdoc cref="CurrentPixel(string, Action{ShaderBindingBuilder}, RenderHitTestContract?, IReadOnlyList{RenderResourceBinding})" path="/param[@name='hitTest']|/param[@name='hitTestResources']"/>
    internal static ShaderDescription CurrentPixel(
        SkslSource source,
        SpirvShaderLowering spirvLowering,
        Action<ShaderBindingBuilder>? bindings,
        RenderHitTestContract? hitTest = null,
        IReadOnlyList<RenderResourceBinding>? hitTestResources = null)
    {
        ArgumentNullException.ThrowIfNull(spirvLowering);
        if (source.Kind != ShaderDescriptionKind.CurrentPixel)
            throw new ArgumentException("The parsed source is not a CurrentPixel source.", nameof(source));
        hitTest?.ThrowIfUninitialized(nameof(hitTest));

        return new ShaderDescription(
            ShaderDescriptionKind.CurrentPixel,
            source,
            spirvLowering,
            RenderBoundsContract.Identity,
            RenderInputDemandContract.Unchanged,
            bindings,
            SKShaderTileMode.Decal,
            hitTest,
            RenderDescriptionValidation.CopyResourceBindings(
                hitTestResources,
                nameof(hitTestResources)));
    }

    /// <summary>Creates a materializing shader stage that may sample arbitrary upstream locations.</summary>
    /// <param name="source">
    /// SkSL with one <c>half4 main(float2 coord)</c> and an implicit <c>uniform shader src;</c> input.
    /// </param>
    /// <param name="bounds">An initialized pure input-to-output bounds contract.</param>
    /// <param name="bindings">
    /// An immediate declaration callback for bindings other than <c>src</c>; registered binders run during execution.
    /// </param>
    /// <param name="sourceTileMode">The tile mode used when the implicit source is sampled outside its bounds.</param>
    /// <param name="inputDemand">
    /// Maps resolved output demand to the input density required by resampling.
    /// </param>
    /// <returns>An immutable deferred shader description.</returns>
    /// <remarks>
    /// May lead a fused run of CurrentPixel stages. <c>coord</c> is in local output-device pixels.
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
        SKShaderTileMode sourceTileMode = SKShaderTileMode.Decal,
        RenderInputDemandContract inputDemand = default,
        RenderHitTestContract? hitTest = null,
        IReadOnlyList<RenderResourceBinding>? hitTestResources = null)
    {
        bounds.ThrowIfUninitialized(nameof(bounds));
        hitTest?.ThrowIfUninitialized(nameof(hitTest));
        if (!Enum.IsDefined(sourceTileMode))
            throw new ArgumentOutOfRangeException(nameof(sourceTileMode), sourceTileMode, "The source tile mode is invalid.");

        return new ShaderDescription(
            ShaderDescriptionKind.WholeSource,
            new SkslSource(source, ShaderDescriptionKind.WholeSource),
            spirvLowering: null,
            bounds,
            inputDemand,
            bindings,
            sourceTileMode,
            hitTest,
            RenderDescriptionValidation.CopyResourceBindings(
                hitTestResources,
                nameof(hitTestResources)));
    }

    /// <summary>Creates a materializing shader stage from a source that was already normalized and validated.</summary>
    /// <inheritdoc cref="WholeSource(string, RenderBoundsContract, Action{ShaderBindingBuilder}, SKShaderTileMode, RenderInputDemandContract, RenderHitTestContract?, IReadOnlyList{RenderResourceBinding})" path="/param|/remarks|/exception"/>
    public static ShaderDescription WholeSource(
        SkslSource source,
        RenderBoundsContract bounds,
        Action<ShaderBindingBuilder>? bindings,
        SKShaderTileMode sourceTileMode,
        RenderInputDemandContract inputDemand = default,
        RenderHitTestContract? hitTest = null,
        IReadOnlyList<RenderResourceBinding>? hitTestResources = null)
    {
        if (source.Kind != ShaderDescriptionKind.WholeSource)
            throw new ArgumentException("The parsed source is not a WholeSource source.", nameof(source));
        bounds.ThrowIfUninitialized(nameof(bounds));
        hitTest?.ThrowIfUninitialized(nameof(hitTest));
        if (!Enum.IsDefined(sourceTileMode))
            throw new ArgumentOutOfRangeException(nameof(sourceTileMode), sourceTileMode, "The source tile mode is invalid.");

        return new ShaderDescription(
            ShaderDescriptionKind.WholeSource,
            source,
            spirvLowering: null,
            bounds,
            inputDemand,
            bindings,
            sourceTileMode,
            hitTest,
            RenderDescriptionValidation.CopyResourceBindings(
                hitTestResources,
                nameof(hitTestResources)));
    }

    /// <param name="supplied">
    /// The builder's own name set, which already holds exactly the names the two lists carry. Rebuilding it here
    /// would allocate a second set per recording for the same membership.
    /// </param>
    private static void ValidateBindings(
        SkslSource source,
        HashSet<string> supplied,
        List<ShaderUniformBinding> uniforms,
        List<ShaderResourceBinding> resources,
        ShaderDescriptionKind kind)
    {
        foreach (ShaderUniformBinding uniform in uniforms)
        {
            if (!source.Uniforms.TryGetValue(uniform.Name, out SkslUniformDeclaration declaration))
                throw new ArgumentException($"The shader does not declare uniform '{uniform.Name}'.", nameof(uniforms));
            if (declaration.IsShader)
                throw new ArgumentException($"Shader declaration '{uniform.Name}' requires a resource binding.", nameof(uniforms));
            uniform.ValidateDeclaration(declaration);
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
        }

        foreach ((string name, SkslUniformDeclaration declaration) in source.Uniforms)
        {
            if (kind == ShaderDescriptionKind.WholeSource
                && name == "src"
                && declaration.IsShader)
            {
                continue;
            }

            if (!supplied.Contains(name))
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
    object inputDemand,
    object? hitTest,
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
           && Equals(inputDemand, other.InputDemand)
           && Equals(hitTest, other.HitTest)
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
        hash.Add(inputDemand);
        hash.Add(hitTest);
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
    private object InputDemand => inputDemand;
    private object? HitTest => hitTest;
    private SKShaderTileMode TileMode => tileMode;
    private ShaderBindingStructuralIdentity[] Uniforms => uniforms;
    private ShaderResourceStructuralIdentity[] Resources => resources;
}

internal readonly record struct ShaderBindingStructuralIdentity(string Name, object DefinitionFingerprint);

internal readonly record struct ShaderResourceStructuralIdentity(
    string Name,
    ShaderResourceCoordinateSpace CoordinateSpace,
    object DefinitionFingerprint);
