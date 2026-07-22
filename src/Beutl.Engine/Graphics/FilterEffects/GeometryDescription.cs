using Beutl.Graphics.Rendering;

namespace Beutl.Graphics.Effects;

/// <summary>Declares an immutable deferred geometry transformation recorded into a render graph.</summary>
/// <remarks>
/// Geometry is an order-preserving zero-or-one map over each input value and is a materialization boundary.
/// Description instances use reference equality; the renderer derives a separate structural identity for plan
/// reuse. The render callback receives a borrowed execution-scoped <see cref="GeometrySession"/> that must not be
/// retained.
/// </remarks>
public sealed class GeometryDescription
{
    private GeometryDescription(
        Action<GeometrySession> render,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        object structuralKey,
        RenderRuntimeIdentity? runtimeIdentity,
        bool requiresReadback,
        IReadOnlyList<RenderResource> resources)
    {
        Render = render;
        Bounds = bounds;
        HitTest = hitTest;
        StructuralKey = structuralKey;
        RuntimeIdentity = runtimeIdentity;
        RequiresReadback = requiresReadback;
        Resources = resources;
        StructuralIdentity = new GeometryStructuralIdentity(
            structuralKey,
            bounds.StructuralIdentity,
            hitTest.StructuralIdentity,
            requiresReadback,
            resources.Select(static resource => resource.GetType()).ToArray());
    }

    /// <summary>Gets the pure mapping from complete input bounds to conservative complete output bounds.</summary>
    public RenderBoundsContract Bounds { get; }

    /// <summary>Gets the CPU-only hit-test contract for the conservative produced geometry.</summary>
    public RenderHitTestContract HitTest { get; }

    /// <summary>Gets the non-null, parameter-independent identity used for structural plan caching.</summary>
    /// <remarks>
    /// When no explicit key is supplied to <see cref="Create"/>, this is the render callback's method identity.
    /// </remarks>
    public object StructuralKey { get; }

    /// <summary>Gets the optional complete identity of pixel-affecting runtime state captured by the callback.</summary>
    /// <remarks>
    /// <see langword="null"/> causes each recording to use a fresh request-local identity and disables
    /// cross-request output-cache reuse for the geometry value.
    /// </remarks>
    public RenderRuntimeIdentity? RuntimeIdentity { get; }

    /// <summary>Gets whether the callback is permitted to request declared input readback.</summary>
    public bool RequiresReadback { get; }

    /// <summary>Gets the non-null immutable list of non-null resources declared for the deferred callback.</summary>
    /// <remarks>
    /// Every resource must belong to the active request family when this description is recorded through
    /// <see cref="RenderNodeContext.Geometry(RenderFragmentHandle, GeometryDescription)"/>.
    /// </remarks>
    public IReadOnlyList<RenderResource> Resources { get; }

    internal Action<GeometrySession> Render { get; }

    internal object StructuralIdentity { get; }

    /// <summary>Creates an immutable deferred geometry description.</summary>
    /// <param name="render">
    /// The non-null callback invoked only during execution. Its borrowed session and facades are valid only for
    /// that invocation and must not be retained.
    /// </param>
    /// <param name="bounds">An initialized pure input-to-output bounds contract.</param>
    /// <param name="hitTest">An initialized pure CPU output hit-test contract.</param>
    /// <param name="structuralKey">
    /// An optional equality-stable, parameter-independent key. <see langword="null"/> uses
    /// <paramref name="render"/>'s method identity; shape-changing captured choices require an explicit key.
    /// </param>
    /// <param name="runtimeIdentity">
    /// The optional complete identity of pixel-affecting captured state. <see langword="null"/> selects a fresh
    /// request-local identity and prevents cross-request output-cache reuse.
    /// </param>
    /// <param name="requiresReadback">Whether the callback may request declared readback of its input.</param>
    /// <param name="resources">
    /// An optional sequence of non-null declared resources. <see langword="null"/> means no resources; otherwise
    /// the sequence is copied immediately and no caller collection is retained.
    /// </param>
    /// <returns>An immutable deferred geometry description.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="render"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// A contract or identity is uninitialized or invalid, or <paramref name="resources"/> contains a null or
    /// released resource.
    /// </exception>
    public static GeometryDescription Create(
        Action<GeometrySession> render,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        object? structuralKey = null,
        RenderRuntimeIdentity? runtimeIdentity = null,
        bool requiresReadback = false,
        IEnumerable<RenderResource>? resources = null)
    {
        ArgumentNullException.ThrowIfNull(render);
        bounds.ThrowIfUninitialized(nameof(bounds));
        hitTest.ThrowIfUninitialized(nameof(hitTest));
        RenderDescriptionValidation.ValidateRuntimeIdentity(runtimeIdentity, nameof(runtimeIdentity));
        object resolvedStructuralKey = RenderDescriptionValidation.ResolveStructuralKey(
            structuralKey,
            render.Method,
            nameof(structuralKey));
        IReadOnlyList<RenderResource> resourceCopy = RenderDescriptionValidation.CopyResources(
            resources,
            nameof(resources));

        return new GeometryDescription(
            render,
            bounds,
            hitTest,
            resolvedStructuralKey,
            runtimeIdentity,
            requiresReadback,
            resourceCopy);
    }
}

internal sealed class GeometryStructuralIdentity(
    object key,
    object bounds,
    object hitTest,
    bool requiresReadback,
    Type[] resourceTypes)
    : IEquatable<GeometryStructuralIdentity>
{
    public bool Equals(GeometryStructuralIdentity? other)
        => other is not null
           && Equals(key, other.Key)
           && Equals(bounds, other.Bounds)
           && Equals(hitTest, other.HitTest)
           && requiresReadback == other.RequiresReadback
           && resourceTypes.AsSpan().SequenceEqual(other.ResourceTypes);

    public override bool Equals(object? obj) => obj is GeometryStructuralIdentity other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(key);
        hash.Add(bounds);
        hash.Add(hitTest);
        hash.Add(requiresReadback);
        foreach (Type resourceType in resourceTypes)
            hash.Add(resourceType);
        return hash.ToHashCode();
    }

    private object Key => key;
    private object Bounds => bounds;
    private object HitTest => hitTest;
    private bool RequiresReadback => requiresReadback;
    private Type[] ResourceTypes => resourceTypes;
}
