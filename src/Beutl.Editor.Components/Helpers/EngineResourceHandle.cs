using Beutl.Engine;

namespace Beutl.Editor.Components.Helpers;

/// <summary>
/// A published engine resource, reachable only through <see cref="Read(Action{TResource})"/> and
/// <see cref="Project{TChild}"/> while its owner is held off. <see cref="Version"/> identifies the publication;
/// every access re-checks the resource, because a rebuild can release a child early.
/// </summary>
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
        return Read(resource =>
        {
            read(resource);
            return true;
        }, false);
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
    /// <paramref name="select"/> runs with the owner held off. A rebuild that drops the child leaves the handle
    /// reading as empty, while a child the rebuild kept goes on reading even after a version bump.
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
