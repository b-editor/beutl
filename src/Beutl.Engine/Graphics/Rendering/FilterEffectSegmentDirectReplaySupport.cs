namespace Beutl.Graphics.Rendering;

internal static class FilterEffectSegmentDirectReplaySupport
{
    public static bool CanMaterialize(RenderFragmentReference fragment)
    {
        if (!fragment.ContributesValuesToTarget || !TryGetPayload(fragment, out _))
            return false;

        RenderFragmentReference input = fragment.Inputs[0];
        while (TryGetPayload(input, out _))
            input = input.Inputs[0];

        return input.ContributesValuesToTarget
               && input.ValueCardinality.Equals(RenderValueCardinality.Single);
    }

    private static bool TryGetPayload(
        RenderFragmentReference fragment,
        out FilterEffectSegmentRenderFragmentPayload payload)
    {
        if (fragment.Kind == RenderFragmentKind.FilterEffectSegment
            && fragment.Inputs.Length == 1
            && fragment.Payload is FilterEffectSegmentRenderFragmentPayload
            {
                SupportsDirectReplay: true,
            } directPayload)
        {
            payload = directPayload;
            return true;
        }

        payload = null!;
        return false;
    }
}
