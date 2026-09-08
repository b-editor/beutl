using System.Collections.Immutable;
using System.Runtime.ExceptionServices;

namespace Beutl.Graphics.Rendering.Requests;

internal sealed class RenderRequestOwner : IDisposable
{
    private List<Exception>? _secondaryFailures;
    private List<Exception>? _cleanupFailures;
    private Dictionary<object, RenderFragmentReference>? _builtInBackdropBindings;
    private ExceptionDispatchInfo? _primaryFailure;

    public ExceptionDispatchInfo? PrimaryFailure => _primaryFailure;

    public ImmutableArray<Exception> SecondaryFailures
        => _secondaryFailures is null ? [] : [.. _secondaryFailures];

    public ImmutableArray<Exception> CleanupFailures
        => _cleanupFailures is null ? [] : [.. _cleanupFailures];

    public bool IsCleanedUp { get; private set; }

    public RenderRequestResourceRegistry ResourceRegistry { get; } = new();

    public RenderRecordingFamily RecordingFamily { get; } = new();

    public void CommitBuiltInBackdropBindings(
        IEnumerable<BuiltInBackdropBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        if (IsCleanedUp)
            throw new InvalidOperationException("The render request owner has already begun cleanup.");

        foreach (BuiltInBackdropBinding binding in bindings)
        {
            (_builtInBackdropBindings ??= new(ReferenceEqualityComparer.Instance))[binding.Identity] =
                binding.Reference;
        }
    }

    public bool TryGetBuiltInBackdrop(
        object identity,
        out RenderFragmentReference? reference)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (_builtInBackdropBindings is null)
        {
            reference = null;
            return false;
        }

        return _builtInBackdropBindings.TryGetValue(identity, out reference);
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
            (_secondaryFailures ??= []).Add(exception);
        }
    }

    public void Cleanup()
    {
        if (IsCleanedUp)
        {
            return;
        }

        IsCleanedUp = true;
        _builtInBackdropBindings = null;
        try
        {
            ResourceRegistry.Dispose();
        }
        catch (Exception ex)
        {
            RecordCleanupFailure(ex);
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

    internal void RecordCleanupFailure(Exception exception)
    {
        (_cleanupFailures ??= []).Add(exception);
        if (_primaryFailure is null)
        {
            _primaryFailure = ExceptionDispatchInfo.Capture(exception);
        }
        else
        {
            (_secondaryFailures ??= []).Add(exception);
        }
    }
}
