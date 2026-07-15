using Beutl.Engine;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering.Cache;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// The single-op output <see cref="PlanExecutor"/> retains for the pass-prefix output cache (feature 004, C10). A
/// full execution capturing pass <see cref="CapturePassIndex"/> shallow-copies that pass's pooled buffer here so
/// the retained ref keeps it alive across frames; the next stable frame resumes from the following pass with this
/// buffer as its input, so the retained prefix's passes never re-execute.
/// </summary>
internal sealed class PrefixCaptureSink : IDisposable
{
    public int CapturePassIndex { get; init; } = -1;

    public RenderTarget? CapturedTarget { get; private set; }

    public Rect CapturedBounds { get; private set; }

    public EffectiveScale CapturedScale { get; private set; }

    public bool Captured => CapturedTarget != null;

    public void Capture(RenderTarget target, Rect bounds, EffectiveScale scale)
    {
        if (CapturedTarget != null)
            return;

        CapturedTarget = target.ShallowCopy();
        CapturedBounds = bounds;
        CapturedScale = scale;
    }

    /// <summary>
    /// Transfers the captured buffer's ownership out of the sink (the cache adopts it). After this the sink no
    /// longer references or disposes the buffer, so a subsequent <see cref="Dispose"/> is a no-op — keeping
    /// ownership single: <see cref="EffectPrefixCache.StoreCaptured"/> adopts on success, <see cref="Dispose"/>
    /// releases when a later pass threw before adoption.
    /// </summary>
    public RenderTarget? Adopt()
    {
        RenderTarget? target = CapturedTarget;
        CapturedTarget = null;
        return target;
    }

    /// <summary>
    /// Releases the shallow-copy ref taken by <see cref="Capture"/> if it was never adopted — the exception-safe
    /// path when a pass after the capture pass throws, so the pooled buffer's lease is not stranded (C7).
    /// Idempotent and a no-op once <see cref="Adopt"/> has transferred ownership.
    /// </summary>
    public void Dispose()
    {
        RenderTarget? target = CapturedTarget;
        CapturedTarget = null;
        target?.Dispose();
    }
}

/// <summary>How <see cref="EffectPrefixCache"/> wants the current frame executed.</summary>
internal enum PrefixMode
{
    /// <summary>No stable prefix; execute the whole plan from pass 0.</summary>
    None,

    /// <summary>Execute the whole plan from pass 0, capturing pass <see cref="PrefixDecision.Pass"/>'s output.</summary>
    Capture,

    /// <summary>Skip passes 0..k, seeding the cached prefix output as the input to pass <see cref="PrefixDecision.Pass"/>.</summary>
    Resume,
}

internal readonly record struct PrefixDecision(
    PrefixMode Mode, int Pass, RenderTarget? SeedTarget, Rect SeedBounds, EffectiveScale SeedScale)
{
    public static readonly PrefixDecision None = new(PrefixMode.None, 0, null, default, default);
}

/// <summary>
/// Per-node pass-prefix output cache (feature 004, contracts/execution-plan.md §C10). It restores the pre-004
/// granularity where a heavy static prefix of an effect chain (a blur, a stroke) was cached while only the animated
/// tail re-rendered — a granularity the 004 un-flattening (one render node per group, required for fusion/SC-001)
/// lost because any animated child now marks the whole node changed every frame, so the outer
/// <see cref="RenderNodeCache"/> never engages and the static prefix re-executes every frame.
/// </summary>
/// <remarks>
/// The cache retains the output of the last <em>capturable linear</em> pass (a <see cref="FusedShaderPass"/> or
/// <see cref="SkiaFilterPass"/>, before any split/composite/nested/dynamic pass — the v1 scope) whose whole
/// provenance range lies within the leading run of children stable for at least <see cref="EngageThreshold"/>
/// frames. Engagement additionally requires an unchanged <see cref="StructuralKey"/>, graphics context, resolved
/// working scale, input-bounds signature, and a stable input subtree (the same predicate the node cache uses). Any
/// of those changing releases the retained buffer and re-warms, so a stale prefix is never replayed.
/// </remarks>
internal sealed class EffectPrefixCache : IDisposable
{
    /// <summary>Consecutive stable frames a child must show before it may join the reusable prefix (matches <see cref="RenderNodeCache.Count"/>).</summary>
    private const int EngageThreshold = RenderNodeCache.Count;

