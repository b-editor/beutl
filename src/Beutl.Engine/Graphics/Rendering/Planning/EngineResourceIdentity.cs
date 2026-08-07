using System.Runtime.CompilerServices;

using Beutl.Engine;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Derives the equality-stable coalescing identity of an <see cref="EngineObject.Resource"/>.
/// </summary>
/// <remarks>
/// <see cref="EngineObject.Resource.GetOriginal"/> is declared non-nullable but its backing field is assigned
/// only by <see cref="EngineObject.Resource.Update"/>, so a resource that never went through
/// <see cref="EngineObject.ToResource"/> returns null and makes <c>GetOriginal().Id</c> throw. The weak table
/// gives that resource an identity equal to itself and to nothing else for as long as it lives.
/// </remarks>
internal static class EngineResourceIdentity
{
    private static readonly ConditionalWeakTable<EngineObject.Resource, DetachedIdentityHolder> s_detached = new();
    private static long s_nextDetachedIdentity;

    public static object Of(EngineObject.Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        EngineObject? original = resource.GetOriginal();
        if (original is not null)
            return original.Id;

        return s_detached.GetValue(
                resource,
                static _ => new DetachedIdentityHolder(
                    new DetachedResourceIdentity(Interlocked.Increment(ref s_nextDetachedIdentity))))
            .Identity;
    }

    private readonly record struct DetachedResourceIdentity(long Value);

    private sealed class DetachedIdentityHolder(DetachedResourceIdentity identity)
    {
        public DetachedResourceIdentity Identity { get; } = identity;
    }
}
