namespace Beutl.Graphics.Rendering;

internal static class FilterEffectSegmentDirectReplaySupport
{
    public static bool CanMaterialize(RenderFragmentReference fragment)
    {
        if (!fragment.ContributesValuesToTarget || !IsDirectReplaySegment(fragment))
            return false;

        RenderFragmentReference input = fragment.Inputs[0];
        while (IsDirectReplaySegment(input))
            input = input.Inputs[0];

        return input.ContributesValuesToTarget
               && input.ValueCardinality.Equals(RenderValueCardinality.Single);
    }

    private static bool IsDirectReplaySegment(RenderFragmentReference fragment)
    {
        return fragment.Kind == RenderFragmentKind.FilterEffectSegment
               && fragment.Inputs.Length == 1
               && fragment.Payload is FilterEffectSegmentRenderFragmentPayload
               {
                   SupportsDirectReplay: true,
               };
    }
}