    private (EngineObject.Resource Resource, int Version)[]? _lastChildVersions;
    private int[] _childStableFrames = [];

    private RenderTarget? _retainedTarget;
    private PrefixRetentionBudget? _retentionBudget;
    private Rect _bounds;
    private EffectiveScale _scale;
    private int _resumeFromPass = -1;
    private int _entryMaxChild = -1;

    private bool _hasSignature;
    private StructuralKey _key;
    private object? _contextId;
    private float _workingScale;

    // OutputScale and MaxWorkingScale reach captured pixels outside workingScale: a deferred child bakes at the
    // describe-time MaxWorkingScale (DisplacementMapTransform's map), so a supply-pinned w with a changed zoom
    // policy would otherwise resume from a buffer rendered under the previous scale policy.
    private float _outputScale;
    private float _maxWorkingScale;
    private RenderIntent _renderIntent;

    // The bounds + supply density of the ops that fed the plan on the last engagement frame, compared EXACTLY (not
    // a hash) so no collision can ever contribute to a false stability match (C10 input-bounds gate). The buffer is
    // reused across frames; _inputSignatureCount is the valid element count (-1 until the first capture).
    private (Rect Bounds, EffectiveScale Scale)[] _inputSignature = [];
    private int _inputSignatureCount = -1;

    // The resolved ROIs/sizes of passes 0..capturePass at capture time. The structural key excludes animated
    // parameters, but a tail pass's backward ROI is parameter-dependent (an animated Clipping/DropShadow moves the
    // prefix's resolved ROI), so a resumed frame must re-check that the captured buffer still covers what the tail
    // reads — otherwise it would seed a stale, differently-cropped prefix output.
    private PassResolution[] _resolutionSignature = [];
    private int _resolutionSignatureCount;

    public PrefixDecision Prepare(
        FilterEffect.Resource resource, CompiledPlan plan, StructuralKey key, object contextId,
        float workingScale, float outputScale, float maxWorkingScale, RenderIntent renderIntent,
        ReadOnlySpan<RenderNodeOperation> input, bool inputSubtreeStable,
        FrameResources resources)
    {
        bool signatureMatch = _hasSignature
            && ReferenceEquals(_contextId, contextId)
            && _key == key
            && _workingScale == workingScale
            && _outputScale == outputScale
            && _maxWorkingScale == maxWorkingScale
            && _renderIntent == renderIntent
            && InputSignatureEquals(input);

        // Any signature change or input-subtree instability voids the cached prefix's assumptions: release the
        // retained buffer and restart stability tracking so nothing stale is reused (C10 invalidation list).
        if (!signatureMatch || !inputSubtreeStable)
        {
            ReleaseEntry();
            ResetStability();
            _hasSignature = true;
            _key = key;
            _contextId = contextId;
            _workingScale = workingScale;
            _outputScale = outputScale;
            _maxWorkingScale = maxWorkingScale;
            _renderIntent = renderIntent;
            CaptureInputSignature(input);
        }

        UpdateChildStability(resource);

        if (!inputSubtreeStable)
            return PrefixDecision.None;

        int stableChildren = LongestStableLeadingRun();

        if (_retainedTarget != null)
        {
            // Reuse the retained prefix while its whole provenance range is still stable AND the prefix passes
            // resolve to the same ROIs/sizes this frame; a destabilized prefix child drops stableChildren at or
            // below _entryMaxChild, and a tail-driven ROI change shifts the resolution slice — either invalidates
            // the buffer and re-warms.
            if (stableChildren > _entryMaxChild && ResolutionSignatureEquals(resources))
            {
                _retentionBudget?.Touch(this);
                return new PrefixDecision(PrefixMode.Resume, _resumeFromPass, _retainedTarget, _bounds, _scale);
            }

            ReleaseEntry();
        }

        int k = ComputeCapturablePass(plan, stableChildren);
        return k >= 0
            ? new PrefixDecision(PrefixMode.Capture, k, null, default, default)
            : PrefixDecision.None;
    }

