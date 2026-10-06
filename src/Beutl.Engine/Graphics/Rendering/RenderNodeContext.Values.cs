using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics.Shaders;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public sealed partial class RenderNodeContext
{
    /// <summary>Wraps a value-eligible fragment so its values contribute to target composition when published.</summary>
    /// <param name="input">
    /// A non-null transaction-scoped fragment whose <see cref="RenderFragmentHandle.CanBeUsedAsValueInput"/> is
    /// <see langword="true"/>.
    /// </param>
    /// <returns>
    /// The borrowed original handle when it already contributes; otherwise a new transaction-scoped contributing
    /// handle. The result is not published automatically.
    /// </returns>
    public RenderFragmentHandle ContributeValues(RenderFragmentHandle input)
    {
        NodeRecordingTransaction transaction = GetTransaction();
        RenderFragmentReference reference = transaction.GetReference(input);
        EnsureValueInput(reference, nameof(input));
        if (reference.ContributesValuesToTarget)
            return input;

        return transaction.CreateFragment(
            RenderFragmentKind.ContributeValues,
            reference.Bounds,
            reference.EffectiveScale,
            reference.ValueCardinality,
            contributesValuesToTarget: true,
            canBeUsedAsValueInput: true,
            reference.HasTargetEffects,
            reference.HasOpaqueExternalWork,
            [reference],
            payload: null,
            RenderFragmentHitTest.Inputs);
    }

    /// <summary>Records a deferred premultiplied-opacity scope around one fragment stream.</summary>
    /// <param name="input">A non-null fragment borrowed from the active transaction.</param>
    /// <param name="opacity">A finite opacity value. Values outside [0, 1] are clamped.</param>
    /// <returns>A new transaction-scoped fragment handle. The result is not published automatically.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="opacity"/> is not finite.</exception>
    public RenderFragmentHandle Opacity(RenderFragmentHandle input, float opacity)
    {
        opacity = OpacityRenderNode.Normalize(opacity);

        NodeRecordingTransaction transaction = GetTransaction();
        RenderFragmentReference reference = transaction.GetReference(input);
        return transaction.CreateFragment(
            RenderFragmentKind.Opacity,
            reference.Bounds,
            reference.EffectiveScale,
            reference.ValueCardinality,
            reference.ContributesValuesToTarget,
            reference.CanBeUsedAsValueInput,
            reference.HasTargetEffects,
            reference.HasOpaqueExternalWork,
            [reference],
            new OpacityRenderFragmentPayload(
                opacity,
                OpacityRenderNode.CreateFusionDescription(opacity)),
            RenderFragmentHitTest.Inputs);
    }

    /// <summary>Records a blend-mode boundary around one input.</summary>
    /// <param name="input">A non-null fragment borrowed from the active transaction.</param>
    /// <param name="blendMode">The blend mode applied during target composition.</param>
    /// <returns>A new transaction-scoped blend fragment. The result is not published automatically.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="blendMode"/> is not a defined <see cref="BlendMode"/> value.
    /// </exception>
    public RenderFragmentHandle Blend(RenderFragmentHandle input, BlendMode blendMode)
    {
        if (!Enum.IsDefined(blendMode))
            throw new ArgumentOutOfRangeException(nameof(blendMode), blendMode, "The blend mode is not defined.");

        NodeRecordingTransaction transaction = GetTransaction();
        RenderFragmentReference reference = transaction.GetReference(input);
        return transaction.CreateFragment(
            RenderFragmentKind.Blend,
            reference.Bounds,
            reference.EffectiveScale,
            reference.ValueCardinality,
            reference.ContributesValuesToTarget,
            canBeUsedAsValueInput: false,
            hasTargetEffects: true,
            reference.HasOpaqueExternalWork,
            [reference],
            new BlendRenderFragmentPayload(blendMode),
            RenderFragmentHitTest.Inputs);
    }

    /// <summary>Records an opacity-mask fragment and its declarative brush dependencies.</summary>
    /// <param name="input">A non-null fragment borrowed from the active transaction.</param>
    /// <param name="mask">
    /// The non-null mask resource whose scalar state and declared dependencies are captured during recording.
    /// </param>
    /// <param name="brushBounds">The finite logical coordinate frame used to map the mask brush.</param>
    /// <param name="invert">Whether to invert the sampled mask alpha.</param>
    /// <returns>A new transaction-scoped mask fragment. The result is not published automatically.</returns>
    public RenderFragmentHandle OpacityMask(
        RenderFragmentHandle input,
        RenderResource<Brush.Resource> mask,
        Rect brushBounds,
        bool invert = false)
    {
        ArgumentNullException.ThrowIfNull(mask);
        RenderRectValidation.ThrowIfInvalidInput(brushBounds, nameof(brushBounds));
        NodeRecordingTransaction transaction = GetTransaction();
        RenderFragmentReference reference = transaction.GetReference(input);
        ValidateDeclaredResource(
            mask,
            transaction.Request.Options.Owner.ResourceRegistry,
            nameof(mask));
        return transaction.CreateFragment(
            RenderFragmentKind.OpacityMask,
            reference.Bounds,
            reference.EffectiveScale,
            reference.ValueCardinality,
            reference.ContributesValuesToTarget,
            reference.CanBeUsedAsValueInput,
            reference.HasTargetEffects,
            reference.HasOpaqueExternalWork,
            [reference],
            new OpacityMaskRenderFragmentPayload(
                mask,
                brushBounds,
                invert),
            RenderFragmentHitTest.Inputs);
    }

    /// <summary>Records a deferred shader transformation over one value-eligible fragment.</summary>
    /// <param name="input">
    /// A non-null transaction-scoped fragment whose <see cref="RenderFragmentHandle.CanBeUsedAsValueInput"/> is
    /// <see langword="true"/>.
    /// </param>
    /// <param name="description">
    /// The non-null caller-owned immutable shader contract. Every declared resource must belong to this request
    /// family.
    /// </param>
    /// <returns>A new transaction-scoped shader fragment. The result is not published automatically.</returns>
    public RenderFragmentHandle Shader(
        RenderFragmentHandle input,
        ShaderDescription description)
        => Shader(input, description, workingScalePolicy: null);

    internal RenderFragmentHandle Shader(
        RenderFragmentHandle input,
        ShaderDescription description,
        FilterEffectWorkingScalePolicy? workingScalePolicy)
    {
        ArgumentNullException.ThrowIfNull(description);
        NodeRecordingTransaction transaction = GetTransaction();
        RenderFragmentReference reference = transaction.GetReference(input);
        EnsureValueInput(reference, nameof(input));
        ValidateDescriptionResources(
            description.Resources.SelectToArray(static binding => binding.Resource),
            nameof(description));

        Rect bounds = description.Bounds.TransformBounds(reference.Bounds);
        bool materializes = description.Kind == ShaderDescriptionKind.WholeSource;
        EffectiveScale scale = workingScalePolicy is null && !materializes
            ? reference.EffectiveScale
            : ResolveMaterializingScale(reference, bounds, workingScalePolicy);

        return transaction.CreateFragment(
            RenderFragmentKind.Shader,
            bounds,
            scale,
            reference.ValueCardinality,
            reference.ContributesValuesToTarget,
            canBeUsedAsValueInput: true,
            reference.HasTargetEffects,
            reference.HasOpaqueExternalWork,
            [reference],
            new ShaderRenderFragmentPayload(
                description,
                workingScalePolicy),
            description.CreateFragmentHitTest());
    }

    /// <summary>Records a deferred geometry callback over one value-eligible fragment.</summary>
    /// <param name="input">
    /// A non-null transaction-scoped fragment whose <see cref="RenderFragmentHandle.CanBeUsedAsValueInput"/> is
    /// <see langword="true"/>.
    /// </param>
    /// <param name="description">
    /// The non-null caller-owned immutable geometry contract. Every declared resource must belong to this request
    /// family.
    /// </param>
    /// <returns>A new transaction-scoped geometry fragment. The result is not published automatically.</returns>
    public RenderFragmentHandle Geometry(
        RenderFragmentHandle input,
        GeometryDescription description)
        => Geometry(input, description, workingScalePolicy: null);

    internal RenderFragmentHandle Geometry(
        RenderFragmentHandle input,
        GeometryDescription description,
        FilterEffectWorkingScalePolicy? workingScalePolicy)
    {
        ArgumentNullException.ThrowIfNull(description);
        NodeRecordingTransaction transaction = GetTransaction();
        RenderFragmentReference reference = transaction.GetReference(input);
        EnsureValueInput(reference, nameof(input));
        ValidateDescriptionResources(description.Resources, nameof(description));

        Rect bounds = description.Bounds.TransformBounds(reference.Bounds);
        EffectiveScale scale = ResolveMaterializingScale(reference, bounds, workingScalePolicy);

        RenderValueCardinality cardinality = RenderValueCardinality.Range(
            minimum: 0,
            maximum: reference.ValueCardinality.Maximum);
        return transaction.CreateFragment(
            RenderFragmentKind.Geometry,
            bounds,
            scale,
            cardinality,
            reference.ContributesValuesToTarget,
            canBeUsedAsValueInput: true,
            reference.HasTargetEffects,
            reference.HasOpaqueExternalWork,
            [reference],
            new GeometryRenderFragmentPayload(
                description,
                workingScalePolicy),
            RenderFragmentHitTest.FromContract(description.HitTest, description.Resources));
    }

    // A working-scale policy decides the scale; without one the fragment materializes at the working scale its
    // input supply and the output allow, clamped so its exact footprint fits the engine's buffer ceiling.
    private EffectiveScale ResolveMaterializingScale(
        RenderFragmentReference reference,
        Rect bounds,
        FilterEffectWorkingScalePolicy? workingScalePolicy)
    {
        if (workingScalePolicy is { } policy)
        {
            return policy.Resolve(
                [reference],
                bounds,
                OutputScale,
                MaxWorkingScale);
        }

        float workingScale = RenderScaleUtilities.ResolveWorkingScale(
            [reference.EffectiveScale],
            OutputScale,
            MaxWorkingScale);
        workingScale = BufferDimensionBudget.EngineCeiling.ClampWorkingScaleToExactFootprint(bounds, workingScale);
        return EffectiveScale.At(workingScale);
    }
}
