namespace Beutl.Graphics.Rendering.Requests;

internal sealed partial class RegionAnalyzer
{
    private static bool HasFilterEffectWorkingScale(RenderFragmentReference reference)
        => reference.Payload is FilterEffectSegmentRenderFragmentPayload
            or ShaderRenderFragmentPayload { WorkingScalePolicy: not null }
            or ShaderGraphRenderFragmentPayload { WorkingScalePolicy: not null }
            or GeometryRenderFragmentPayload { WorkingScalePolicy: not null };

    internal sealed class UncappedWorkingScaleResolver(float outputScale)
    {
        private readonly Dictionary<RenderFragmentReference, EffectiveScale> _scales = new(ReferenceEqualityComparer.Instance);
        private Func<RenderFragmentReference, EffectiveScale>? _resolveSupply;

        public bool IsClamped(RenderFragmentReference reference, Rect required)
        {
            if (required.IsEmpty)
                return false;

            try
            {
                float previewScale = FitRequiredRegion(reference, reference.EffectiveScale.Value, required);
                if (previewScale < reference.EffectiveScale.Value)
                    return false;

                EffectiveScale uncapped = Resolve(reference);
                return !uncapped.IsUnbounded && uncapped.Value > previewScale
                    && FitRequiredRegion(reference, uncapped.Value, required, useUncappedSupply: true) > previewScale;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ArithmeticException)
            {
                // An extension may be unable to resolve a valid scale for delivery. The preview still has its scale.
                return false;
            }
        }

        public EffectiveScale Resolve(RenderFragmentReference reference)
        {
            // A transform may have capped the supply seen by a filter. Follow its inputs through the same
            // density rules, keeping the resolved geometry and output density fixed.
            if (!_scales.TryGetValue(reference, out EffectiveScale scale))
            {
                scale = ResolveForwardScale(reference, reference.Bounds, outputScale, float.PositiveInfinity,
                    _resolveSupply ??= Resolve);
                _scales.Add(reference, scale);
            }

            return scale;
        }

        private float FitRequiredRegion(RenderFragmentReference reference, float density, Rect required,
            bool useUncappedSupply = false)
            => reference.Payload switch
            {
                ShaderGraphRenderFragmentPayload graph => graph.ClampDensity(reference.Inputs[0], density, required,
                    useUncappedSupply ? Resolve(reference.Inputs[0]) : null),
                FilterEffectSegmentRenderFragmentPayload => BufferDimensionBudget.EngineCeiling
                    .ClampWorkingScaleToExactFootprint(required, density),
                _ => density,
            };
    }
}