    /// <summary>Records the captured prefix output after a <see cref="PrefixMode.Capture"/> execution completed.</summary>
    public void StoreCaptured(
        PrefixCaptureSink sink, int capturePass, CompiledPlan plan, FrameResources resources, RenderTargetPool pool)
    {
        if (!sink.Captured)
            return;

        ReleaseEntry();
        _bounds = sink.CapturedBounds;
        _scale = sink.CapturedScale;
        _retainedTarget = sink.Adopt();
        if (_retainedTarget == null)
            return;
        _resumeFromPass = capturePass + 1;
        _entryMaxChild = plan.Passes[capturePass].ProvenanceMaxChild;
        CaptureResolutionSignature(resources, capturePass);

        _retentionBudget = pool.PrefixRetentionBudget;
        long retainedBytes = checked((long)_retainedTarget.Width * _retainedTarget.Height * 8);
        try
        {
            if (!_retentionBudget.TryRetain(this, retainedBytes))
                ReleaseEntry();
        }
        catch
        {
            ReleaseEntry();
            throw;
        }
    }

    /// <summary>
    /// Releases the retained cross-frame lease when the node's outer <see cref="Cache.RenderNodeCache"/> takes over
    /// the serving path (C10): once the outer cache replays tiles, this node's <c>Process</c> no longer runs, so a
    /// still-held prefix lease would be stranded outside every pool budget. Re-warms from scratch on the next miss
    /// if the outer cache later invalidates.
    /// </summary>
    public void Release() => ReleaseEntry();

    public void Dispose() => ReleaseEntry();

    // The last capturable linear pass whose whole provenance range lies within the stable leading run. The prefix
    // ends at the first non-fused/Skia (or full-frame / dynamic) pass — the C10 v1 linear-prefix scope —
    // and never covers the whole plan (the all-stable case belongs to the outer node cache, so keep a tail pass).
    private static int ComputeCapturablePass(CompiledPlan plan, int stableChildren)
    {
        if (stableChildren <= 0)
            return -1;

        var passes = plan.Passes;
        int n = passes.Length;
        int k = -1;
        for (int p = 0; p < n; p++)
        {
            CompiledPass pass = passes[p];
            if (!IsCapturable(pass) || pass.ProvenanceMaxChild < 0 || pass.ProvenanceMaxChild >= stableChildren)
                break;

            k = p;
        }

        return k < n - 1 ? k : -1;
    }

    private static bool IsCapturable(CompiledPass pass)
        => pass is FusedShaderPass or SkiaFilterPass && !pass.RequiresFullInput && !pass.IsDynamicOutputs;

    private void UpdateChildStability(FilterEffect.Resource resource)
    {
        (EngineObject.Resource Resource, int Version)[] current = CaptureChildVersions(resource);
        if (_lastChildVersions == null || _lastChildVersions.Length != current.Length)
        {
            _childStableFrames = new int[current.Length];
        }
        else
        {
            for (int i = 0; i < current.Length; i++)
            {
                bool same = ReferenceEquals(current[i].Resource, _lastChildVersions[i].Resource)
                    && current[i].Version == _lastChildVersions[i].Version;
                _childStableFrames[i] = same ? _childStableFrames[i] + 1 : 0;
            }
        }

        _lastChildVersions = current;
    }

    private int LongestStableLeadingRun()
    {
        int run = 0;
        for (int i = 0; i < _childStableFrames.Length; i++)
        {
            if (_childStableFrames[i] < EngageThreshold)
                break;

            run++;
        }

        return run;
    }

    private static (EngineObject.Resource Resource, int Version)[] CaptureChildVersions(FilterEffect.Resource resource)
    {
        // A group exposes its top-level child resources (each with its own Version); a non-group effect is one child.
        if (resource is FilterEffectGroup.Resource group)
        {
            List<FilterEffect.Resource> children = group.Children;
            var versions = new (EngineObject.Resource, int)[children.Count];
            for (int i = 0; i < children.Count; i++)
                versions[i] = (children[i], children[i].Version);

            return versions;
        }

        return [(resource, resource.Version)];
    }

    // Exact comparison of the current frame's resolution of passes 0..capturePass against the capture-time slice.
    // Record value-equality, so any ROI, device size, clamped scale, or skip flag drift releases the buffer.
    private bool ResolutionSignatureEquals(FrameResources resources)
    {
        if (_resolutionSignatureCount > resources.Passes.Length)
            return false;

        for (int i = 0; i < _resolutionSignatureCount; i++)
        {
            if (_resolutionSignature[i] != resources.Passes[i])
                return false;
        }

        return true;
    }

