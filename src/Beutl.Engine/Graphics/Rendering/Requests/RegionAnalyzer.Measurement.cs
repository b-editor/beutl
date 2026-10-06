namespace Beutl.Graphics.Rendering.Requests;

internal sealed partial class RegionAnalyzer
{
    private static RenderNodeMeasurement Measure(
        RenderRequestOptions options,
        IReadOnlyList<RenderFragmentReference> roots)
    {
        Rect outputBounds = default;
        Rect queryBounds = default;
        int minimum = 0;
        int? maximum = 0;
        float densestSupply = 0;
        bool hasContributingValues = false;
        bool hasTargetEffects = false;

        foreach (RenderFragmentReference root in roots)
        {
            minimum = checked(minimum + root.ValueCardinality.Minimum);
            maximum = maximum is null || root.ValueCardinality.Maximum is null
                ? null
                : checked(maximum.Value + root.ValueCardinality.Maximum.Value);
            hasContributingValues |= root.ContributesValuesToTarget;
            hasTargetEffects |= root.HasTargetEffects;

            if (root.ContributesValuesToTarget)
                outputBounds = outputBounds.Union(root.Bounds);
            if (TargetWriteMetadataResolver.Resolve(root, options.TargetDomain) is { } affected)
                outputBounds = outputBounds.Union(affected);
            queryBounds = queryBounds.Union(ResolveQueryBounds(root));

            if (!root.EffectiveScale.IsUnbounded)
                densestSupply = MathF.Max(densestSupply, root.EffectiveScale.Value);
        }

        if (options.TargetDomain is { } targetDomain)
            outputBounds = outputBounds.Intersect(targetDomain);

        EffectiveScale effectiveScale = densestSupply > 0
            ? EffectiveScale.At(densestSupply)
            : EffectiveScale.Unbounded;
        return new RenderNodeMeasurement(
            outputBounds,
            queryBounds,
            effectiveScale,
            RenderValueCardinality.Range(minimum, maximum),
            roots.Count > 0,
            hasContributingValues,
            hasTargetEffects);
    }

    private static Rect ResolveQueryBounds(RenderFragmentReference reference)
    {
        if (reference.Kind is RenderFragmentKind.TargetCapture
            or RenderFragmentKind.BuiltInBackdropCapture)
        {
            return reference.ContributesValuesToTarget ? reference.Bounds : Rect.Empty;
        }

        if (reference.Payload is TargetCommandRenderFragmentPayload command)
            return command.Description.QueryBounds;
        if (reference.Payload is RawTargetCommandRenderFragmentPayload rawCommand)
            return rawCommand.Description.QueryBounds;
        if (reference.Payload is LayerRenderFragmentPayload layerPayload)
        {
            if (layerPayload is { DomainIsQueryFootprint: true, Domain: { } queryFootprint })
                return queryFootprint;

            Rect layerQuery = Rect.Empty;
            foreach (RenderFragmentReference input in reference.Inputs)
                layerQuery = layerQuery.Union(ResolveQueryBounds(input));
            return layerQuery.Intersect(layerPayload.Domain ?? reference.Bounds);
        }
        if (reference.ContributesValuesToTarget)
            return reference.Bounds.Union(ResolveDeclaredQueryFootprint(reference));
        if (reference.Kind == RenderFragmentKind.OpacityMask)
        {
            return reference.Inputs.IsDefaultOrEmpty
                ? Rect.Empty
                : ResolveQueryBounds(reference.Inputs[0]);
        }

        Rect result = default;
        foreach (RenderFragmentReference input in reference.Inputs)
            result = result.Union(ResolveQueryBounds(input));

        return MapQueryBoundsThroughScope(reference, result);
    }

    // A fixed-size viewport declares a query footprint wider than what it draws, and every value-contributing
    // ancestor above it reports only its own content-derived bounds. The footprint is therefore resolved on its
    // own descent, mapped through the same scopes the ordinary descent maps through.
    private static Rect ResolveDeclaredQueryFootprint(RenderFragmentReference reference)
    {
        if (reference.Payload is LayerRenderFragmentPayload
            { DomainIsQueryFootprint: true, Domain: { } queryFootprint })
        {
            return queryFootprint;
        }

        Rect result = default;
        foreach (RenderFragmentReference input in reference.Inputs)
            result = result.Union(ResolveDeclaredQueryFootprint(input));

        return MapQueryBoundsThroughScope(reference, result);
    }

    private static Rect MapQueryBoundsThroughScope(RenderFragmentReference reference, Rect result)
    {
        if (result.Width == 0 || result.Height == 0)
            return Rect.Empty;

        return reference.Payload switch
        {
            TargetScopeRenderFragmentPayload scope
                => scope.Description.Bounds.TransformBounds(result),
            RawTargetScopeRenderFragmentPayload scope
                => scope.Description.Bounds.TransformBounds(result),
            TargetLayerScopeRenderFragmentPayload layer
                => layer.Region.Kind == TargetRegionKind.Region
                    ? result.Intersect(layer.Region.Value)
                    : layer.Region.Kind == TargetRegionKind.Empty ? Rect.Empty : result,
            _ => result,
        };
    }
}
