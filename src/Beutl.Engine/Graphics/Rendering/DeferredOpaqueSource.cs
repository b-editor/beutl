namespace Beutl.Graphics.Rendering;

internal static class DeferredOpaqueSource
{
    /// <summary>Lists the non-null resources once each, in declaration order.</summary>
    /// <remarks>
    /// Called once per recorded node per frame with a handful of mostly-null arguments, so a scan settles
    /// membership for less than the set behind <c>DistinctBy</c> costs to build.
    /// </remarks>
    public static IReadOnlyList<RenderResource> Resources(params RenderResource?[] resources)
    {
        var distinct = new RenderResource[resources.Length];
        int count = 0;
        for (int index = 0; index < resources.Length; index++)
        {
            if (resources[index] is not { } resource)
                continue;

            bool seen = false;
            for (int other = 0; other < count; other++)
            {
                if (ReferenceEquals(distinct[other].SlotIdentity, resource.SlotIdentity))
                {
                    seen = true;
                    break;
                }
            }

            if (!seen)
                distinct[count++] = resource;
        }

        return count == 0 ? [] : distinct[..count];
    }
}
