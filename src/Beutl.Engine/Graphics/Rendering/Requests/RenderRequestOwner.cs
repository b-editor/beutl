using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Graphics.Rendering.Requests;

internal sealed class RenderRequestOwner : IDisposable
{
    private static ILogger? s_logger;
    private List<Exception>? _secondaryFailures;
    private int _reportedSecondaryFailureCount;
    private bool _primaryFailureReported;
    private List<Exception>? _cleanupFailures;
    private Dictionary<object, RenderFragmentReference>? _builtInBackdropBindings;
    private ExceptionDispatchInfo? _primaryFailure;

    private static ILogger Logger => Log.GetLoggerOnceConfigured(ref s_logger, typeof(RenderRequestOwner));

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

    // Only one exception reaches the caller, so log the failures it masks before it is thrown: the
    // secondary failures, and the primary failure itself when the caller throws a different exception.
    public void ReportMaskedFailures(Exception thrown)
    {
        ArgumentNullException.ThrowIfNull(thrown);
        if (!_primaryFailureReported
            && _primaryFailure is not null
            && !ReferenceEquals(_primaryFailure.SourceException, thrown))
        {
            _primaryFailureReported = true;
            ReportMaskedFailure(_primaryFailure.SourceException);
        }

        if (_secondaryFailures is null)
        {
            return;
        }

        for (; _reportedSecondaryFailureCount < _secondaryFailures.Count; _reportedSecondaryFailureCount++)
        {
            Exception failure = _secondaryFailures[_reportedSecondaryFailureCount];
            if (!ReferenceEquals(failure, thrown))
            {
                ReportMaskedFailure(failure);
            }
        }
    }

    private static void ReportMaskedFailure(Exception failure)
    {
        try
        {
            Logger.LogWarning(failure, "A render request failure was suppressed by the failure being thrown.");
        }
        catch
        {
            // Logging must not replace the render failure that is about to be thrown.
        }
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
