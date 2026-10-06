using Beutl.Engine;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Paints one source fragment's content onto the canvas the engine hands it during execution.
/// </summary>
/// <typeparam name="TState">The type of the caller-supplied state the callback paints from.</typeparam>
/// <param name="canvas">
/// The canvas the fragment paints into. It is already positioned so that the coordinates the node used to
/// declare its output bounds are the coordinates the callback draws in.
/// </param>
/// <param name="fill">The resolved fill brush, or <see langword="null"/> when the source paints no interior.</param>
/// <param name="pen">The resolved stroke pen, or <see langword="null"/> when the source paints no outline.</param>
/// <param name="state">
/// The state the node handed to <see cref="RenderNodeContext"/>'s <c>PaintedSource</c>.
/// </param>
/// <remarks>
/// The callback runs during execution, long after <see cref="RenderNode.Process(RenderNodeContext)"/> returned, so
/// it must not capture the recording context or any handle obtained from it. Declare it as a static lambda over the
/// four parameters. The callback reaches execution through the state channel, so capturing does not change the
/// description's identity - which is exactly why it is unsafe: a captured per-frame value shapes pixels without
/// <see cref="RenderNode.MarkChanged"/> observing it, so the node reports itself clean while its output is stale.
/// BESG003 rejects a capturing callback here.
/// </remarks>
public delegate void PaintedSourceDraw<TState>(
    ImmediateCanvas canvas,
    Brush.Resource? fill,
    Pen.Resource? pen,
    TState state);

/// <summary>
/// Records declarative render fragments for one active <see cref="RenderNode.Process(RenderNodeContext)"/> call.
/// </summary>
/// <remarks>
/// The engine creates and seals each transaction. Methods record metadata only; deferred callbacks run later.
/// The context, its borrowed <see cref="Inputs"/>, and all handles obtained from it become invalid when the
/// process call returns. They do not own rendering resources and cannot be retained for a later request.
/// </remarks>
public sealed partial class RenderNodeContext
{
    private readonly NodeRecordingTransaction _transaction;
    private readonly RenderFragmentHandle[] _inputs;
    private readonly RenderIntent _intent;
    private readonly RenderRequestPurpose _purpose;
    private readonly Rect? _targetDomain;
    private readonly float _outputScale;
    private readonly float _maxWorkingScale;
    private readonly bool _supports3DRendering;
    private readonly Device3DExtentBudget _device3DExtentBudget;

