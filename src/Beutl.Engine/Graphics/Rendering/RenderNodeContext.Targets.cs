using System.Collections.Immutable;
using Beutl.Graphics.Rendering.Requests;

namespace Beutl.Graphics.Rendering;

public sealed partial class RenderNodeContext
{
    /// <summary>Records a declared capture of the active target.</summary>
    /// <param name="description">The non-null immutable capture region, bounds, scale, and access contract.</param>
    /// <returns>
    /// A new transaction-scoped, non-contributing value fragment that contains the captured pixels when executed.
    /// The result is not published automatically.
    /// </returns>
    /// <remarks>The captured value is request-owned until it is released or transferred to an accepted cache.</remarks>
    public RenderFragmentHandle TargetCapture(TargetCaptureDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);
        ValidateDescriptionResources(description.Resources, nameof(description));
        EffectiveScale scale = description.Scale.PreservesTargetSupply
            ? EffectiveScale.Unbounded
            : description.Scale.ResolveDeclared(
                description.Bounds,
                OutputScale,
                MaxWorkingScale);
        return GetTransaction().CreateFragment(
            RenderFragmentKind.TargetCapture,
            description.Bounds,
            scale,
            RenderValueCardinality.Single,
            contributesValuesToTarget: false,
            canBeUsedAsValueInput: true,
            hasTargetEffects: true,
            hasOpaqueExternalWork: false,
            inputs: [],
            new TargetCaptureRenderFragmentPayload(description),
            RenderFragmentHitTest.FromContract(description.HitTest, description.Resources));
    }

    internal RenderFragmentHandle BuiltInBackdropCapture(object identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (identity is not IBuiltInBackdropCaptureSink)
        {
            throw new ArgumentException(
                "A built-in backdrop capture identity must accept successful fallback publication.",
                nameof(identity));
        }
        NodeRecordingTransaction transaction = GetTransaction();
        var placeholder = new Rect(0, 0, 1, 1);
        var description = TargetCaptureDescription.Create(
            TargetRegion.Full,
            placeholder,
            RenderHitTestContract.None,
            TargetCaptureScaleContract.PreserveTargetSupply);
        RenderFragmentHandle handle = transaction.CreateFragment(
            RenderFragmentKind.BuiltInBackdropCapture,
            placeholder,
            EffectiveScale.Unbounded,
            RenderValueCardinality.Single,
            contributesValuesToTarget: false,
            canBeUsedAsValueInput: true,
            hasTargetEffects: true,
            hasOpaqueExternalWork: false,
            inputs: [],
            new BuiltInBackdropCaptureRenderFragmentPayload(description, identity),
            RenderFragmentHitTest.FromContract(description.HitTest, description.Resources),
            boundsRequirement: RenderFragmentBoundsRequirement.OwningTargetDomain);
        transaction.BindBuiltInBackdrop(identity, handle);
        return handle;
    }

    internal bool TryBuiltInBackdrop(
        object identity,
        out RenderFragmentHandle? capture)
        => GetTransaction().TryGetBuiltInBackdrop(identity, out capture);

    /// <summary>Records a finite off-screen layer and returns its composited value.</summary>
    /// <param name="inputs">A non-null ordered list of non-null fragments replayed inside the layer.</param>
    /// <param name="domain">The finite logical layer domain.</param>
    /// <param name="domainIsQueryFootprint">
    /// <see langword="true"/> when the layer occupies its whole <paramref name="domain"/> for bounds queries even
    /// where it draws nothing — a fixed-size viewport such as a nested scene, whose layout footprint is the frame
    /// it references rather than its content. Output bounds, rasterization regions, and hit testing stay
    /// content-derived either way; only the queried footprint changes.
    /// </param>
    /// <returns>
    /// A new transaction-scoped single-value fragment. The result is not published automatically and owns no
    /// execution resource itself.
    /// </returns>
    /// <remarks>
    /// A finite Layer is a concrete-metadata barrier. If any input has symbolic recording metadata, the result uses
    /// the complete <paramref name="domain"/> for conservative bounds and hit testing.
    /// </remarks>
    public RenderFragmentHandle Layer(
        IReadOnlyList<RenderFragmentHandle> inputs,
        Rect domain,
        bool domainIsQueryFootprint = false)
    {
        if (!RenderRectValidation.IsFiniteNonNegative(domain)
            || domain.Width == 0
            || domain.Height == 0)
        {
            throw new ArgumentException("A finite Layer domain must be finite and non-empty.", nameof(domain));
        }

        NodeRecordingTransaction transaction = GetTransaction();
        ImmutableArray<RenderFragmentReference> references =
            transaction.GetReferences(inputs, nameof(inputs));
        bool hasConcreteInputMetadata = references.All(
            static reference => reference.HasConcreteRecordingMetadata);
        bool contributes = false;
        Rect bounds = default;
        foreach (RenderFragmentReference reference in references)
        {
            if (reference.ContributesValuesToTarget)
            {
                contributes = true;
                bounds = bounds.Union(reference.Bounds);
            }

            if (TargetWriteMetadataResolver.Resolve(reference, domain) is { } affected)
            {
                contributes = true;
                bounds = bounds.Union(affected);
            }
        }
        bounds = hasConcreteInputMetadata
            ? bounds.Intersect(domain)
            : domain;
        // The layer's own bounds are clipped to the domain above, so a point the domain excludes names
        // content the layer cannot render however far an input's geometry reaches.
        RenderFragmentHitTest hitTest = hasConcreteInputMetadata
            ? RenderFragmentHitTest.RegionAndInputs(domain)
            : RenderFragmentHitTest.Region(domain);
        return transaction.CreateFragment(
            RenderFragmentKind.Layer,
            bounds,
            EffectiveScale.Unbounded,
            RenderValueCardinality.Single,
            contributes,
            canBeUsedAsValueInput: true,
            hasTargetEffects: true,
            hasOpaqueExternalWork: references.Any(static item => item.HasOpaqueExternalWork),
            references,
            new LayerRenderFragmentPayload(domain, domainIsQueryFootprint),
            hitTest);
    }

    /// <summary>
    /// Records an off-screen layer whose finite domain is resolved from its owning target after surrounding
    /// target scopes are known.
    /// </summary>
    /// <param name="inputs">A non-null ordered list of non-null fragments replayed inside the layer.</param>
    /// <returns>
    /// A new transaction-scoped single-value fragment. The result is not published automatically and remains
    /// symbolic until graph-wide target-domain resolution.
    /// </returns>
    /// <remarks>
    /// Use this form when a mixed painter sequence must become value-eligible but no finite domain is available
    /// during recording. Graph finalization rejects the fragment unless an enclosing scope or request supplies a
    /// finite owning target domain.
    /// </remarks>
    public RenderFragmentHandle OwningTargetLayer(
        IReadOnlyList<RenderFragmentHandle> inputs)
    {
        NodeRecordingTransaction transaction = GetTransaction();
        ImmutableArray<RenderFragmentReference> references =
            transaction.GetReferences(inputs, nameof(inputs));
        Rect recordedBounds = CalculateReferenceBounds(references);
        return transaction.CreateFragment(
            RenderFragmentKind.Layer,
            recordedBounds,
            EffectiveScale.Unbounded,
            RenderValueCardinality.Single,
            references.Any(static reference =>
                reference.ContributesValuesToTarget || reference.PotentiallyWritesTarget),
            canBeUsedAsValueInput: true,
            hasTargetEffects: true,
            hasOpaqueExternalWork: references.Any(static item => item.HasOpaqueExternalWork),
            references,
            new LayerRenderFragmentPayload(Domain: null),
            RenderFragmentHitTest.Inputs,
            boundsRequirement: RenderFragmentBoundsRequirement.OwningTargetDomain);
    }

    /// <summary>Records ordered target work scoped to a symbolic target region.</summary>
    /// <param name="inputs">A non-null ordered list of non-null fragments replayed inside the scope.</param>
    /// <param name="region">The target region resolved after surrounding domains are known.</param>
    /// <returns>
    /// A new transaction-scoped, non-value-eligible target-effect fragment. The result is not published
    /// automatically.
    /// </returns>
    public RenderFragmentHandle TargetLayerScope(
        IReadOnlyList<RenderFragmentHandle> inputs,
        TargetRegion region)
    {
        region.ThrowIfUninitialized(nameof(region));
        NodeRecordingTransaction transaction = GetTransaction();
        ImmutableArray<RenderFragmentReference> references =
            transaction.GetReferences(inputs, nameof(inputs));
        return transaction.CreateFragment(
            RenderFragmentKind.TargetLayerScope,
            CalculateReferenceBounds(references),
            EffectiveScale.Unbounded,
            AggregateCardinality(references),
            contributesValuesToTarget: false,
            canBeUsedAsValueInput: false,
            hasTargetEffects: true,
            hasOpaqueExternalWork: references.Any(static item => item.HasOpaqueExternalWork),
            references,
            new TargetLayerScopeRenderFragmentPayload(region),
            CreateTargetLayerScopeHitTest(region),
            hasDirectSymbolicBoundsDependency: region.Kind == TargetRegionKind.Full);
    }

    // A finite region bounds what the scope can put on its target the same way it bounds rasterization, so a
    // point outside it names content this scope cannot render however far an input's geometry reaches. A Full
    // region has no recording-time extent to test against and defers to its inputs, and an Empty one renders
    // nothing at all.
    private static RenderFragmentHitTest CreateTargetLayerScopeHitTest(TargetRegion region)
        => region.Kind switch
        {
            TargetRegionKind.Empty => RenderFragmentHitTest.None,
            TargetRegionKind.Region => RenderFragmentHitTest.RegionAndInputs(region.Value),
            _ => RenderFragmentHitTest.Inputs,
        };

    /// <summary>Records a guarded target scope around one input.</summary>
    /// <param name="input">A non-null fragment borrowed from the active transaction and replayed inside the scope.</param>
    /// <param name="description">
    /// The non-null caller-owned guarded scope contract. Every declared resource must belong to this request family.
    /// </param>
    /// <returns>A new transaction-scoped target scope. The result is not published automatically.</returns>
    public RenderFragmentHandle TargetScope(
        RenderFragmentHandle input,
        TargetScopeDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);
        return RecordTargetScope(input, description, raw: false);
    }

    /// <summary>Records an opaque external target scope around one input.</summary>
    /// <param name="input">A non-null fragment borrowed from the active transaction and replayed inside the scope.</param>
    /// <param name="description">
    /// The non-null caller-owned raw scope contract. Every declared resource must belong to this request family.
    /// </param>
    /// <returns>A new transaction-scoped external-work boundary. The result is not published automatically.</returns>
    public RenderFragmentHandle RawTargetScope(
        RenderFragmentHandle input,
        RawTargetScopeDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);
        return RecordTargetScope(input, description, raw: true);
    }

    /// <summary>Records an opaque external command against the active target.</summary>
    /// <param name="description">
    /// The non-null caller-owned raw command contract. Every declared resource must belong to this request family.
    /// </param>
    /// <returns>A new transaction-scoped external-work boundary. The result is not published automatically.</returns>
    public RenderFragmentHandle RawTargetCommand(RawTargetCommandDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);
        ValidateDescriptionResources(description.Resources, nameof(description));
        return GetTransaction().CreateFragment(
            RenderFragmentKind.RawTargetCommand,
            description.QueryBounds,
            EffectiveScale.Unbounded,
            RenderValueCardinality.None,
            contributesValuesToTarget: false,
            canBeUsedAsValueInput: false,
            hasTargetEffects: true,
            hasOpaqueExternalWork: true,
            inputs: [],
            new RawTargetCommandRenderFragmentPayload(description),
            RenderFragmentHitTest.FromContract(description.HitTest, description.Resources));
    }

    /// <summary>Records a guarded command that consumes declared values and accesses the active target.</summary>
    /// <param name="inputs">
    /// A non-null ordered list of non-null value-eligible fragments borrowed from the active transaction and made
    /// available to the command.
    /// </param>
    /// <param name="description">
    /// The non-null caller-owned guarded command contract. Every declared resource must belong to this request
    /// family.
    /// </param>
    /// <returns>A new transaction-scoped target command. The result is not published automatically.</returns>
    public RenderFragmentHandle TargetCommand(
        IReadOnlyList<RenderFragmentHandle> inputs,
        TargetCommandDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);
        NodeRecordingTransaction transaction = GetTransaction();
        ImmutableArray<RenderFragmentReference> references =
            transaction.GetReferences(inputs, nameof(inputs));
        foreach (RenderFragmentReference reference in references)
            EnsureValueInput(reference, nameof(inputs));
        IReadOnlyList<RenderInputReadback> inputReadbacks = description.ResolveInputReadbacks(
            references.Length,
            nameof(description));
        ValidateDescriptionResources(description.Resources, nameof(description));

        return transaction.CreateFragment(
            RenderFragmentKind.TargetCommand,
            description.QueryBounds,
            EffectiveScale.Unbounded,
            RenderValueCardinality.None,
            contributesValuesToTarget: false,
            canBeUsedAsValueInput: false,
            hasTargetEffects: true,
            hasOpaqueExternalWork: false,
            references,
            new TargetCommandRenderFragmentPayload(description, inputReadbacks),
            RenderFragmentHitTest.FromContract(description.HitTest, description.Resources));
    }

    private RenderFragmentHandle RecordTargetScope(
        RenderFragmentHandle input,
        object description,
        bool raw)
    {
        NodeRecordingTransaction transaction = GetTransaction();
        RenderFragmentReference reference = transaction.GetReference(input);
        RenderBoundsContract boundsContract;
        RenderHitTestContract hitTestContract;
        RenderScaleContract scaleContract;
        IReadOnlyList<RenderResourceBinding> resourceBindings;
        if (description is TargetScopeDescription typed)
        {
            boundsContract = typed.Bounds;
            hitTestContract = typed.HitTest;
            scaleContract = typed.Scale;
            resourceBindings = typed.Resources;
        }
        else if (description is RawTargetScopeDescription rawDescription)
        {
            boundsContract = rawDescription.Bounds;
            hitTestContract = rawDescription.HitTest;
            scaleContract = rawDescription.Scale;
            resourceBindings = rawDescription.Resources;
        }
        else
        {
            throw new ArgumentException("The target scope description type is invalid.", nameof(description));
        }

        ValidateDescriptionResources(resourceBindings, nameof(description));
        Rect bounds = boundsContract.TransformBounds(reference.Bounds);
        EffectiveScale scale = scaleContract.Resolve(
            [reference.EffectiveScale],
            bounds,
            OutputScale,
            MaxWorkingScale);
        bool isValueReplayMap = !raw
            && ((TargetScopeDescription)description).IsValueReplayMap;
        return transaction.CreateFragment(
            raw ? RenderFragmentKind.RawTargetScope : RenderFragmentKind.TargetScope,
            bounds,
            scale,
            reference.ValueCardinality,
            reference.ContributesValuesToTarget,
            canBeUsedAsValueInput: isValueReplayMap
                && reference.CanBeUsedAsValueInput
                && reference.ValueCardinality.Equals(RenderValueCardinality.Single)
                && reference.ContributesValuesToTarget
                && !RenderFragmentTargetDependency.HasExternalTargetDependency(reference),
            hasTargetEffects: isValueReplayMap ? reference.HasTargetEffects : true,
            hasOpaqueExternalWork: raw || reference.HasOpaqueExternalWork,
            [reference],
            raw
                ? new RawTargetScopeRenderFragmentPayload((RawTargetScopeDescription)description)
                : new TargetScopeRenderFragmentPayload((TargetScopeDescription)description),
            RenderFragmentHitTest.FromContract(hitTestContract, resourceBindings),
            // A scope whose transform composes against the ambient is recorded over the matrix as declared and
            // rewritten once the graph exists, so the bounds recorded here are provisional by construction.
            hasDirectSymbolicBoundsDependency:
                description is TargetScopeDescription { AmbientTransform.DependsOnAmbient: true });
    }

    private static Rect CalculateReferenceBounds(
        IEnumerable<RenderFragmentReference> references)
    {
        Rect result = default;
        foreach (RenderFragmentReference reference in references)
        {
            result = result.Union(reference.Bounds);
        }

        return result;
    }

    private static RenderValueCardinality AggregateCardinality(
        IEnumerable<RenderFragmentReference> references)
    {
        int minimum = 0;
        int? maximum = 0;
        foreach (RenderFragmentReference reference in references)
        {
            minimum = checked(minimum + reference.ValueCardinality.Minimum);
            maximum = maximum is null || reference.ValueCardinality.Maximum is null
                ? null
                : checked(maximum.Value + reference.ValueCardinality.Maximum.Value);
        }

        return RenderValueCardinality.Range(minimum, maximum);
    }
}
