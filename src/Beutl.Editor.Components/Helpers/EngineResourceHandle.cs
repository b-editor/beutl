using Beutl.Engine;

namespace Beutl.Editor.Components.Helpers;

/// <summary>
/// A published engine resource that can only be read while its owner is holding still.
/// </summary>
/// <remarks>
/// <para>
/// The resource itself is never handed out: the only way to reach it is <see cref="Read(Action{TResource})"/>
/// and its siblings, which take the gate the owning dispatcher holds across the rebuild. A reader that
/// smuggles the resource out of that callback is back to racing the rebuild, which is the whole hazard this
/// type exists to close.
/// </para>
/// <para>
/// <see cref="Version"/> is the resource's version as of the publication that produced this handle, and is
/// what makes two handles over the same resource compare unequal once it has changed. It is not re-checked
/// on read: a read sees whatever the resource holds at that moment, consistently.
/// </para>
/// <para>
/// The gate records only the owning subscription's release, which is the end of the resource tree, not of any
/// one resource in it. A rebuild releases a child the moment it drops it, so every entry point re-checks the
/// resource it is about to lend as well.
/// </para>
/// </remarks>
public readonly struct EngineResourceHandle<TResource> : IEquatable<EngineResourceHandle<TResource>>
    where TResource : EngineObject.Resource
{
    private readonly EngineResourceGate? _gate;
    private readonly TResource? _resource;

    internal EngineResourceHandle(EngineResourceGate gate, TResource resource, int version)
    {
        _gate = gate;
        _resource = resource;
        Version = version;
    }

    /// <summary>The resource's version as of the publication that produced this handle.</summary>
    public int Version { get; }

    /// <summary>
    /// Runs <paramref name="read"/> against the resource with its owner held off.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when the resource has already been released, in which case
    /// <paramref name="read"/> never runs.
    /// </returns>
    /// <remarks>
    /// The owning dispatcher cannot rebuild the resource for as long as this runs, so keep it to reading the
    /// resource out; anything that waits on that dispatcher from inside it deadlocks. Letting go of the
    /// subscription from in here is allowed - the resource stays alive until this returns.
    /// </remarks>
    public bool Read(Action<TResource> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        if (_gate is null || _resource is null)
            return false;

        lock (_gate.SyncRoot)
        {
            if (IsGone)
                return false;

            _gate.EnterRead();
            try
            {
                read(_resource);
            }
            finally
            {
                _gate.ExitRead();
            }

            return true;
        }
    }

    /// <summary>
    /// Projects the resource to a value with its owner held off, or answers <paramref name="fallback"/> when
    /// the resource has already been released.
    /// </summary>
    /// <inheritdoc cref="Read(Action{TResource})" path="/remarks"/>
    public TResult Read<TResult>(Func<TResource, TResult> read, TResult fallback)
    {
        ArgumentNullException.ThrowIfNull(read);
        if (_gate is null || _resource is null)
            return fallback;

        lock (_gate.SyncRoot)
        {
            if (IsGone)
                return fallback;

            _gate.EnterRead();
            try
            {
                return read(_resource);
            }
            finally
            {
                _gate.ExitRead();
            }
        }
    }

    /// <summary>
    /// Reaches a resource this one owns and hands it back behind the same gate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A child is released with its parent, so it belongs behind the parent's gate rather than one of its
    /// own. <paramref name="select"/> runs with the owner held off, because reaching a child is itself a
    /// read.
    /// </para>
    /// <para>
    /// A child is also released well before its parent: a rebuild that replaces or drops one disposes it on
    /// the spot, leaving the gate open and this handle pointing at nothing. It reads as empty from then on,
    /// while a projection whose child the rebuild kept goes on reading - a version bump alone must not
    /// invalidate one, or every unrelated edit to the parent would blank the projection for a frame.
    /// </para>
    /// </remarks>
    public EngineResourceHandle<TChild>? Project<TChild>(Func<TResource, TChild?> select)
        where TChild : EngineObject.Resource
    {
        ArgumentNullException.ThrowIfNull(select);
        if (_gate is null || _resource is null)
            return null;

        lock (_gate.SyncRoot)
        {
            if (IsGone)
                return null;

            TChild? child;
            _gate.EnterRead();
            try
            {
                child = select(_resource);
            }
            finally
            {
                _gate.ExitRead();
            }

            return child is null || child.IsDisposed
                ? null
                : new EngineResourceHandle<TChild>(_gate, child, Version);
        }
    }

    /// <summary>Whether the resource behind this handle is gone. Call under <see cref="EngineResourceGate.SyncRoot"/>.</summary>
    private bool IsGone => _gate!.IsReleased || _resource!.IsDisposed;

    public bool Equals(EngineResourceHandle<TResource> other)
    {
        return ReferenceEquals(_gate, other._gate)
               && ReferenceEquals(_resource, other._resource)
               && Version == other.Version;
    }

    public override bool Equals(object? obj) => obj is EngineResourceHandle<TResource> other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_gate, _resource, Version);

    public static bool operator ==(EngineResourceHandle<TResource> left, EngineResourceHandle<TResource> right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(EngineResourceHandle<TResource> left, EngineResourceHandle<TResource> right)
    {
        return !left.Equals(right);
    }
}
