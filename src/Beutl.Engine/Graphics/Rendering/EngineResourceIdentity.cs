using System.Runtime.CompilerServices;

using Beutl.Engine;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Derives the equality-stable identity of an <see cref="EngineObject.Resource"/> for use as a cache,
/// structural, or hit-test key.
/// </summary>
/// <remarks>
/// <para>
/// This is the only safe way to key on an engine resource. <see cref="EngineObject.Resource.GetOriginal"/> is
/// declared non-nullable but its backing field is assigned only by <see cref="EngineObject.Resource.Update"/>,
/// so a resource that never went through <see cref="EngineObject.ToResource"/> returns null and makes
/// <c>GetOriginal().Id</c> throw <see cref="NullReferenceException"/>.
/// </para>
/// <para>
/// The derivation is renderer-wide rather than a recorder or effect responsibility: nodes, brushes, filter
/// effects, and 3D all key on the same resources, and a node needs this outside <c>Borrow</c> whenever the
/// identity feeds a hit-test or structural key rather than a declared-resource registration.
/// </para>
/// </remarks>
public static class EngineResourceIdentity
{
    private static readonly ConditionalWeakTable<EngineObject.Resource, DetachedIdentityHolder> s_detached = new();

    /// <summary>Gets the equality-stable identity of <paramref name="resource"/>.</summary>
    /// <param name="resource">The non-null resource to identify.</param>
    /// <returns>
    /// The backing <see cref="EngineObject.Id"/>, or a synthesized <see cref="Guid"/> for a resource that has no
    /// backing object.
    /// </returns>
    /// <remarks>
    /// A synthesized identity is stable per <see cref="EngineObject.Resource"/> instance and held weakly, so a
    /// caller that reallocates the resource every frame gets a new identity every frame and never reaches a
    /// cached output. Returning <see cref="Guid"/> rather than <see cref="object"/> is what lets a caller hold
    /// the identity in a <see cref="Guid"/>-typed field without boxing on every <c>Process</c>.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="resource"/> is <see langword="null"/>.</exception>
    public static Guid Of(EngineObject.Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        EngineObject? original = resource.GetOriginal();
        if (original is not null)
            return original.Id;

        return s_detached.GetValue(resource, static _ => new DetachedIdentityHolder(Guid.NewGuid())).Value;
    }

    private sealed class DetachedIdentityHolder(Guid value)
    {
        public Guid Value { get; } = value;
    }
}
