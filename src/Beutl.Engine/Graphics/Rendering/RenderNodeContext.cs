using System.Collections.Immutable;
using Beutl.Graphics.Effects;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Records declarative render fragments for one active <see cref="RenderNode.Process(RenderNodeContext)"/> call.
/// </summary>
/// <remarks>
/// The engine creates and seals each transaction. Methods record metadata only; deferred callbacks run later.
/// The context, its borrowed <see cref="Inputs"/>, and all handles obtained from it become invalid when the
/// process call returns. They do not own rendering resources and cannot be retained for a later request.
/// </remarks>
public sealed class RenderNodeContext
{
    private readonly NodeRecordingTransaction _transaction;
    private readonly IReadOnlyList<RenderFragmentHandle> _inputs;
    private readonly RenderIntent _intent;
    private readonly RenderRequestPurpose _purpose;
    private readonly float _outputScale;
    private readonly float _maxWorkingScale;

    internal RenderNodeContext(NodeRecordingTransaction transaction)
    {
        _transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
        _inputs = transaction.Inputs;
        _intent = transaction.Request.Options.Intent;
        _purpose = transaction.Request.Options.Purpose;
        _outputScale = transaction.Request.Options.OutputScale;
        _maxWorkingScale = transaction.Request.Options.MaxWorkingScale;
    }

    /// <summary>Gets the non-null ordered fragment inputs borrowed by the current node transaction.</summary>
    public IReadOnlyList<RenderFragmentHandle> Inputs
    {
        get { VerifyActive(); return _inputs; }
    }

    /// <summary>Gets the render intent of the current request.</summary>
    public RenderIntent Intent
    {
        get { VerifyActive(); return _intent; }
    }

    /// <summary>Gets the purpose of the current request.</summary>
    public RenderRequestPurpose Purpose
    {
        get { VerifyActive(); return _purpose; }
    }

    /// <summary>Gets whether the current transaction remains eligible for persistent render caching.</summary>
    public bool IsRenderCacheEnabled
    {
        get { VerifyActive(); return _transaction.IsRenderCacheEnabled; }
    }

    /// <summary>
    /// Gets the positive finite final output density in device pixels per root logical unit.
    /// </summary>
    /// <remarks>This is informational for intermediate values and does not clamp their working density.</remarks>
    public float OutputScale
    {
        get { VerifyActive(); return _outputScale; }
    }

    /// <summary>
    /// Gets the sanitized request-wide ceiling for intermediate working densities.
    /// </summary>
    /// <remarks>The value is positive finite or positive infinity.</remarks>
    public float MaxWorkingScale
    {
        get { VerifyActive(); return _maxWorkingScale; }
    }

    /// <summary>Tries to calculate the union of all current input bounds from concrete recording metadata.</summary>
    /// <param name="bounds">
    /// Receives the logical input-bounds union, or <see langword="default"/> when any input still depends on an
    /// unresolved owning target domain. An empty input list succeeds with an empty rectangle.
    /// </param>
    /// <returns><see langword="true"/> when every input has concrete recording metadata.</returns>
    /// <remarks>This method does not execute deferred work or resolve graph-wide regions of interest.</remarks>
    public bool TryCalculateInputBounds(out Rect bounds)
    {
        VerifyActive();
        Rect result = default;
        foreach (RenderFragmentHandle input in _inputs)
        {
            RenderFragmentReference reference = _transaction.GetReference(input);
            if (!reference.HasConcreteRecordingMetadata)
            {
                bounds = default;
                return false;
            }

            result = result.Union(reference.RecordedBounds);
        }

        bounds = result;
        return true;
    }

    internal bool TryCalculateFiniteIsolationDomain(out Rect domain)
    {
        VerifyActive();
        Rect result = default;
        foreach (RenderFragmentHandle input in _inputs)
        {
            RenderFragmentReference reference = _transaction.GetReference(input);
            if (reference.ContributesValuesToTarget)
            {
                if (!reference.HasConcreteRecordingMetadata)
                {
                    domain = default;
                    return false;
                }

                result = result.Union(reference.RecordedBounds);
            }

            if (!TargetWriteMetadataResolver.TryResolveFinite(reference, out Rect? affectedBounds))
            {
                domain = default;
                return false;
            }

            if (affectedBounds is { } affected)
                result = result.Union(affected);
        }

        domain = result;
        return true;
    }

    /// <summary>Monotonically disables persistent render caching for the current node transaction.</summary>
    public void DisableRenderCache()
    {
        GetTransaction().DisableRenderCache();
    }

    /// <summary>Publishes every current input unchanged and in order.</summary>
    public void PassThrough() => GetTransaction().PassThrough();

    /// <summary>Publishes one recorded fragment stream as a node output.</summary>
    /// <param name="fragment">A non-null handle borrowed from the active transaction.</param>
    public void Publish(RenderFragmentHandle fragment)
        => GetTransaction().Publish(fragment);

