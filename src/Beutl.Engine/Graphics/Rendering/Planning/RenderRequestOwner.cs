using System.Collections.Immutable;
using System.Runtime.ExceptionServices;

namespace Beutl.Graphics.Rendering;

internal sealed class RenderRequestOwner : IDisposable
{
    private readonly List<RenderOwnershipToken> _ownership = [];
    private readonly List<Exception> _secondaryFailures = [];
    private readonly List<Exception> _cleanupFailures = [];
    private readonly Dictionary<object, RenderFragmentReference> _builtInBackdropBindings =
        new(ReferenceEqualityComparer.Instance);
    private ExceptionDispatchInfo? _primaryFailure;

    public RenderRequestOwner()
    {
        ResourceRegistry = new RenderRequestResourceRegistry();
        RecordingFamily = new RenderRecordingFamily();
        _ownership.Add(new RenderOwnershipToken(this, ResourceRegistry.Dispose));
    }

    public ExceptionDispatchInfo? PrimaryFailure => _primaryFailure;

    public ImmutableArray<Exception> SecondaryFailures => [.. _secondaryFailures];

    public ImmutableArray<Exception> CleanupFailures => [.. _cleanupFailures];

    public bool IsCleanedUp { get; private set; }

    public RenderRequestResourceRegistry ResourceRegistry { get; }

    public RenderRecordingFamily RecordingFamily { get; }

    public void CommitBuiltInBackdropBindings(
        IEnumerable<BuiltInBackdropBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        if (IsCleanedUp)
            throw new InvalidOperationException("The render request owner has already begun cleanup.");

        foreach (BuiltInBackdropBinding binding in bindings)
            _builtInBackdropBindings[binding.Identity] = binding.Reference;
    }

    public bool TryGetBuiltInBackdrop(
        object identity,
        out RenderFragmentReference? reference)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return _builtInBackdropBindings.TryGetValue(identity, out reference);
    }

    public RenderOwnershipToken Register(Action cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        if (IsCleanedUp)
        {
            throw new InvalidOperationException("The render request owner has already begun cleanup.");
        }

        var token = new RenderOwnershipToken(this, cleanup);
        _ownership.Add(token);
        return token;
    }

    public void Discharge(RenderOwnershipToken token)
    {
        EnsurePendingToken(token);
        token.State = RenderOwnershipState.Discharged;
    }

    public void DischargeAfterAcceptedCacheTransfer(RenderOwnershipToken token)
    {
        EnsurePendingToken(token);
        token.State = RenderOwnershipState.CacheTransferred;
    }

    public void RecordPrimaryFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (_primaryFailure is null)
        {
            _primaryFailure = ExceptionDispatchInfo.Capture(exception);
        }
        else if (ReferenceEquals(_primaryFailure.SourceException, exception))
        {
            // Nested recording/request boundaries may observe the same exception while it
            // propagates. That is not an independent secondary failure.
            return;
        }
        else
        {
            _secondaryFailures.Add(exception);
        }
    }

    public void Cleanup()
    {
        if (IsCleanedUp)
        {
            return;
        }

        IsCleanedUp = true;
        _builtInBackdropBindings.Clear();
        for (int index = _ownership.Count - 1; index >= 0; index--)
        {
            RenderOwnershipToken token = _ownership[index];
            if (token.State != RenderOwnershipState.Pending)
            {
                continue;
            }

            token.State = RenderOwnershipState.Discharged;
            try
            {
                token.Cleanup();
            }
            catch (Exception ex)
            {
                RecordCleanupFailure(ex);
            }
        }
    }

    public void ThrowIfFailed()
    {
        _primaryFailure?.Throw();
    }

    public void Dispose()
    {
        Cleanup();
    }

    private void RecordCleanupFailure(Exception exception)
    {
        _cleanupFailures.Add(exception);
        if (_primaryFailure is null)
        {
            _primaryFailure = ExceptionDispatchInfo.Capture(exception);
        }
        else
        {
            _secondaryFailures.Add(exception);
        }
    }

    private void EnsurePendingToken(RenderOwnershipToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (!ReferenceEquals(token.Owner, this))
        {
            throw new InvalidOperationException("The ownership token belongs to a different render request owner.");
        }

        if (token.State != RenderOwnershipState.Pending)
        {
            throw new InvalidOperationException("The ownership token has already been discharged or transferred.");
        }

        if (IsCleanedUp)
        {
            throw new InvalidOperationException("The render request owner has already begun cleanup.");
        }
    }
}

internal sealed class RenderOwnershipToken
{
    public RenderOwnershipToken(RenderRequestOwner owner, Action cleanup)
    {
        Owner = owner;
        Cleanup = cleanup;
    }

    public RenderRequestOwner Owner { get; }

    public Action Cleanup { get; }

    public RenderOwnershipState State { get; set; }
}

internal enum RenderOwnershipState : byte
{
    Pending,
    Discharged,
    CacheTransferred,
}
