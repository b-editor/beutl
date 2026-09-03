using System.Diagnostics.CodeAnalysis;

namespace Beutl.Extensibility;

/// <summary>Opaque identity carried by a host-bound editor-context close capability.</summary>
/// <remarks>
/// Host tokens are compared by reference. A host creates one token and retains it for its
/// lifetime; contexts created by that host must expose the same instance through their close
/// capability. Independent host implementations use <see cref="TryAcquireContext"/> to claim each
/// context before publication and hold the returned lease through asynchronous teardown.
/// </remarks>
public sealed class EditorContextHostToken
{
    private readonly object _ownershipGate = new();
    private readonly Dictionary<IEditorContext, EditorContextOwnershipLease> _ownedContexts =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>Attempts to acquire exclusive ownership of an editor context.</summary>
    /// <remarks>
    /// An editor host must acquire this lease before publishing a context and retain it until the
    /// context has been unpublished and disposed. The operation is atomic across all hosts that use
    /// this token, including contexts whose close capability is exposed through a wrapper.
    /// </remarks>
    /// <param name="context">The context the host is preparing to publish.</param>
    /// <param name="lease">The exclusive ownership lease when the claim succeeds.</param>
    /// <returns><see langword="true"/> when ownership was acquired; otherwise <see langword="false"/>.</returns>
    public bool TryAcquireContext(
        IEditorContext context,
        [NotNullWhen(true)] out EditorContextOwnershipLease? lease)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!ReferenceEquals(context.CloseService.HostToken, this))
        {
            lease = null;
            return false;
        }

        lock (_ownershipGate)
        {
            if (_ownedContexts.ContainsKey(context))
            {
                lease = null;
                return false;
            }

            lease = new EditorContextOwnershipLease(this, context);
            _ownedContexts.Add(context, lease);
            return true;
        }
    }

    internal void Release(IEditorContext context, EditorContextOwnershipLease lease)
    {
        lock (_ownershipGate)
        {
            if (_ownedContexts.TryGetValue(context, out EditorContextOwnershipLease? current)
                && ReferenceEquals(current, lease))
            {
                _ownedContexts.Remove(context);
            }
        }
    }
}

/// <summary>Represents a host's exclusive ownership of one editor context.</summary>
/// <remarks>
/// Dispose the lease only after the context is no longer published and its asynchronous disposal
/// has completed. Disposal is idempotent.
/// </remarks>
public sealed class EditorContextOwnershipLease : IDisposable
{
    private readonly IEditorContext _context;
    private EditorContextHostToken? _hostToken;

    internal EditorContextOwnershipLease(
        EditorContextHostToken hostToken,
        IEditorContext context)
    {
        _hostToken = hostToken;
        _context = context;
    }

    /// <summary>Releases the ownership claim.</summary>
    public void Dispose()
    {
        EditorContextHostToken? current = Interlocked.Exchange(ref _hostToken, null);
        current?.Release(_context, this);
    }
}