    /// <summary>Publishes recorded fragment streams in enumeration order.</summary>
    /// <param name="fragments">A non-null sequence of non-null handles borrowed from the active transaction.</param>
    public void PublishRange(IEnumerable<RenderFragmentHandle> fragments)
    {
        ArgumentNullException.ThrowIfNull(fragments);
        NodeRecordingTransaction transaction = GetTransaction();
        foreach (RenderFragmentHandle fragment in fragments)
        {
            transaction.Publish(fragment);
        }
    }

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
            reference.HitTest);
    }

    /// <summary>Records a deferred premultiplied-opacity scope around one fragment stream.</summary>
    /// <param name="input">A non-null fragment borrowed from the active transaction.</param>
    /// <param name="opacity">A finite opacity value.</param>
    /// <returns>A new transaction-scoped fragment handle. The result is not published automatically.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="opacity"/> is not finite.</exception>
    public RenderFragmentHandle Opacity(RenderFragmentHandle input, float opacity)
    {
        if (!float.IsFinite(opacity))
            throw new ArgumentOutOfRangeException(nameof(opacity), opacity, "Opacity must be finite.");

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
            reference.HitTest);
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
            reference.HitTest);
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
        Brush.Resource mask,
        Rect brushBounds,
        bool invert = false)
    {
        ArgumentNullException.ThrowIfNull(mask);
        RenderRectValidation.ThrowIfInvalidInput(brushBounds, nameof(brushBounds));
        NodeRecordingTransaction transaction = GetTransaction();
        RenderFragmentReference reference = transaction.GetReference(input);
        RecordedBrushPlan maskPlan = BrushRecorder.RecordMask(this, mask, mask.Version, brushBounds);
        ImmutableArray<RenderFragmentReference> dependencies =
            transaction.GetReferences(maskPlan.Dependencies, nameof(mask));
        var inputs = ImmutableArray.CreateBuilder<RenderFragmentReference>(1 + dependencies.Length);
        inputs.Add(reference);
        inputs.AddRange(dependencies);
        return transaction.CreateFragment(
            RenderFragmentKind.OpacityMask,
            reference.Bounds,
            reference.EffectiveScale,
            reference.ValueCardinality,
            reference.ContributesValuesToTarget,
            reference.CanBeUsedAsValueInput && !maskPlan.IsRawExternal,
            reference.HasTargetEffects
                || dependencies.Any(static dependency => dependency.HasTargetEffects),
            reference.HasOpaqueExternalWork || maskPlan.IsRawExternal,
            inputs,
            new OpacityMaskRenderFragmentPayload(
                maskPlan.Brush,
                maskPlan.Resources,
                brushBounds,
                invert,
                maskPlan.IsRawExternal),
            reference.HitTest);
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
            description.Resources.Select(static binding => binding.Resource).ToArray(),
            nameof(description));

        Rect bounds = description.Bounds.TransformBounds(reference.Bounds);
        bool materializes = description.Kind == ShaderDescriptionKind.WholeSource;
        EffectiveScale scale;
        if (workingScalePolicy is { } policy)
        {
            scale = policy.Resolve(
                [reference],
                bounds,
                OutputScale,
                MaxWorkingScale);
        }
        else if (materializes)
        {
            float workingScale = RenderScaleUtilities.ResolveWorkingScale(
                [reference.EffectiveScale],
                OutputScale,
                MaxWorkingScale);
            workingScale = RenderScaleUtilities.ClampWorkingScaleToBufferBudget(bounds, workingScale);
            scale = EffectiveScale.At(workingScale);
        }
        else
        {
            scale = reference.EffectiveScale;
        }

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
                description.CreateRuntimeIdentity(),
                workingScalePolicy),
            reference.HitTest);
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
        EffectiveScale scale;
        if (workingScalePolicy is { } policy)
        {
            scale = policy.Resolve(
                [reference],
                bounds,
                OutputScale,
                MaxWorkingScale);
        }
        else
        {
            float workingScale = RenderScaleUtilities.ResolveWorkingScale(
                [reference.EffectiveScale],
                OutputScale,
                MaxWorkingScale);
            workingScale = RenderScaleUtilities.ClampWorkingScaleToBufferBudget(bounds, workingScale);
            scale = EffectiveScale.At(workingScale);
        }

        Func<Point, bool> hitTest = CreateHitTest(description.HitTest, bounds, [reference]);
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
                description.RuntimeIdentity?.Key ?? new object(),
                workingScalePolicy),
            hitTest);
    }

    /// <summary>Records an opaque value source whose callback runs only during execution.</summary>
    /// <param name="description">
    /// A non-null caller-owned source-topology description whose declared resources belong to this request family.
    /// </param>
    /// <returns>A new transaction-scoped source fragment. The result is not published automatically.</returns>
    public RenderFragmentHandle OpaqueSource(OpaqueRenderDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);
        description.ThrowIfIncompatible(OpaqueRenderTopology.Source, nameof(description));
        ValidateDescriptionResources(description.Resources, nameof(description));

        Rect bounds = description.Bounds.TransformBounds([]);
        EffectiveScale scale = description.Scale.Resolve([], bounds, OutputScale, MaxWorkingScale);
        Func<Point, bool> hitTest = CreateHitTest(description.HitTest, bounds, []);
        return GetTransaction().CreateFragment(
            RenderFragmentKind.OpaqueSource,
            bounds,
            scale,
            description.ValueCardinality,
            contributesValuesToTarget: true,
            canBeUsedAsValueInput: true,
            hasTargetEffects: false,
            hasOpaqueExternalWork: description.DirectReplay is null,
            inputs: null,
            new OpaqueRenderFragmentPayload(OpaqueRenderTopology.Source, description),
            hitTest);
    }

    /// <summary>Records an opaque one-input value transformation.</summary>
    /// <param name="input">A non-null value-eligible fragment borrowed from the active transaction.</param>
    /// <param name="description">
    /// A non-null caller-owned map-topology description whose declared resources belong to this request family.
    /// </param>
    /// <returns>A new transaction-scoped opaque fragment. The result is not published automatically.</returns>
    public RenderFragmentHandle OpaqueMap(
        RenderFragmentHandle input,
        OpaqueRenderDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);
        NodeRecordingTransaction transaction = GetTransaction();
        RenderFragmentReference reference = transaction.GetReference(input);
        EnsureValueInput(reference, nameof(input));
        description.ThrowIfIncompatible(OpaqueRenderTopology.Map, nameof(description));
        ValidateDescriptionResources(description.Resources, nameof(description));

        Rect bounds = description.Bounds.TransformBounds([reference.Bounds]);
        EffectiveScale scale = description.Scale.Resolve(
            [reference.EffectiveScale],
            bounds,
            OutputScale,
            MaxWorkingScale);
        RenderValueCardinality cardinality = description.ValueCardinality.Equals(RenderValueCardinality.Single)
            ? reference.ValueCardinality
            : RenderValueCardinality.Range(0, reference.ValueCardinality.Maximum);
        Func<Point, bool> hitTest = CreateHitTest(description.HitTest, bounds, [reference]);
        return transaction.CreateFragment(
            RenderFragmentKind.OpaqueMap,
            bounds,
            scale,
            cardinality,
            reference.ContributesValuesToTarget,
            canBeUsedAsValueInput: true,
            hasTargetEffects: reference.HasTargetEffects,
            hasOpaqueExternalWork: true,
            [reference],
            new OpaqueRenderFragmentPayload(OpaqueRenderTopology.Map, description),
            hitTest);
    }

    /// <summary>Records an opaque many-input combination.</summary>
    /// <param name="inputs">
    /// A non-null ordered list of non-null value-eligible fragments borrowed from the active transaction.
    /// </param>
    /// <param name="description">
    /// A non-null caller-owned combine-topology description whose declared resources belong to this request family.
    /// </param>
    /// <returns>A new transaction-scoped opaque fragment. The result is not published automatically.</returns>
    public RenderFragmentHandle OpaqueCombine(
        IReadOnlyList<RenderFragmentHandle> inputs,
        OpaqueRenderDescription description)
        => RecordOpaqueMany(inputs, description, OpaqueRenderTopology.Combine);

    /// <summary>Records an opaque many-input fragment that may expand value cardinality.</summary>
    /// <param name="inputs">
    /// A non-null ordered list of non-null value-eligible fragments borrowed from the active transaction.
    /// </param>
    /// <param name="description">
    /// A non-null caller-owned expand-topology description whose declared resources belong to this request family.
    /// </param>
    /// <returns>A new transaction-scoped opaque fragment. The result is not published automatically.</returns>
    public RenderFragmentHandle OpaqueExpand(
        IReadOnlyList<RenderFragmentHandle> inputs,
        OpaqueRenderDescription description)
        => RecordOpaqueMany(inputs, description, OpaqueRenderTopology.Expand);

    internal RenderFragmentHandle LegacyFilterEffect(
        IReadOnlyList<RenderFragmentHandle> inputs,
        RenderResource<FilterEffectContext> effectContext,
        Rect outputBounds,
        bool requiresOwningTargetDomain = false,
        IReadOnlyList<IFEItem>? boundsItems = null,
        FilterEffectWorkingScalePolicy? workingScalePolicy = null)
    {
        ArgumentNullException.ThrowIfNull(effectContext);
        NodeRecordingTransaction transaction = GetTransaction();
        ImmutableArray<RenderFragmentReference> references =
            transaction.GetReferences(inputs, nameof(inputs));
        foreach (RenderFragmentReference reference in references)
            EnsureValueInput(reference, nameof(inputs));
        ValidateDescriptionResources([effectContext], nameof(effectContext));

        RenderRectValidation.ThrowIfInvalidInput(outputBounds, nameof(effectContext));
        IReadOnlyList<IFEItem> recordedBoundsItems = boundsItems ?? [];
        Rect[] bufferBounds = FilterEffectWorkingScalePolicy.CalculateLegacyBufferBounds(
            references.Select(static item => item.Bounds).ToArray(),
            recordedBoundsItems,
            outputBounds);
        EffectiveScale scale;
        if (workingScalePolicy is { } policy)
        {
            scale = policy.Resolve(
                references.Select(static item => item.EffectiveScale).ToArray(),
                references.Select(static item => item.Bounds).ToArray(),
                bufferBounds,
                OutputScale,
                MaxWorkingScale);
        }
        else
        {
            scale = FilterEffectWorkingScalePolicy.ResolveMaterialized(
                references.Select(static item => item.EffectiveScale).ToArray(),
                bufferBounds,
                OutputScale,
                MaxWorkingScale);
        }

        return transaction.CreateFragment(
            RenderFragmentKind.LegacyFilterEffect,
            outputBounds,
            scale,
            RenderValueCardinality.Dynamic,
            references.Any(static item => item.ContributesValuesToTarget),
            canBeUsedAsValueInput: true,
            references.Any(static item => item.HasTargetEffects),
            hasOpaqueExternalWork: true,
            references,
            new LegacyFilterEffectRenderFragmentPayload(
                effectContext,
                [.. recordedBoundsItems],
                workingScalePolicy),
            outputBounds.Contains,
            requiresOwningTargetDomain
                ? RenderFragmentBoundsRequirement.OwningTargetDomain
                : RenderFragmentBoundsRequirement.Finite);
    }

    /// <summary>Records a declared render target as an existing materialized value without copying it.</summary>
    /// <param name="description">
    /// The non-null immutable target, bounds, concrete density, and hit-test contract. The target resource must
    /// belong to this request family; its resource registration determines disposal ownership.
    /// </param>
    /// <returns>A new transaction-scoped materialized input. The result is not published automatically.</returns>
    public RenderFragmentHandle MaterializedInput(MaterializedInputDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);
        ValidateDescriptionResources([description.Target], nameof(description));
        Func<Point, bool> hitTest = CreateHitTest(description.HitTest, description.Bounds, []);
        return GetTransaction().CreateFragment(
            RenderFragmentKind.MaterializedInput,
            description.Bounds,
            description.EffectiveScale,
            RenderValueCardinality.Single,
            contributesValuesToTarget: true,
            canBeUsedAsValueInput: true,
            hasTargetEffects: false,
            hasOpaqueExternalWork: false,
            inputs: null,
            new MaterializedInputRenderFragmentPayload(description),
            hitTest);
    }

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
        EffectiveScale scale = description.Scale.Resolve(
            [],
            description.Bounds,
            OutputScale,
            MaxWorkingScale);
        Func<Point, bool> hitTest = CreateHitTest(description.HitTest, description.Bounds, []);
        return GetTransaction().CreateFragment(
            RenderFragmentKind.TargetCapture,
            description.Bounds,
            scale,
            RenderValueCardinality.Single,
            contributesValuesToTarget: false,
            canBeUsedAsValueInput: true,
            hasTargetEffects: true,
            hasOpaqueExternalWork: false,
            inputs: null,
            new TargetCaptureRenderFragmentPayload(description),
            hitTest);
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
            RenderScaleContract.MaterializeAtWorkingScale);
        RenderFragmentHandle handle = transaction.CreateFragment(
            RenderFragmentKind.BuiltInBackdropCapture,
            placeholder,
            EffectiveScale.Unbounded,
            RenderValueCardinality.Single,
            contributesValuesToTarget: false,
            canBeUsedAsValueInput: true,
            hasTargetEffects: true,
            hasOpaqueExternalWork: false,
            inputs: null,
            new BuiltInBackdropCaptureRenderFragmentPayload(description, identity),
            hitTest: null,
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
        Rect domain)
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
        Func<Point, bool> hitTest = hasConcreteInputMetadata
            ? point => references.Any(item => item.HitTest(point))
            : domain.Contains;
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
            new LayerRenderFragmentPayload(domain),
            hitTest);
    }

    internal RenderFragmentHandle OwningTargetLayer(
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
            point => references.Any(item => item.HitTest(point)),
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
            point => references.Any(item => item.HitTest(point)));
    }

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
        Func<Point, bool> hitTest = CreateHitTest(
            description.HitTest,
            description.QueryBounds,
            []);
        return GetTransaction().CreateFragment(
            RenderFragmentKind.RawTargetCommand,
            description.QueryBounds,
            EffectiveScale.Unbounded,
            RenderValueCardinality.None,
            contributesValuesToTarget: false,
            canBeUsedAsValueInput: false,
            hasTargetEffects: true,
            hasOpaqueExternalWork: true,
            inputs: null,
            new RawTargetCommandRenderFragmentPayload(description),
            hitTest);
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
        ValidateDescriptionResources(description.Resources, nameof(description));

        Func<Point, bool> hitTest = CreateHitTest(
            description.HitTest,
            description.QueryBounds,
            references);
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
            new TargetCommandRenderFragmentPayload(description),
            hitTest);
    }

    /// <summary>Records a root and its descendants into the current request without executing them.</summary>
    /// <param name="root">The non-null caller-owned subtree root.</param>
    /// <returns>A non-null borrowed list of the subtree's transaction-scoped outputs.</returns>
    public IReadOnlyList<RenderFragmentHandle> RecordSubtree(RenderNode root)
        => GetTransaction().RecordNode(root, [], subtree: true);

    /// <summary>Records another node with explicit inputs into the current request.</summary>
    /// <param name="node">The non-null caller-owned node to record.</param>
    /// <param name="inputs">A non-null ordered list of non-null inputs remapped into the child transaction.</param>
    /// <returns>A non-null borrowed list of the child node's outputs remapped into this transaction.</returns>
    public IReadOnlyList<RenderFragmentHandle> RecordNode(
        RenderNode node,
        IReadOnlyList<RenderFragmentHandle> inputs)
        => GetTransaction().RecordNode(node, inputs, subtree: false);

    internal RecordedNestedRenderTarget RecordNestedTarget(
        RenderNode root,
        Rect targetDomain,
        Rect? requestedRegion = null)
        => RecordNestedTargetCore(
            root,
            targetDomain,
            requestedRegion,
            workingScale: null);

    internal RecordedNestedRenderTarget RecordNestedTargetAtScale(
        RenderNode root,
        Rect targetDomain,
        float workingScale,
        Rect? requestedRegion = null)
        => RecordNestedTargetCore(
            root,
            targetDomain,
            requestedRegion,
            workingScale);

    private RecordedNestedRenderTarget RecordNestedTargetCore(
        RenderNode root,
        Rect targetDomain,
        Rect? requestedRegion,
        float? workingScale)
    {
        ArgumentNullException.ThrowIfNull(root);
        var binding = new NestedRenderTargetBinding();
        RenderResource<NestedRenderTargetBinding>? bindingResource = null;
        NodeRecordingTransaction transaction = GetTransaction();
        try
        {
            bindingResource = transaction.Own(binding, cacheKey: null, version: 0);
            RenderRequestOptions nestedOptions = workingScale is { } scale
                ? transaction.Request.Options.CreateNestedAtScale(
                    binding,
                    scale,
                    targetDomain,
                    requestedRegion ?? targetDomain)
                : transaction.Request.Options.CreateNested(
                    binding,
                    targetDomain,
                    requestedRegion ?? targetDomain);
            RecordedNestedRenderRequest recording = transaction.RecordNestedRequest(
                root,
                nestedOptions);
            return new RecordedNestedRenderTarget(recording, bindingResource, binding);
        }
        catch
        {
            if (bindingResource is not null)
                transaction.RollbackResources([bindingResource]);
            else
                binding.Dispose();
            throw;
        }
    }

    /// <summary>Transfers a disposable resource to the current request family.</summary>
    /// <typeparam name="T">The disposable resource type.</typeparam>
    /// <param name="resource">The non-null resource whose ownership is transferred.</param>
    /// <param name="cacheKey">
    /// An optional equality-stable cache identity. <see langword="null"/> creates a distinct request-local identity.
    /// </param>
    /// <param name="version">The pixel-affecting resource version.</param>
    /// <returns>A non-null declared resource handle owned by the request family.</returns>
    /// <remarks>
    /// Ownership transfers when this method succeeds. The family disposes the resource exactly once on rollback,
    /// failure, or normal completion unless an accepted cache transfer explicitly assumes ownership.
    /// </remarks>
    public RenderResource<T> Own<T>(T resource, object? cacheKey = null, long version = 0)
        where T : class, IDisposable
        => GetTransaction().Own(resource, cacheKey, version);

    /// <summary>Registers a caller-owned resource that the current request may borrow.</summary>
    /// <typeparam name="T">The resource type.</typeparam>
    /// <param name="resource">The non-null caller-owned resource.</param>
    /// <param name="cacheKey">
    /// An optional equality-stable coalescing identity. <see langword="null"/> creates a distinct request-local
    /// registration and never coalesces.
    /// </param>
    /// <param name="version">The pixel-affecting resource version.</param>
    /// <returns>A non-null declared resource handle that never transfers disposal ownership.</returns>
    /// <remarks>The request borrows the resource only for its active family and never disposes it.</remarks>
    public RenderResource<T> Borrow<T>(T resource, object? cacheKey = null, long version = 0)
        where T : class
        => GetTransaction().Borrow(resource, cacheKey, version);

    internal void RollbackResources(IReadOnlyList<RenderResource> resources)
        => GetTransaction().RollbackResources(resources);

    internal RenderFragmentMetadata GetRecordedMetadataHint(RenderFragmentHandle fragment)
    {
        RenderFragmentReference reference = GetTransaction().GetReference(fragment);
        return new RenderFragmentMetadata(reference.RecordedBounds, reference.RecordedEffectiveScale);
    }

    internal Func<Point, bool> GetRecordedHitTest(RenderFragmentHandle fragment)
        => GetTransaction().GetReference(fragment).HitTest;

    internal Rect CalculateRecordedInputBoundsHint()
    {
        NodeRecordingTransaction transaction = GetTransaction();
        Rect result = default;
        foreach (RenderFragmentHandle input in _inputs)
        {
            result = result.Union(transaction.GetReference(input).RecordedBounds);
        }

        return result;
    }

    private NodeRecordingTransaction GetTransaction()
    {
        VerifyActive();
        return _transaction;
    }

    private void VerifyActive() => _transaction.VerifyActive();

    private static void EnsureValueInput(RenderFragmentReference reference, string parameterName)
    {
        if (!reference.CanBeUsedAsValueInput)
        {
            throw new ArgumentException(
                "The fragment cannot be consumed as a materialized value input. Use a finite Layer explicitly.",
                parameterName);
        }
    }

    private RenderFragmentHandle RecordOpaqueMany(
        IReadOnlyList<RenderFragmentHandle> inputs,
        OpaqueRenderDescription description,
        OpaqueRenderTopology topology)
    {
        ArgumentNullException.ThrowIfNull(description);
        NodeRecordingTransaction transaction = GetTransaction();
        ImmutableArray<RenderFragmentReference> references =
            transaction.GetReferences(inputs, nameof(inputs));
        foreach (RenderFragmentReference reference in references)
            EnsureValueInput(reference, nameof(inputs));

        description.ThrowIfIncompatible(topology, nameof(description));
        ValidateDescriptionResources(description.Resources, nameof(description));
        Rect bounds = description.Bounds.TransformBounds(
            references.Select(static item => item.Bounds).ToArray());
        EffectiveScale scale = description.Scale.Resolve(
            references.Select(static item => item.EffectiveScale).ToArray(),
            bounds,
            OutputScale,
            MaxWorkingScale);
        Func<Point, bool> hitTest = CreateHitTest(description.HitTest, bounds, references);
        return transaction.CreateFragment(
            topology == OpaqueRenderTopology.Combine
                ? RenderFragmentKind.OpaqueCombine
                : RenderFragmentKind.OpaqueExpand,
            bounds,
            scale,
            description.ValueCardinality,
            references.Any(static item => item.ContributesValuesToTarget),
            canBeUsedAsValueInput: true,
            hasTargetEffects: references.Any(static item => item.HasTargetEffects),
            hasOpaqueExternalWork: true,
            references,
            new OpaqueRenderFragmentPayload(topology, description),
            hitTest);
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
        IReadOnlyList<RenderResource> resources;
        if (description is TargetScopeDescription typed)
        {
            boundsContract = typed.Bounds;
            hitTestContract = typed.HitTest;
            scaleContract = typed.Scale;
            resources = typed.Resources;
        }
        else if (description is RawTargetScopeDescription rawDescription)
        {
            boundsContract = rawDescription.Bounds;
            hitTestContract = rawDescription.HitTest;
            scaleContract = rawDescription.Scale;
            resources = rawDescription.Resources;
        }
        else
        {
            throw new ArgumentException("The target scope description type is invalid.", nameof(description));
        }

        ValidateDescriptionResources(resources, nameof(description));
        Rect bounds = boundsContract.TransformBounds(reference.Bounds);
        EffectiveScale scale = scaleContract.Resolve(
            [reference.EffectiveScale],
            bounds,
            OutputScale,
            MaxWorkingScale);
        Func<Point, bool> hitTest = CreateHitTest(hitTestContract, bounds, [reference]);
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
            hitTest);
    }

    private void ValidateDescriptionResources(
        IReadOnlyList<RenderResource> resources,
        string parameterName)
    {
        NodeRecordingTransaction transaction = GetTransaction();
        foreach (RenderResource resource in resources)
        {
            if (!ReferenceEquals(resource.Registry, transaction.Request.Options.Owner.ResourceRegistry)
                || resource.RegistrationState == RenderResourceRegistrationState.Released)
            {
                throw new ArgumentException(
                    "Every declared render resource must belong to the active request family.",
                    parameterName);
            }
        }
    }

    private static Func<Point, bool> CreateHitTest(
        RenderHitTestContract contract,
        Rect outputBounds,
        IReadOnlyList<RenderFragmentReference> inputs)
    {
        RenderHitTestInput[] views = inputs
            .Select(static item => new RenderHitTestInput(item.Bounds, item.HitTest))
            .ToArray();
        return point => contract.Evaluate(outputBounds, views, point);
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

internal readonly record struct FilterEffectWorkingScalePolicy
{
    public FilterEffectWorkingScalePolicy(RenderScaleContract scale)
    {
        scale.ThrowIfUninitialized(nameof(scale));
        Scale = scale;
    }

    public RenderScaleContract Scale { get; }

    public object StructuralIdentity => Scale.StructuralIdentity;

    public EffectiveScale Resolve(
        IReadOnlyList<RenderFragmentReference> inputs,
        Rect outputBounds,
        float outputScale,
        float maxWorkingScale)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        return Resolve(
            inputs.Select(static input => input.EffectiveScale).ToArray(),
            inputs.Select(static input => input.Bounds).ToArray(),
            outputBounds,
            outputScale,
            maxWorkingScale);
    }

    public EffectiveScale Resolve(
        IReadOnlyList<EffectiveScale> inputSupplies,
        IReadOnlyList<Rect> inputBounds,
        Rect outputBounds,
        float outputScale,
        float maxWorkingScale)
        => Resolve(
            inputSupplies,
            inputBounds,
            Enumerable.Repeat(outputBounds, inputSupplies.Count).ToArray(),
            outputScale,
            maxWorkingScale);

    public EffectiveScale Resolve(
        IReadOnlyList<EffectiveScale> inputSupplies,
        IReadOnlyList<Rect> inputBounds,
        IReadOnlyList<Rect> bufferBounds,
        float outputScale,
        float maxWorkingScale)
    {
        ArgumentNullException.ThrowIfNull(inputSupplies);
        ArgumentNullException.ThrowIfNull(inputBounds);
        ArgumentNullException.ThrowIfNull(bufferBounds);
        if (inputSupplies.Count == 0)
            throw new InvalidOperationException("A filter-effect working-scale policy requires at least one input.");
        if (inputSupplies.Count != inputBounds.Count)
            throw new ArgumentException("Filter-effect input supplies and bounds must have matching cardinality.");
        if (bufferBounds.Count == 0)
            throw new ArgumentException("A filter-effect operation requires at least one buffer footprint.");

        EffectiveScale[] mappedSupplies = new EffectiveScale[inputSupplies.Count];
        for (int index = 0; index < inputSupplies.Count; index++)
        {
            mappedSupplies[index] = Scale.Resolve(
                [inputSupplies[index]],
                inputBounds[index],
                outputScale,
                maxWorkingScale);
        }

        float workingScale = 0;
        bool hasConcreteScale = false;
        foreach (EffectiveScale mappedSupply in mappedSupplies)
        {
            if (mappedSupply.IsUnbounded)
                continue;

            workingScale = hasConcreteScale
                ? MathF.Max(workingScale, mappedSupply.Value)
                : mappedSupply.Value;
            hasConcreteScale = true;
        }

        if (!hasConcreteScale)
        {
            workingScale = MathF.Min(
                outputScale,
                RenderScaleUtilities.SanitizeMaxWorkingScale(maxWorkingScale));
        }
        else
        {
            workingScale = MathF.Min(
                workingScale,
                RenderScaleUtilities.SanitizeMaxWorkingScale(maxWorkingScale));
        }

        return EffectiveScale.At(ClampToBufferBudgets(bufferBounds, workingScale));
    }

    internal static EffectiveScale ResolveMaterialized(
        IReadOnlyList<EffectiveScale> inputSupplies,
        IReadOnlyList<Rect> bufferBounds,
        float outputScale,
        float maxWorkingScale)
    {
        ArgumentNullException.ThrowIfNull(inputSupplies);
        ArgumentNullException.ThrowIfNull(bufferBounds);
        if (inputSupplies.Count == 0)
            throw new InvalidOperationException("A materialized filter-effect operation requires at least one input.");
        if (bufferBounds.Count == 0)
            throw new ArgumentException("A materialized filter-effect operation requires at least one buffer footprint.");

        float workingScale = RenderScaleUtilities.ResolveWorkingScale(
            inputSupplies.ToArray(),
            outputScale,
            maxWorkingScale);
        return EffectiveScale.At(ClampToBufferBudgets(bufferBounds, workingScale));
    }

    internal static Rect[] CalculateLegacyBufferBounds(
        IReadOnlyList<Rect> inputBounds,
        IReadOnlyList<IFEItem> boundsItems,
        Rect fallbackBounds)
    {
        ArgumentNullException.ThrowIfNull(inputBounds);
        ArgumentNullException.ThrowIfNull(boundsItems);
        var result = new List<Rect>();
        int firstCustomIndex = -1;
        for (int index = 0; index < boundsItems.Count; index++)
        {
            if (boundsItems[index] is IFEItem_Custom)
            {
                firstCustomIndex = index;
                break;
            }
        }

        int branchItemCount = firstCustomIndex >= 0 ? firstCustomIndex : boundsItems.Count;
        Rect preCustomAggregateBounds = default;
        var preCustomBranchStates = new List<LegacyFootprintState>(inputBounds.Count);
        foreach (Rect input in inputBounds)
        {
            LegacyFootprintState branchState = CollectLegacyFootprints(
                input,
                boundsItems,
                startIndex: 0,
                itemCount: branchItemCount,
                fallbackBounds,
                result);
            preCustomAggregateBounds = preCustomAggregateBounds.Union(branchState.SemanticBounds);
            preCustomBranchStates.Add(branchState);
        }

        if (firstCustomIndex >= 0)
        {
            var preCustomRetainedBackingOffsets = new List<Rect>();
            foreach (LegacyFootprintState branchState in preCustomBranchStates)
            {
                foreach (Rect offset in branchState.RetainedBackingOffsets)
                {
                    Rect physicalBounds = offset.Translate(branchState.SemanticBounds.Position);
                    preCustomRetainedBackingOffsets.Add(physicalBounds.Translate(new Point(
                        -preCustomAggregateBounds.X,
                        -preCustomAggregateBounds.Y)));
                }
            }

            // A legacy Custom callback can combine or split the complete target list. Collapse from the union of
            // the actual per-target semantic results, not TransformBounds(inputUnion): arbitrary pure mappings need
            // not distribute over Union.
            CollectLegacyFootprints(
                preCustomAggregateBounds,
                boundsItems,
                firstCustomIndex,
                boundsItems.Count - firstCustomIndex,
                fallbackBounds,
                result,
                preCustomRetainedBackingOffsets,
                skipInitialCustomPreFlush: true);
        }

        if (result.Count == 0)
            result.Add(ToLocalLegacyFootprint(fallbackBounds, fallbackBounds));
        return result.ToArray();
    }

    private static LegacyFootprintState CollectLegacyFootprints(
        Rect initialSemanticBounds,
        IReadOnlyList<IFEItem> boundsItems,
        int startIndex,
        int itemCount,
        Rect fallbackBounds,
        List<Rect> result,
        IReadOnlyList<Rect>? initialRetainedBackingOffsets = null,
        bool skipInitialCustomPreFlush = false)
    {
        Rect semanticBounds = initialSemanticBounds;
        Rect allocationBounds = ToLocalLegacyFootprint(semanticBounds, fallbackBounds);
        Rect[] retainedBackingOffsets = initialRetainedBackingOffsets?.ToArray()
            ?? [CreateInitialRetainedBackingOffset(semanticBounds, fallbackBounds)];
        bool hasPendingSkiaWork = false;
        int endIndex = checked(startIndex + itemCount);
        for (int index = startIndex; index < endIndex; index++)
        {
            IFEItem item = boundsItems[index];
            switch (item)
            {
                case IFEItem_Skia:
                    Rect previousSemanticBounds = semanticBounds;
                    if (!semanticBounds.IsInvalid)
                        semanticBounds = item.TransformBounds(semanticBounds);
                    if (!allocationBounds.IsInvalid)
                        allocationBounds = item.TransformBounds(allocationBounds);
                    retainedBackingOffsets = TransformRetainedBackingOffsets(
                        retainedBackingOffsets,
                        previousSemanticBounds,
                        semanticBounds,
                        item,
                        fallbackBounds);
                    hasPendingSkiaWork = true;
                    break;
                case IFEItem_Custom:
                    if (!(skipInitialCustomPreFlush && index == startIndex))
                    {
                        result.Add(NormalizeLegacyAllocationBounds(allocationBounds, fallbackBounds));
                        AddRetainedBackingFootprints(
                            result,
                            semanticBounds,
                            retainedBackingOffsets,
                            fallbackBounds);
                    }
                    if (!semanticBounds.IsInvalid)
                        semanticBounds = item.TransformBounds(semanticBounds);
                    allocationBounds = ToLocalLegacyFootprint(semanticBounds, fallbackBounds);
                    result.Add(allocationBounds);
                    AddRetainedBackingFootprints(
                        result,
                        semanticBounds,
                        retainedBackingOffsets,
                        fallbackBounds);
                    hasPendingSkiaWork = false;
                    break;
                case FEItem_Shader:
                case FEItem_Geometry:
                    if (hasPendingSkiaWork)
                    {
                        result.Add(NormalizeLegacyAllocationBounds(allocationBounds, fallbackBounds));
                        AddRetainedBackingFootprints(
                            result,
                            semanticBounds,
                            retainedBackingOffsets,
                            fallbackBounds);
                    }
                    if (!semanticBounds.IsInvalid)
                        semanticBounds = item.TransformBounds(semanticBounds);
                    allocationBounds = NormalizeLegacySemanticBounds(semanticBounds, fallbackBounds);
                    retainedBackingOffsets =
                        [CreateInitialRetainedBackingOffset(semanticBounds, fallbackBounds)];
                    result.Add(allocationBounds);
                    hasPendingSkiaWork = false;
                    break;
                default:
                    Rect previousDefaultSemanticBounds = semanticBounds;
                    if (!semanticBounds.IsInvalid)
                        semanticBounds = item.TransformBounds(semanticBounds);
                    if (!allocationBounds.IsInvalid)
                        allocationBounds = item.TransformBounds(allocationBounds);
                    retainedBackingOffsets = TransformRetainedBackingOffsets(
                        retainedBackingOffsets,
                        previousDefaultSemanticBounds,
                        semanticBounds,
                        item,
                        fallbackBounds);
                    result.Add(NormalizeLegacyAllocationBounds(allocationBounds, fallbackBounds));
                    AddRetainedBackingFootprints(
                        result,
                        semanticBounds,
                        retainedBackingOffsets,
                        fallbackBounds);
                    hasPendingSkiaWork = false;
                    break;
            }
        }

        Rect normalizedAllocationBounds = NormalizeLegacyAllocationBounds(allocationBounds, fallbackBounds);
        Rect normalizedSemanticBounds = NormalizeLegacySemanticBounds(semanticBounds, fallbackBounds);
        result.Add(normalizedAllocationBounds);
        result.Add(normalizedSemanticBounds);
        result.Add(new Rect(
            normalizedSemanticBounds.Position,
            new Size(
                Math.Max(normalizedAllocationBounds.Width, normalizedSemanticBounds.Width),
                Math.Max(normalizedAllocationBounds.Height, normalizedSemanticBounds.Height))));
        AddRetainedBackingFootprints(
            result,
            normalizedSemanticBounds,
            retainedBackingOffsets,
            fallbackBounds);
        return new LegacyFootprintState(normalizedSemanticBounds, retainedBackingOffsets);
    }

    private static Rect CreateInitialRetainedBackingOffset(
        Rect semanticBounds,
        Rect fallbackBounds)
    {
        Rect normalizedSemanticBounds = NormalizeLegacySemanticBounds(semanticBounds, fallbackBounds);
        Rect scaleOneRasterBounds = PixelRect.FromRect(normalizedSemanticBounds, 1).ToRect(1);
        return scaleOneRasterBounds.Translate(new Point(
            -normalizedSemanticBounds.X,
            -normalizedSemanticBounds.Y));
    }

    private static Rect[] TransformRetainedBackingOffsets(
        IReadOnlyList<Rect> retainedBackingOffsets,
        Rect previousSemanticBounds,
        Rect semanticBounds,
        IFEItem item,
        Rect fallbackBounds)
    {
        Rect previous = NormalizeLegacySemanticBounds(previousSemanticBounds, fallbackBounds);
        Rect current = NormalizeLegacySemanticBounds(semanticBounds, fallbackBounds);
        var result = new Rect[retainedBackingOffsets.Count];
        for (int index = 0; index < retainedBackingOffsets.Count; index++)
        {
            Rect physicalBounds = retainedBackingOffsets[index].Translate(previous.Position);
            Rect transformed = item.TransformBounds(physicalBounds);
            Rect normalized = NormalizeLegacyAllocationBounds(transformed, fallbackBounds);
            result[index] = normalized.Translate(new Point(-current.X, -current.Y));
        }

        return result;
    }

    private static void AddRetainedBackingFootprints(
        List<Rect> result,
        Rect semanticBounds,
        IReadOnlyList<Rect> retainedBackingOffsets,
        Rect fallbackBounds)
    {
        Rect normalizedSemanticBounds = NormalizeLegacySemanticBounds(semanticBounds, fallbackBounds);
        foreach (Rect offset in retainedBackingOffsets)
        {
            result.Add(NormalizeLegacyAllocationBounds(
                offset.Translate(normalizedSemanticBounds.Position),
                fallbackBounds));
        }
    }

    private static Rect NormalizeLegacySemanticBounds(Rect bounds, Rect fallbackBounds)
        => bounds.IsInvalid ? fallbackBounds : bounds;

    private static Rect ToLocalLegacyFootprint(Rect bounds, Rect fallbackBounds)
    {
        Rect normalized = NormalizeLegacySemanticBounds(bounds, fallbackBounds);
        return new Rect(default(Point), normalized.Size);
    }

    private static Rect NormalizeLegacyAllocationBounds(Rect bounds, Rect fallbackBounds)
        => bounds.IsInvalid ? new Rect(default(Point), fallbackBounds.Size) : bounds;

    private static float ClampToBufferBudgets(
        IReadOnlyList<Rect> bufferBounds,
        float workingScale)
    {
        float result = workingScale;
        foreach (Rect bounds in bufferBounds)
        {
            result = MathF.Min(
                result,
                RenderScaleUtilities.ClampWorkingScaleToExactBufferBudget(bounds, workingScale));
        }

        return result;
    }

    private readonly record struct LegacyFootprintState(
        Rect SemanticBounds,
        IReadOnlyList<Rect> RetainedBackingOffsets);
}