    private void CaptureResolutionSignature(FrameResources resources, int capturePass)
    {
        int count = capturePass + 1;
        if (_resolutionSignature.Length < count)
            _resolutionSignature = new PassResolution[count];

        for (int i = 0; i < count; i++)
            _resolutionSignature[i] = resources.Passes[i];

        _resolutionSignatureCount = count;
    }

    // Order-sensitive exact comparison of the current input ops' bounds + supply density against the retained
    // signature. Exact so a differing input geometry/density can never alias to a match and replay a stale prefix.
    private bool InputSignatureEquals(ReadOnlySpan<RenderNodeOperation> input)
    {
        if (_inputSignatureCount != input.Length)
            return false;

        for (int i = 0; i < input.Length; i++)
        {
            (Rect Bounds, EffectiveScale Scale) prev = _inputSignature[i];
            if (prev.Bounds != input[i].Bounds || prev.Scale != input[i].EffectiveScale)
                return false;
        }

        return true;
    }

    // Rewrites the retained signature into the reused buffer (grown only when the input set is longer than before),
    // so the steady-state per-frame compare allocates nothing.
    private void CaptureInputSignature(ReadOnlySpan<RenderNodeOperation> input)
    {
        if (_inputSignature.Length < input.Length)
            _inputSignature = new (Rect, EffectiveScale)[input.Length];

        for (int i = 0; i < input.Length; i++)
            _inputSignature[i] = (input[i].Bounds, input[i].EffectiveScale);

        _inputSignatureCount = input.Length;
    }

    private void ResetStability()
    {
        _lastChildVersions = null;
        _childStableFrames = [];
    }

    private void ReleaseEntry()
    {
        PrefixRetentionBudget? budget = _retentionBudget;
        _retentionBudget = null;
        budget?.Release(this);
        DisposeRetainedTarget();
    }

    internal void EvictFromBudget(PrefixRetentionBudget budget)
    {
        if (!ReferenceEquals(_retentionBudget, budget))
            return;

        _retentionBudget = null;
        DisposeRetainedTarget();
    }

    private void DisposeRetainedTarget()
    {
        RenderTarget? target = _retainedTarget;
        _retainedTarget = null;
        _resumeFromPass = -1;
        _entryMaxChild = -1;
        target?.Dispose();
    }
}

/// <summary>
/// Renderer-scoped byte-budget and LRU for prefix outputs retained by otherwise-independent render nodes.
/// Entries are live pool leases, so they are accounted separately from idle buffers and evicted before the retained
/// set can grow past the renderer's cap.
/// </summary>
internal sealed class PrefixRetentionBudget(long maxBytes)
{
    private readonly record struct Entry(EffectPrefixCache Owner, long Bytes);

    private readonly Dictionary<EffectPrefixCache, LinkedListNode<Entry>> _nodes = [];
    private readonly LinkedList<Entry> _lru = [];
    private long _retainedBytes;

    public long RetainedBytes => _retainedBytes;

    public int Count => _nodes.Count;

    public bool TryRetain(EffectPrefixCache owner, long bytes)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (bytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(bytes));
        if (bytes > maxBytes)
            return false;

        Release(owner);
        LinkedListNode<Entry> node = _lru.AddLast(new Entry(owner, bytes));
        _nodes.Add(owner, node);
        _retainedBytes += bytes;

        Exception? failure = null;
        while (_retainedBytes > maxBytes && _lru.First is { } victim)
        {
            EffectPrefixCache victimOwner = victim.Value.Owner;
            Remove(victim);
            try
            {
                victimOwner.EvictFromBudget(this);
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }
        }

        if (failure != null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();

        return true;
    }

    public void Touch(EffectPrefixCache owner)
    {
        if (_nodes.TryGetValue(owner, out LinkedListNode<Entry>? node) && node != _lru.Last)
        {
            _lru.Remove(node);
            _lru.AddLast(node);
        }
    }

    public void Release(EffectPrefixCache owner)
    {
        if (_nodes.TryGetValue(owner, out LinkedListNode<Entry>? node))
            Remove(node);
    }

    public void Clear()
    {
        Exception? failure = null;
        while (_lru.First is { } node)
        {
            EffectPrefixCache owner = node.Value.Owner;
            Remove(node);
            try
            {
                owner.EvictFromBudget(this);
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }
        }

        if (failure != null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private void Remove(LinkedListNode<Entry> node)
    {
        _lru.Remove(node);
        _nodes.Remove(node.Value.Owner);
        _retainedBytes -= node.Value.Bytes;
    }
}