    internal RenderNodeContext(NodeRecordingTransaction transaction)
    {
        _transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
        _inputs = transaction.InputHandles;
        _intent = transaction.Request.Options.Intent;
        _purpose = transaction.Request.Options.Purpose;
        _targetDomain = transaction.Request.Options.TargetDomain;
        _outputScale = transaction.Request.Options.OutputScale;
        _maxWorkingScale = transaction.Request.Options.MaxWorkingScale;
        _supports3DRendering = transaction.Request.Options.Supports3DRendering;
        _device3DExtentBudget = transaction.Request.Options.Device3DExtentBudget;
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

    /// <summary>Gets the optional finite logical domain available to root target accesses.</summary>
    public Rect? TargetDomain
    {
        get { VerifyActive(); return _targetDomain; }
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

    /// <summary>Gets whether the current request may expect a 3D-capable backend at execution.</summary>
    /// <remarks>
    /// A node whose output exists only on a 3D backend records nothing when this is <see langword="false"/>,
    /// so that its bounds, hit test, and cardinality describe what the request can actually produce. The
    /// value is request state settled before any node records - never a live probe of the process - and it
    /// turns <see langword="false"/> only once the process has established that no 3D backend exists; before
    /// a backend is built it is <see langword="true"/>, because the first allocation builds one.
    /// </remarks>
    public bool Supports3DRendering
    {
        get { VerifyActive(); return _supports3DRendering; }
    }

    /// <summary>Gets the 3D extents the current request may expect to allocate.</summary>
    /// <remarks>
    /// A node whose output exists only as a 3D attachment asks this while recording, because an extent past
    /// a limit is refused rather than drawn: recording it anyway would publish bounds and a hit test for a
    /// value the request goes on to drop, and a scene that is never drawn must not answer clicks. Like
    /// <see cref="Supports3DRendering"/> this is request state settled before any node records, never a live
    /// probe of the device, and <see cref="Device3DExtentBudget.Unreported"/> refuses nothing - the
    /// allocation still decides.
    /// </remarks>
    public Device3DExtentBudget Device3DExtentBudget
    {
        get { VerifyActive(); return _device3DExtentBudget; }
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
        for (int index = 0; index < _inputs.Length; index++)
        {
            RenderFragmentReference reference = _transaction.GetReference(_inputs[index]);
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

    /// <summary>
    /// Attempts to compute the finite target domain that covers everything the current inputs put on the target.
    /// </summary>
    /// <param name="domain">
    /// When this method returns <see langword="true"/>, the union of every value-contributing input's recorded
    /// bounds with the resolved bounds of every target write the inputs perform; otherwise
    /// <see cref="Rect.Empty"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when every value-contributing input has concrete recording metadata and every
    /// input's target write resolves to a finite region; <see langword="false"/> when any of them is still
    /// symbolic.
    /// </returns>
    /// <remarks>
    /// Valid only during <see cref="RenderNode.Process(RenderNodeContext)"/>, on the recording context passed to
    /// that call. A node that isolates its inputs into an off-screen layer uses this to choose between the finite
    /// <see cref="Layer(IReadOnlyList{RenderFragmentHandle}, Rect, bool)"/> and
    /// <see cref="OwningTargetLayer(IReadOnlyList{RenderFragmentHandle})"/>, whose domain is instead resolved from
    /// the enclosing target after recording. Unlike <see cref="TryCalculateInputBounds"/>, this accounts for target
    /// writes, so an input that clears or paints target pixels still yields a finite domain whenever the surrounding
    /// scopes bound that write. A returned domain of zero width or height means the inputs cover nothing, and the
    /// node can pass through instead of isolating.
    /// </remarks>
    public bool TryCalculateFiniteIsolationDomain(out Rect domain)
    {
        VerifyActive();
        Rect result = default;
        for (int index = 0; index < _inputs.Length; index++)
        {
            RenderFragmentReference reference = _transaction.GetReference(_inputs[index]);
            if (!TryUnionFiniteFootprint(reference, reference.ContributesValuesToTarget, ref result))
            {
                domain = default;
                return false;
            }
        }

        domain = result;
        return true;
    }

    /// <summary>Monotonically disables persistent render caching for the current node transaction.</summary>
    /// <remarks>
    /// A node that records a child it does not list in <see cref="RenderNode.ChildNodes"/> must call this,
    /// because the cache cannot observe a change reported only by that unlisted child.
    /// </remarks>
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

    /// <summary>Abandons a recorded fragment so it is neither published nor executed.</summary>
    /// <remarks>Required for a target-effect fragment recorded only to inspect its metadata.</remarks>
    /// <param name="fragment">A non-null unpublished handle borrowed from the active transaction.</param>
    public void Drop(RenderFragmentHandle fragment)
        => GetTransaction().Drop(fragment);

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

    /// <summary>Maps every current input to one output and publishes the mapped outputs in input order.</summary>
    /// <param name="mapper">
    /// A synchronous callback that returns one active, unpublished handle for each borrowed input without
    /// publishing fragments itself.
    /// </param>
    /// <remarks>
    /// This is explicit publication for a one-to-one input transform. An empty input list invokes no callbacks and
    /// publishes no output. Use <see cref="Publish"/>, <see cref="PublishRange"/>, or <see cref="PassThrough"/>
    /// directly for other topologies or publication orders.
    /// </remarks>
    public void PublishMappedInputs(Func<RenderFragmentHandle, RenderFragmentHandle> mapper)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        PublishMappedInputs(mapper, static (_, input, callback) => callback(input));
    }

    /// <summary>Maps every current input to one output and publishes the mapped outputs in input order.</summary>
    /// <typeparam name="TState">The callback state supplied for every input.</typeparam>
    /// <param name="state">The callback state supplied for every input.</param>
    /// <param name="mapper">
    /// A synchronous callback that returns one active, unpublished handle for each borrowed input without
    /// publishing fragments itself.
    /// </param>
    /// <remarks>
    /// Pass explicit state with a <see langword="static"/> callback when the recording path must avoid a
    /// per-call capture. The context and every input handle remain transaction-scoped and must not be retained.
    /// </remarks>
    public void PublishMappedInputs<TState>(
        TState state,
        Func<RenderNodeContext, RenderFragmentHandle, TState, RenderFragmentHandle> mapper)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        NodeRecordingTransaction transaction = GetTransaction();
        foreach (RenderFragmentHandle input in _inputs)
        {
            int publicationCount = transaction.PublicationCount;
            RenderFragmentHandle mapped = mapper(this, input, state);
            if (transaction.PublicationCount != publicationCount)
            {
                throw new InvalidOperationException(
                    "A PublishMappedInputs mapper must return its output without publishing fragments.");
            }

            transaction.Publish(mapped);
        }
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
            bindingResource = transaction.Own(binding);
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
        catch (Exception ex)
        {
            if (bindingResource is not null)
            {
                _ = transaction.RollbackResourcesAndCapture([bindingResource], ex);
            }
            else
            {
                transaction.Request.Options.Owner.RecordPrimaryFailure(ex);
                try
                {
                    binding.Dispose();
                }
                catch (Exception cleanupFailure)
                {
                    transaction.Request.Options.Owner.RecordCleanupFailure(cleanupFailure);
                }
            }

            throw;
        }
    }

    /// <summary>Transfers a disposable resource to the current request family.</summary>
    /// <typeparam name="T">The disposable resource type.</typeparam>
    /// <param name="resource">The non-null resource whose ownership is transferred.</param>
    /// <returns>A non-null declared resource handle owned by the request family.</returns>
    /// <remarks>
    /// Ownership transfers when this method succeeds. The family disposes the resource exactly once on rollback,
    /// failure, or normal completion.
    /// </remarks>
    public RenderResource<T> Own<T>(T resource)
        where T : class, IDisposable
        => GetTransaction().Own(resource);

    /// <summary>Registers a caller-owned resource that the current request may borrow.</summary>
    /// <typeparam name="T">The resource type.</typeparam>
    /// <param name="resource">The non-null caller-owned resource.</param>
    /// <returns>A non-null declared resource handle that never transfers disposal ownership.</returns>
    /// <remarks>
    /// The request borrows the resource only for its active family and never disposes it. Resource registrations
    /// do not provide persistent render-cache identity; cache eligibility follows the node's change reporting.
    /// </remarks>
    public RenderResource<T> Borrow<T>(T resource)
        where T : class
        => GetTransaction().Borrow(resource);

    internal void RollbackResources(IReadOnlyList<RenderResource> resources)
        => GetTransaction().RollbackResources(resources);

    internal Exception? RollbackResourcesAndCapture(
        IReadOnlyList<RenderResource> resources,
        Exception primaryFailure)
        => GetTransaction().RollbackResourcesAndCapture(resources, primaryFailure);

    /// <summary>Reads the recording-time bounds and supply density already recorded for one fragment.</summary>
    /// <param name="fragment">A non-null handle borrowed from the active transaction.</param>
    /// <returns>The fragment's recorded bounds paired with the density at which it can supply values.</returns>
    /// <remarks>
    /// Valid only during <see cref="RenderNode.Process(RenderNodeContext)"/>, on the recording context passed to
    /// that call. This is a hint, not a guarantee: it reports what recording knows so far and never forces
    /// resolution, so a fragment whose metadata is still symbolic reports the conservative values recorded up to
    /// this point rather than its final ones. Use it to size or order work while recording; use
    /// <see cref="TryCalculateInputBounds"/> when the node must instead know whether concrete metadata exists at
    /// all.
    /// </remarks>
    public RenderFragmentMetadata GetRecordedMetadataHint(RenderFragmentHandle fragment)
    {
        RenderFragmentReference reference = GetTransaction().GetReference(fragment);
        return new RenderFragmentMetadata(reference.RecordedBounds, reference.RecordedEffectiveScale);
    }

    /// <summary>
    /// Attempts to compute the finite region that a set of already-recorded fragments covers on the target.
    /// </summary>
    /// <param name="fragments">
    /// A non-null list of handles borrowed from the active transaction. Unlike
    /// <see cref="TryCalculateFiniteIsolationDomain(out Rect)"/>, which always measures the node's own inputs, this
    /// measures whichever fragments the node names — typically ones it has just recorded itself.
    /// </param>
    /// <param name="extent">
    /// When this method returns <see langword="true"/>, the union of every value-producing fragment's recorded
    /// bounds with the resolved bounds of every target write those fragments perform; otherwise
    /// <see cref="Rect.Empty"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when every value-producing fragment has concrete recording metadata and every target
    /// write resolves to a finite region; <see langword="false"/> when any of them is still symbolic.
    /// </returns>
    /// <remarks>
    /// Valid only during <see cref="RenderNode.Process(RenderNodeContext)"/>, on the recording context passed to
    /// that call. A node that has recorded a sub-graph and now has to size something around it — a clip, a layer, a
    /// backdrop read — uses this to learn whether that sub-graph's footprint is knowable at recording time. A
    /// returned extent of zero width or height means the fragments cover nothing. A <see langword="false"/> result
    /// is not an error: it says the footprint is only known once the enclosing target is resolved, so the node must
    /// fall back on a target-resolved construct such as
    /// <see cref="OwningTargetLayer(IReadOnlyList{RenderFragmentHandle})"/> instead of a finite rectangle.
    /// </remarks>
    public bool TryCalculateRecordedOutputExtent(
        IReadOnlyList<RenderFragmentHandle> fragments,
        out Rect extent)
    {
        ArgumentNullException.ThrowIfNull(fragments);
        NodeRecordingTransaction transaction = GetTransaction();
        Rect result = default;
        foreach (RenderFragmentHandle fragment in fragments)
        {
            RenderFragmentReference reference = transaction.GetReference(fragment);
            if (!TryUnionFiniteFootprint(reference, reference.ValueCardinality.Maximum != 0, ref result))
            {
                extent = default;
                return false;
            }
        }

        extent = result;
        return true;
    }

    /// <summary>Unions the recorded bounds of every current input.</summary>
    /// <returns>
    /// The union of each input's recorded bounds, or <see cref="Rect.Empty"/> when the node has no inputs.
    /// </returns>
    /// <remarks>
    /// Valid only during <see cref="RenderNode.Process(RenderNodeContext)"/>, on the recording context passed to
    /// that call. This always returns a rectangle, where <see cref="TryCalculateInputBounds"/> reports failure for a
    /// symbolic input, so the result is a best-effort hint: it describes only recorded <em>value</em> bounds and
    /// therefore does not cover a full-target write. A node scoping its inputs by this rectangle must first ask
    /// <see cref="HasSymbolicInputTargetWrite"/> whether such a write exists.
    /// </remarks>
    public Rect CalculateRecordedInputBoundsHint()
    {
        NodeRecordingTransaction transaction = GetTransaction();
        Rect result = default;
        foreach (RenderFragmentHandle input in _inputs)
        {
            result = result.Union(transaction.GetReference(input).RecordedBounds);
        }

        return result;
    }

    /// <summary>
    /// Gets whether a recorded input writes target pixels that
    /// <see cref="CalculateRecordedInputBoundsHint"/> does not describe.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when at least one input performs a target write with no recording-time extent.
    /// </returns>
    /// <remarks>
    /// Valid only during <see cref="RenderNode.Process(RenderNodeContext)"/>, on the recording context passed to
    /// that call. This is the predicate that picks the region for
    /// <see cref="TargetLayerScope(IReadOnlyList{RenderFragmentHandle}, TargetRegion)"/>. A node that scopes its
    /// inputs by their recorded value bounds has to ask this first: a full-target write contributes no value
    /// bounds, so scoping by them alone turns the whole scope empty and drops the write. Answer
    /// <see langword="true"/> with <see cref="TargetRegion.Full"/> and <see langword="false"/> with
    /// <see cref="TargetRegion.Region(Rect)"/> over <see cref="CalculateRecordedInputBoundsHint"/>. Passing
    /// <see cref="TargetRegion.Full"/> unconditionally is not a safe shortcut — it makes bounds-dependent
    /// downstream work measure against the whole target.
    /// </remarks>
    public bool HasSymbolicInputTargetWrite()
    {
        NodeRecordingTransaction transaction = GetTransaction();
        foreach (RenderFragmentHandle input in _inputs)
        {
            if (transaction.GetReference(input).HasSymbolicTargetWrite)
                return true;
        }

        return false;
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

    // Adds one fragment's recorded value bounds (when it produces values) and its resolved target write to a
    // finite footprint; false when either is still symbolic, leaving the caller to discard the partial union.
    private static bool TryUnionFiniteFootprint(
        RenderFragmentReference reference,
        bool producesValues,
        ref Rect footprint)
    {
        if (producesValues)
        {
            if (!reference.HasConcreteRecordingMetadata)
                return false;

            footprint = footprint.Union(reference.RecordedBounds);
        }

        if (!TargetWriteMetadataResolver.TryResolveFinite(reference, out Rect? affectedBounds))
            return false;

        if (affectedBounds is { } affected)
            footprint = footprint.Union(affected);
        return true;
    }

    private void ValidateDescriptionResources(
        IReadOnlyList<RenderResource> resources,
        string parameterName)
        => ValidateDeclaredResources(resources, static resource => resource, parameterName);

    private void ValidateDescriptionResources(
        IReadOnlyList<RenderResourceBinding> resources,
        string parameterName)
        => ValidateDeclaredResources(resources, static binding => binding.Resource, parameterName);

    /// <remarks>
    /// A description declares its resources either as bare tokens or as slot bindings, so the resource is
    /// read through a selector rather than by projecting one list into the other. Every recording path below
    /// reaches this once per operation per frame, and the projection was an array allocated per call to be
    /// read once and dropped.
    /// </remarks>
    private void ValidateDeclaredResources<TDeclared>(
        IReadOnlyList<TDeclared> declared,
        Func<TDeclared, RenderResource> selectResource,
        string parameterName)
    {
        NodeRecordingTransaction transaction = GetTransaction();
        RenderRequestResourceRegistry registry = transaction.Request.Options.Owner.ResourceRegistry;
        for (int index = 0; index < declared.Count; index++)
        {
            ValidateDeclaredResource(selectResource(declared[index]), registry, parameterName);
        }
    }

    private static void ValidateDeclaredResource(
        RenderResource resource,
        RenderRequestResourceRegistry registry,
        string parameterName)
    {
        if (!ReferenceEquals(resource.Registry, registry)
            || resource.RegistrationState == RenderResourceRegistrationState.Released)
        {
            throw new ArgumentException(
                "Every declared render resource must belong to the active request family.",
                parameterName);
        }
    }

}
