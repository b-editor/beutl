namespace Beutl.Graphics.Rendering;

/// <summary>
/// Identifies a fragment recorded by the active <see cref="RenderNodeContext"/> transaction.
/// </summary>
/// <remarks>
/// A handle is a borrowed, non-executable view of one ordered fragment stream; it is not necessarily
/// one bitmap and does not own resources. Handles are transaction-scoped. Every public member throws
/// <see cref="InvalidOperationException"/> after the owning node's
/// <see cref="RenderNode.Process(RenderNodeContext)"/> call completes.
/// </remarks>
public sealed class RenderFragmentHandle
{
    private readonly IRenderFragmentHandleOwner _owner;
    private readonly RenderFragmentReference _reference;

    internal RenderFragmentHandle(
        IRenderFragmentHandleOwner owner,
        RenderFragmentReference reference)
    {
        _owner = owner;
        _reference = reference;
    }

    /// <summary>Tries to get concrete recording-time bounds and effective-scale metadata.</summary>
    /// <param name="metadata">
    /// Receives the concrete metadata, or <see langword="default"/> when the fragment still depends on an
    /// unresolved owning target domain.
    /// </param>
    /// <returns><see langword="true"/> when <paramref name="metadata"/> is concrete and author-readable.</returns>
    /// <remarks>This method does not execute deferred work or resolve graph-wide regions of interest.</remarks>
    public bool TryGetMetadata(out RenderFragmentMetadata metadata)
    {
        VerifyActive();
        if (!_reference.HasConcreteRecordingMetadata)
        {
            metadata = default;
            return false;
        }

        metadata = new RenderFragmentMetadata(
            _reference.RecordedBounds,
            _reference.RecordedEffectiveScale);
        return true;
    }

    /// <summary>Gets the declared number of materializable values the fragment may produce.</summary>
    public RenderValueCardinality ValueCardinality
    {
        get
        {
            VerifyActive();
            return _reference.ValueCardinality;
        }
    }

    /// <summary>Gets whether publishing the fragment automatically composites its values into the target.</summary>
    /// <remarks>
    /// A value may be non-contributing, and a target-effect fragment may still mutate or read the target
    /// when this property is <see langword="false"/>.
    /// </remarks>
    public bool ContributesValuesToTarget
    {
        get
        {
            VerifyActive();
            return _reference.ContributesValuesToTarget;
        }
    }

    /// <summary>Gets whether the complete fragment stream may be consumed by another value-producing fragment.</summary>
    /// <remarks>
    /// This is conservative recording metadata, not a promise that the fragment is pure or independent of
    /// target-token dependencies.
    /// </remarks>
    public bool CanBeUsedAsValueInput
    {
        get
        {
            VerifyActive();
            return _reference.CanBeUsedAsValueInput;
        }
    }

    /// <summary>Tries to evaluate the fragment's concrete recorded CPU-only hit-test contract.</summary>
    /// <param name="point">The point in the fragment's request coordinate space.</param>
    /// <param name="result">
    /// Receives the hit-test result, or <see langword="false"/> when the fragment still depends on an unresolved
    /// owning target domain.
    /// </param>
    /// <returns><see langword="true"/> when <paramref name="result"/> was evaluated from concrete metadata.</returns>
    /// <remarks>This method does not execute deferred rendering or pixel readback.</remarks>
    public bool TryHitTest(Point point, out bool result)
    {
        VerifyActive();
        bool concrete = _reference.HasConcreteRecordingMetadata;
        result = concrete && _reference.HitTest(point);
        _owner.NoteHitTestRead(_reference, point, concrete, result);
        return concrete;
    }

    internal RenderFragmentReference GetReference(IRenderFragmentHandleOwner owner)
    {
        VerifyActive();
        if (!ReferenceEquals(_owner, owner))
        {
            throw new InvalidOperationException(
                "The render fragment handle belongs to a different recording transaction.");
        }

        return _reference;
    }

    private void VerifyActive()
    {
        _owner.VerifyActive();
        _owner.VerifyOwns(_reference);
    }
}

/// <summary>How a fragment answers a hit test, stated so that it can be re-evaluated over any inputs.</summary>
internal enum RenderFragmentHitTestKind : byte
{
    /// <summary>Never hits.</summary>
    None,

    /// <summary>Hits everywhere inside the fragment's own bounds.</summary>
    Bounds,

    /// <summary>Hits everywhere inside a fixed region the fragment carries.</summary>
    Region,

    /// <summary>Hits wherever any input hits.</summary>
    Inputs,

    /// <summary>Hits wherever an input hits inside a fixed region the fragment carries.</summary>
    RegionAndInputs,

    /// <summary>Hits where an author-declared contract says, read over the fragment's bounds and inputs.</summary>
    Contract,
}