internal sealed record OpacityRenderFragmentPayload(
    float Opacity,
    ShaderDescription FusionDescription);

internal sealed record BlendRenderFragmentPayload(BlendMode BlendMode);

internal sealed record OpacityMaskRenderFragmentPayload(
    RecordedBrush Mask,
    IReadOnlyList<RenderResource> Resources,
    Rect BrushBounds,
    bool Invert,
    bool IsRawFallback);

internal sealed record ShaderRenderFragmentPayload(
    ShaderDescription Description,
    object RuntimeIdentity,
    FilterEffectWorkingScalePolicy? WorkingScalePolicy = null);

internal sealed record GeometryRenderFragmentPayload(
    GeometryDescription Description,
    object RuntimeIdentity,
    FilterEffectWorkingScalePolicy? WorkingScalePolicy = null);

internal sealed record LayerRenderFragmentPayload(Rect? Domain);

internal sealed record TargetLayerScopeRenderFragmentPayload(TargetRegion Region);

internal sealed record OpaqueRenderFragmentPayload(
    OpaqueRenderTopology Topology,
    OpaqueRenderDescription Description);

internal sealed record LegacyFilterEffectRenderFragmentPayload(
    RenderResource<FilterEffectContext> Context,
    ImmutableArray<IFEItem> BoundsItems,
    FilterEffectWorkingScalePolicy? WorkingScalePolicy = null);

internal sealed record MaterializedInputRenderFragmentPayload(
    MaterializedInputDescription Description);

internal sealed record TargetCaptureRenderFragmentPayload(
    TargetCaptureDescription Description);

internal sealed record BuiltInBackdropCaptureRenderFragmentPayload(
    TargetCaptureDescription Description,
    object Identity);

internal sealed record TargetScopeRenderFragmentPayload(
    TargetScopeDescription Description);

internal sealed record RawTargetScopeRenderFragmentPayload(
    RawTargetScopeDescription Description);

internal sealed record RawTargetCommandRenderFragmentPayload(
    RawTargetCommandDescription Description);

internal sealed record TargetCommandRenderFragmentPayload(
    TargetCommandDescription Description);

internal interface IBuiltInBackdropCaptureSink
{
    void CommitBackdropCapture(Bitmap bitmap, float density);
}
