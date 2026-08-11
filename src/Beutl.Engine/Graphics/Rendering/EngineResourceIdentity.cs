using System.Runtime.CompilerServices;

using Beutl.Engine;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Derives an equality-stable identity of an <see cref="EngineObject.Resource"/> for engine-only metadata use.
/// </summary>
/// <remarks>
/// <para>
/// This is the only safe way to key on an engine resource. <see cref="EngineObject.Resource.GetOriginal"/> is
/// null for a resource that never went through <see cref="EngineObject.ToResource"/>; comparing those missing
/// backing ids directly would make any two detached resources compare equal.
/// </para>
/// <para>
/// The derivation is renderer-wide rather than a recorder or effect responsibility: nodes, brushes, filter
/// effects, and 3D all consult the same resources when evaluating engine-only metadata. Public render-node
/// authoring uses declared <see cref="RenderResourceSlot{T}"/> values instead.
/// </para>
/// </remarks>
internal static class EngineResourceIdentity
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
/// caller that reallocates the resource every frame gets a new identity every frame. Returning
/// <see cref="Guid"/> rather than <see cref="object"/> avoids boxing in engine metadata paths.
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
