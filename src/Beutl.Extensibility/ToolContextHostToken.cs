using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Beutl.Extensibility;

/// <summary>Coordinates exclusive ownership of tool contexts across editor hosts.</summary>
/// <remarks>
/// Each editor context creates one token for its tool host. Claim a tool context before publishing
/// it and retain the returned lease until asynchronous teardown completes. Claims are process-wide,
/// so the same tool instance cannot be published by two independent editor contexts.
/// </remarks>
public sealed class ToolContextHostToken
{
    private static readonly object s_ownershipGate = new();
    private static readonly ConditionalWeakTable<IToolContext, ToolContextOwnershipLease> s_owners = new();

    /// <summary>Attempts to acquire exclusive ownership of a tool context.</summary>
    public bool TryAcquireContext(
        IToolContext context,
        [NotNullWhen(true)] out ToolContextOwnershipLease? lease)
    {
        ArgumentNullException.ThrowIfNull(context);
        lock (s_ownershipGate)
        {
            if (s_owners.TryGetValue(context, out _))
            {
                lease = null;
                return false;
            }

            lease = new ToolContextOwnershipLease(context);
            s_owners.Add(context, lease);
            return true;
        }
    }

    internal static void Release(IToolContext context, ToolContextOwnershipLease lease)
    {
        lock (s_ownershipGate)
        {
            if (s_owners.TryGetValue(context, out ToolContextOwnershipLease? current)
                && ReferenceEquals(current, lease))
            {
                s_owners.Remove(context);
            }
        }
    }
}

/// <summary>Represents one editor tool host's exclusive claim on a tool context.</summary>
public sealed class ToolContextOwnershipLease : IDisposable
{
    private IToolContext? _context;

    internal ToolContextOwnershipLease(IToolContext context)
    {
        _context = context;
    }

    /// <summary>Releases the ownership claim. Disposal is idempotent.</summary>
    public void Dispose()
    {
        IToolContext? context = Interlocked.Exchange(ref _context, null);
        if (context is not null)
            ToolContextHostToken.Release(context, this);
    }
}
